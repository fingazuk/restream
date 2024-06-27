using System.Net;
using System.Text;

if (args.Length == 0)
{
    Console.WriteLine("Specify an m3u playlist");
    return;
}
string playListURL = "";
try { Uri uri = new(args[0]); playListURL = uri.ToString(); }
catch (Exception ex) { Console.WriteLine($"Playlist Uri format error. {ex.Message}"); }

Task? streamingTask = null;
CancellationTokenSource cts = new();
const string m3uFileName = "playlist.m3u";
string playList = DownloadPlaylist(playListURL);
string currentUrl = "";
HttpListener listener = new();
listener.Prefixes.Add($"http://*:{Globals.port}/");
listener.Start();
HashSet<Task> requests = [];
for (int i = 0; i < 2; i++) requests.Add(listener.GetContextAsync());
Console.WriteLine($"Listening on {Globals.intUrl}...");

while (true)
{
    Task t = await Task.WhenAny(requests);
    HttpListenerContext context = ((Task<HttpListenerContext>)t).Result;

    if (context.Request.Url == null) continue;
    if (context.Request.Url.AbsolutePath == "/iptv")
    {
        Console.WriteLine("Sending playlist");
        context.Response.ContentType = "application/x-mpegurl";
        try
        {
            using StreamWriter writer = new(context.Response.OutputStream);
            writer.Write(playList);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Sending playlist : {ex.Message}");
        }
        context.Response.Close();
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
