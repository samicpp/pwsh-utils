namespace Samicpp.Pwsh;

using Samicpp.Http;
using Samicpp.Http.Http1;
using Samicpp.Http.Http2;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Web;

public interface IHandler
{
    public Task Entry(IDualHttpSocket http);

    public static async Task Serve(IHandler handler, Channel<IDualHttpSocket> channel)
    {
        await foreach (var http in channel.Reader.ReadAllAsync())
        {
            // unsafe { byte e = *((byte*)null); }
            Console.WriteLine("got client");
            _ = Task.Run(() => handler.Entry(http));
        }
    }

    public static async Task SelfServe(IHandler handler, ushort port, IPAddress? address = null, bool dualmode = true, int backlog = 10)
    {
        IPAddress ipa = address ?? IPAddress.IPv6Any;
        IPEndPoint endPoint = new(ipa, port);
        bool usedm = false && dualmode && ipa.AddressFamily == AddressFamily.InterNetworkV6;

        Console.WriteLine(endPoint);

        var cert = Helper.SelfSigned();

        PolyServer server = new(endPoint, cert) 
        { 
            backlog = backlog, 
            dualmode = usedm, 
            fallback = true,
            alpn = [ SslApplicationProtocol.Http2, SslApplicationProtocol.Http11, new SslApplicationProtocol("http/1.0"), new SslApplicationProtocol("http/0.9") ], 
        };


        await server.Serve(handler.Entry);
    }
}

public class SimpleServe: IHandler
{
    static readonly Regex remove1 = new(@"(\?.*$)|(\#.*$)|(\:.*$)", RegexOptions.Compiled);
    static readonly Regex remove2 = new(@"\/\.{1,2}(?=\/|$)", RegexOptions.Compiled);
    static readonly Regex remove3 = new(@"/$", RegexOptions.Compiled);
    static readonly Regex collapse = new(@"\/+", RegexOptions.Compiled);
    
    public string startdir = ".";
    public bool allowDirs = true;
    public bool logClients = true;
    public bool logPath = true;
    public bool useCompression = false;
    public bool lookforDirname = true;
    public string index = "index";
    public int fileBufferSize = 16 * 1024 * 1024;
    public string defConType = "text/plain"; // "application/octet-stream";

    public SimpleServe() { }

    static string Cleanse(string path)
    {
        var cpath = remove1.Replace(path, "");
        cpath = remove2.Replace(cpath, "");
        cpath = collapse.Replace(cpath, "/");
        cpath = remove3.Replace(cpath, "");
        return cpath;
    }

    public async Task Entry(IDualHttpSocket http)
    {
        try
        {
            Console.WriteLine($"\e[38;5;8m{http.EndPoint}\e[0m");
            Console.WriteLine($"\e[38;5;3mprotocol = {http.Client.VersionString}\e[0m");

            http.SetHeader("Content-Type", defConType);

            var path = Path.GetFullPath(HttpUtility.UrlDecode(startdir + Cleanse("/" + http.Client.Path)));
            // FileInfo file = new(path);
            // DirectoryInfo dir = new(path);

            // if (logClients) Console.WriteLine($"{http.EndPoint} connected using {http.Client.VersionString}");
            if (logPath) Console.WriteLine($"\e[38;5;2m{path}\e[0m");

            if (useCompression && http.Client.Headers.TryGetValue("accept-encoding", out List<string>? encoding))
            {
                if (encoding[0].Contains("br"))
                {
                    http.Compression = CompressionType.Brotli;
                    http.SetHeader("Content-Encoding", "br");
                }
                else if (encoding[0].Contains("deflate"))
                {
                    http.Compression = CompressionType.Deflate;
                    http.SetHeader("Content-Encoding", "deflate");
                }
                else if (encoding[0].Contains("gzip"))
                {
                    http.Compression = CompressionType.Gzip;
                    http.SetHeader("Content-Encoding", "gzip");
                }
                else
                {
                    http.Compression = CompressionType.None;
                    http.SetHeader("Content-Encoding", "identity");
                }
            }
            else
            {
                http.Compression = CompressionType.None;
                http.SetHeader("Content-Encoding", "identity");
            }


            await Auto(path, http);
            // Console.WriteLine("done");
        }
        catch(Exception e)
        {
            Console.WriteLine(e);
            http.Status = 500;
            http.StatusMessage = "Internal Server Error";
            await http.CloseAsync($"{e}");
        }
        finally
        {
            await http.DisposeAsync();
        }
    }

    public async Task Auto(string path, IDualHttpSocket http)
    {
        FileInfo file = new(path);
        DirectoryInfo dir = new(path);

        if (file.Exists)
        {
            await FileHandler(path, file, http);
        }
        else if (dir.Exists)
        {
            await DirHandler(path, dir, http);
        }
        else
        {
            await Error(404, path, http);
        }
    }

