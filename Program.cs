using System.Net;
using System.Text;
using System.Text.Json;

const string cacheFileName = "playlist.m3u";
const int bufferSize = 0x1000; //4KB
string currentURL = "";

try
{
    Globals.settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText("settings.json")) ?? throw new Exception();
}
catch
{
    Console.WriteLine("settings.json error");
    return;
}

string playList = GetPlaylist(Globals.settings.PlaylistURL);
HttpListener listener = new();
listener.Prefixes.Add($"http://*:{Globals.settings.Port}/");
listener.Start();
Console.WriteLine($"Listening on {Globals.IntUrl}...");
Task? restreamTask = null;
CancellationTokenSource cts = new();

while (true)
{
    HttpListenerContext context = listener.GetContext();
    if (context.Request.Url == null) continue;

    if (context.Request.Url.AbsolutePath == "/")
    {
        string exMsg = "";
        context.Response.ContentType = "application/x-mpegurl";
        try
        {
            using StreamWriter writer = new(context.Response.OutputStream);
            writer.Write(playList);
        }
        catch (Exception ex)
        {
            exMsg = ex.Message;
        }
        context.Response.Close();
        Console.WriteLine($"Send playlist : {(exMsg == "" ? "OK" : exMsg)}");
    }
    else
    {
        string videoUrl = context.Request.Url.AbsoluteUri.Replace(Globals.IntUrl, Globals.extUrl);
        context.Response.ContentType = "video/MP2T"; // MPEG-TS content type
        context.Response.SendChunked = true;

        if (restreamTask != null && !restreamTask.IsCompleted && videoUrl != currentURL)
        {
            cts.Cancel();
            await restreamTask;
            cts = new();
        }

        if (Globals.destinations.Count < Globals.settings.MaxConnections)
        {
            Globals.destinations.Add(context);
            currentURL = videoUrl;
            if (restreamTask == null || restreamTask.IsCompleted) restreamTask = Restream(videoUrl, cts.Token);
        }
        else context.Response.Close();
    }
}

static async Task Restream(string videoUrl, CancellationToken token)
{
    byte[] buffer = new byte[bufferSize];
    int bytesRead;
    List<Task> writeTasks = [];
    string exMsg = "";
    using HttpClient client = new();
    client.Timeout = TimeSpan.FromSeconds(30);
    Console.WriteLine($"Playing {videoUrl}");

    try
    {
        using HttpResponseMessage videoResponse = await client.GetAsync(videoUrl, HttpCompletionOption.ResponseHeadersRead, token);
        using Stream videoStream = await videoResponse.Content.ReadAsStreamAsync();
        while (Globals.destinations.Count > 0 && (bytesRead = await videoStream.ReadAsync(buffer, token)) > 0)
        {
            foreach (HttpListenerContext destination in Globals.destinations)
            {
                writeTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await destination.Response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                    }
                    catch
                    {
                        Console.WriteLine($"Stopped {destination.Request.RemoteEndPoint.Address}");
                        destination.Response.Close();
                        Globals.destinations.Remove(destination);
                    }
                }));
            }

            await Task.WhenAll(writeTasks);
            writeTasks.Clear();
        }
    }
    catch (Exception ex)
    {
        exMsg = ex.Message;
    }

    Console.WriteLine($"{(exMsg != "" ? exMsg : "Stopped")} {videoUrl}");

    foreach (HttpListenerContext destination in Globals.destinations) destination.Response.Close();
    Globals.destinations.Clear();
}

static string GetPlaylist(string playList)
{
    string content = "";
    if (File.Exists(cacheFileName) && File.GetLastWriteTime(cacheFileName).Date == DateTime.Today.Date)
        content = File.ReadAllText(cacheFileName);
    else
    {
        using HttpClient client = new();
        try
        {
            HttpResponseMessage response = client.GetAsync(playList).Result;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Console.WriteLine($"Unable to download playlist");
                return "";
            }
            content = response.Content.ReadAsStringAsync().Result;
            File.WriteAllText(cacheFileName, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Playlist error : {ex.Message}");
            return "";
        }
    }

    int counter = 0;
    StringBuilder sb = new();
    StringReader sr = new(content);
    sb.AppendLine(sr.ReadLine());
    string? line;
    while ((line = sr.ReadLine()) != null)
    {
        if (!Globals.settings.WhiteList.Exists(a => line.Contains(a)))
        {
            sr.ReadLine();
            continue;
        }
        sb.AppendLine(line);
        line = sr.ReadLine();
        if (line == null) continue;
        if (Globals.extUrl == "")
        {
            Uri uri = new(line);
            Globals.extUrl = $"{uri.Host}:{uri.Port}";
        }
        sb.AppendLine(line.Replace(Globals.extUrl, Globals.IntUrl));
        counter++;
    }

    Console.WriteLine($"Loaded {counter} channels");
    return sb.ToString();
}
