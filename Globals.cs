using System.Net;
using System.Net.Sockets;

static class Globals
{
    public static int Port { get; set; }
    public static int MaxConnections { get; set; }
    public static string PlaylistURL { get; set; } = "";
    public static List<string> WhiteList { get; set; } = new();
    public static List<HttpListenerContext> destinations = [];
    public static string intUrl = "", extUrl = "";
    public static string GetLocalIPAddress()
    {
        IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) return ip.ToString();
        }
        return "localhost";
    }
}

public class Settings
{
    public int Port { get; set; }
    public int MaxConnections { get; set; }
    public string PlaylistURL { get; set; } = "";
    public List<string> WhiteList { get; set; } = new();
}