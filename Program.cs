using System.Net;
using System.Text;

const string m3uFileName = "playlist.m3u";
const int maxConcurrentConnections = 2;
string playListURL = "", currentUrl = "";

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

string playList = DownloadPlaylist(playListURL);
HttpListener listener = new();
listener.Prefixes.Add($"http://*:{Globals.port}/");
listener.Start();
HashSet<Task> requests = [];
for (int i = 0; i < maxConcurrentConnections; i++) requests.Add(listener.GetContextAsync());
Console.WriteLine($"Listening on {Globals.intUrl}...");
Task? streamingTask = null;
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
        Console.WriteLine($"Send playlist : {(exMsg == "" ? "" : exMsg)}");
    }
    else
    {
        string videoUrl = context.Request.Url.AbsoluteUri.Replace(Globals.intUrl, Globals.extUrl);
        context.Response.ContentType = "video/MP2T"; // MPEG-TS content type
        context.Response.SendChunked = true;

        if ((streamingTask != null && streamingTask.IsCompleted) || videoUrl != currentUrl)
        {
            if (streamingTask != null && !streamingTask.IsCompleted)
            {
                cts.Cancel();
                await streamingTask;
                cts = new();
            }
            Globals.destinations.Add(context);
            streamingTask = fetchStream(videoUrl, cts.Token);
            currentUrl = videoUrl;
        }
        else Globals.destinations.Add(context);
    }
    requests.Remove(t);
    requests.Add(listener.GetContextAsync());
}

static async Task fetchStream(string videoUrl, CancellationToken token)
{
    string exMsg = "";
    Console.WriteLine($"Playing {videoUrl}");
    using HttpClient client = new();
    client.Timeout = TimeSpan.FromSeconds(30);
    // Copy the video stream to the response output stream
    try
    {
        using HttpResponseMessage videoResponse = client.GetAsync(videoUrl, HttpCompletionOption.ResponseHeadersRead).Result;
        using Stream videoStream = videoResponse.Content.ReadAsStream();
        const int bufferSize = 0x1000; // Buffer size in bytes
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
    foreach (var d in Globals.destinations) d.Response.Close();
    Globals.destinations.Clear();
}

static string DownloadPlaylist(string playList)
{
    string content = "";
    if (File.Exists(m3uFileName) && File.GetLastWriteTime(m3uFileName).Date == DateTime.Today.Date)
        content = File.ReadAllText(m3uFileName);
    else
    {
        using HttpClient client = new();
        HttpResponseMessage response = client.GetAsync(playList).Result;
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Console.WriteLine("Unable to download remote playlist");
            return "";
        }
        content = response.Content.ReadAsStringAsync().Result;
        try { File.WriteAllText(m3uFileName, content); }
        catch { }
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
