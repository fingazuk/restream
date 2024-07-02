using System.Net;
using System.Net.Sockets;

static class Globals
{
    public static List<HttpListenerContext> destinations = [];
    public const uint port = 3666;
    public static string intUrl = $"{GetLocalIPAddress()}:{port}", extUrl = "";
    public static List<string> whiteList = [];
    private static string GetLocalIPAddress()
    {
        IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) return ip.ToString();
        }
        return "localhost";
    }

}