using System.Net;
using System.Text;

const string cacheFileName = "playlist.m3u";
const int maxConnections = 2;
const int bufferSize = 0x1000; //4KB
string playListURL = "", currentURL = "";

if (args.Length > 0)
{
    try { Uri uri = new(args[0]); playListURL = uri.ToString(); }
    catch (Exception ex) { Console.WriteLine($"Playlist Uri format error : {ex.Message}"); }
    if (args.Length == 2) Globals.whiteList = [.. args[1].ToString().Split(",")];
}
else
{
    Console.WriteLine("Usage : restream [M3U URL] [WhiteList (optional)]");
    return;
}

string playList = GetPlaylist(playListURL);
HttpListener listener = new();
listener.Prefixes.Add($"http://*:{Globals.port}/");
listener.Start();
HashSet<Task> requests = [];
for (int i = 0; i < maxConnections; i++) requests.Add(listener.GetContextAsync());
Console.WriteLine($"Listening on {Globals.intUrl}...");
Task? restreamTask = null;
CancellationTokenSource cts = new();

while (true)
{
    Task t = await Task.WhenAny(requests);
    HttpListenerContext context = ((Task<HttpListenerContext>)t).Result;
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
        string videoUrl = context.Request.Url.AbsoluteUri.Replace(Globals.intUrl, Globals.extUrl);
        context.Response.ContentType = "video/MP2T"; // MPEG-TS content type
        context.Response.SendChunked = true;

        if (restreamTask != null && !restreamTask.IsCompleted && videoUrl != currentURL)
        {
            cts.Cancel();
            await restreamTask;
            cts = new();
        }
        Globals.destinations.Add(context);
        currentURL = videoUrl;
        if (restreamTask == null || restreamTask.IsCompleted) restreamTask = Restream(videoUrl, cts.Token);
    }
    requests.Remove(t);
    requests.Add(listener.GetContextAsync());
}

static async Task Restream(string videoUrl, CancellationToken token)
{
    string exMsg = "";
    Console.WriteLine($"Playing {videoUrl}");
    using HttpClient client = new();
    client.Timeout = TimeSpan.FromSeconds(30);
    // Copy the video stream to the response output streams
    try
    {
        using HttpResponseMessage videoResponse = client.GetAsync(videoUrl, HttpCompletionOption.ResponseHeadersRead).Result;
        using Stream videoStream = videoResponse.Content.ReadAsStream();
        byte[] buffer = new byte[bufferSize];
        int bytesRead;
        while (Globals.destinations.Count > 0 && (bytesRead = await videoStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
        {
            for (int i = 0; i < Globals.destinations.Count; i++)
            {
                HttpListenerContext c = Globals.destinations[i];
                try { await c.Response.OutputStream.WriteAsync(buffer, 0, bytesRead); }
                catch (Exception ex) { exMsg = ex.Message; c.Response.Close(); Globals.destinations.Remove(c); }
            }
        }
    }
    catch (Exception ex) { exMsg = ex.Message; }
    Console.WriteLine($"{(exMsg != "" ? exMsg : "Stopped")} {videoUrl}");
    Globals.destinations.ForEach(d => d.Response.Close()); ;
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
        if (!Globals.whiteList.Exists(a => line.Contains(a)))
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
        sb.AppendLine(line.Replace(Globals.extUrl, Globals.intUrl));
        counter++;
    }

    Console.WriteLine($"Loaded {counter} channels");
    return sb.ToString();
}