    public async Task FileHandler(string path, FileInfo info, IDualHttpSocket http)
    {
        using FileStream file = File.OpenRead(path);
        var buff = new byte[fileBufferSize];
        int read;

        var ctype = MimeTypes.types.GetValueOrDefault(info.Name.Split(".").Last()) ?? defConType;

        if (info.Name.EndsWith(".br") || info.Name.EndsWith(".gz"))
        {
            var dts = info.Name.Split(".");
            var last = dts.ElementAtOrDefault(dts.Length - 2) ?? "";
            var enc = dts.Last();

            ctype = MimeTypes.types.GetValueOrDefault(last) ?? defConType;
            http.Compression = CompressionType.None;
            http.SetHeader("Content-Encoding", enc == "br" ? "br" : "gzip");
        }

        var etag = SHA256.HashData(Encoding.UTF8.GetBytes($"{info}@{info.LastWriteTimeUtc}"));
        http.SetHeader("ETag", Convert.ToBase64String(etag));
        http.SetHeader("Last-Modified", $"{info.LastWriteTimeUtc}");
        http.SetHeader("Content-Type", ctype);
        http.SetHeader("Content-Length", info.Length.ToString());

        
        // Console.WriteLine($"{info.Name} {info.Name.Split(".").Last()} {ctype}");


        if (http is Http1Socket http1) {
            await http1.SendHeadAsync();

            while ((read = await file.ReadAsync(buff)) != 0)
            {
                await http1.socket.WriteAsync(buff.AsMemory(0, read));
            }
        }
        else
        {
            while ((read = await file.ReadAsync(buff)) != 0)
            {
                await http.WriteAsync(buff.AsMemory(0, read));
            }
            await http.CloseAsync();
        }
    }

    public async Task DirHandler(string path, DirectoryInfo info, IDualHttpSocket http)
    {
        string last = Path.GetFileName(path);
        var files = Directory.GetFileSystemEntries(path);
        var file = files.FirstOrDefault(f =>
        {
            string name = Path.GetFileName(f);
            return (lookforDirname && name.StartsWith(last, StringComparison.CurrentCultureIgnoreCase)) || name.StartsWith(index, StringComparison.CurrentCultureIgnoreCase);
        });


        if (file != null)
        {
            await Auto(file, http);
        }
        else if (allowDirs)
        {
            await IndexDir(files, path, http);
        }
        else
        {
            await Error(403, path, http);
        }
    }

    public static async Task IndexDir(string[]? files, string path, IDualHttpSocket http)
    {
        http.SetHeader("Content-Type", "text/html");

        StringBuilder fs = new($"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Directory Index of {path}</title>
            <style>
                @import url("/dirindex.css");
                @import url("dirindex.css");

                body{"{"}
                    font-family: 'Courier New', Courier, monospace;
                    background-color: #111;
                    font-size: 1.3em;
                {"}"}

                li.dir a::after{"{"}
                    content: "/";
                {"}"}

                a{"{"}
                    --clr: aliceblue;

                    color: var(--clr);
                    text-decoration: none;
                    border-bottom: solid 0.01em var(--clr);
                {"}"}
                a:visited{"{"}
                    border-bottom: none;
                {"}"}
                a:hover{"{"}
                    font-weight: bold;
                {"}"}

                li.entry{"{"}
                    /* transition: transform 0.1s ease; */
                {"}"}
                li.entry:has(a:hover) {"{"}
                    /* transform: scale(1.1); */
                {"}"}

                li.file a{"{"}
                    --clr: #00f7ff;
                {"}"}

                li.dir a{"{"}
                    --clr: #00ff6a;
                {"}"}

                li.unknown a{"{"}
                    --clr: #ffbf00;
                {"}"}

                
            </style>
        </head>
        <body>
            <a class="parent" href="..">parent</a> <br/>
            <ul>
        """);
        foreach (string file in files!) 
        {
            string name = Path.GetFileName(file);
            string uri = Path.Combine(http.Client.Path, name).Replace('\\', '/');
            if (File.Exists(file)) fs.Append($"<li class=\"entry file\"><a href=\"{uri}\">{name}</a></li>");
            else if (Directory.Exists(file)) fs.Append($"<li class=\"entry dir\"><a href=\"{uri}\">{name}</a></li>");
            else fs.Append($"<li class=\"entry unknown\"><a href=\"{uri}\">{name}</a></li>");
        };
        fs.Append("</ul>\n</body>\n</html>");
        
        await http.CloseAsync(fs.ToString());
    }

    public static async Task Error(int code, string path, IDualHttpSocket http)
    {
        http.Status = code;
        http.SetHeader("Content-Type", "text/plain");

        if (code == 404)
        {
            http.StatusMessage = "Not Found";
            await http.CloseAsync("couldnt find " + path);
        }
        else if (code == 403)
        {
            http.StatusMessage = "Forbidden";
            await http.CloseAsync("access to " + path + "denied");
        }
    }


    // public static async Task Serve(ushort port, IPAddress? address = null, bool dualmode = true, int backlog = 10) { }
}
