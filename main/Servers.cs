namespace Samicpp.Pwsh;

using Samicpp.Http;
using Samicpp.Http.Http1;
using Samicpp.Http.Http2;
using Samicpp.Http.Http09;

using System;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Security.Authentication;
using System.IO;
using System.Security.Cryptography;

public delegate Task Handler(IDualHttpSocket socket);

internal static class Helper
{
    public static X509Certificate2 SelfSigned()
    {
        // using RSA rsa = RSA.Create(2048);
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        X500DistinguishedName subject = new("CN=localhost");
        CertificateRequest req = new(subject, ecdsa, HashAlgorithmName.SHA256);
        
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

        SubjectAlternativeNameBuilder sanBuilder = new();
        sanBuilder.AddDnsName("*.localhost");
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName("127.0.0.1");
        sanBuilder.AddDnsName("::1");
        req.CertificateExtensions.Add(sanBuilder.Build());

        X509Certificate2 cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1)
        );

        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, ""), "");
    }

    public static async Task Http2Loop(Http2Session h2, Handler handler, bool init = true)
    {
        // using Http2Session h2 = new(socket, Http2Settings.Default(), end);

        try
        {
            if (init)
            {
                await h2.InitAsync();
                await h2.SendSettingsAsync(new(4096, null, null, 16777215, 16777215, null));
                await h2.SendPingAsync([104, 101, 97, 114, 98, 101, 97, 116]);
            }
            
            List<Task> waitfor = [];

            while (h2.goaway == null)
            {
                try
                {
                    
                    Http2Frame frame = await h2.ReadOneAsync();
                    var sid = await h2.HandleAsync(frame);

                    if (frame.type == Http2FrameType.Ping && (frame.flags & 0x1) != 0) continue;

                    if (sid != null)
                    {
                        Http2Stream stream = new((int)sid, h2);
                        waitfor.Add(handler(stream));
                    }
                }
                catch (HttpException.ConnectionClosed)
                {
                    break;
                }
                catch (IOException ioe) when (ioe.InnerException is SocketException e && (e.SocketErrorCode == SocketError.ConnectionReset || e.SocketErrorCode == SocketError.Shutdown || e.SocketErrorCode == SocketError.ConnectionAborted || e.ErrorCode == 32 /* Broken Pipe */))
                {
                    break;
                }
            }

            foreach (var t in waitfor) await t;
        }
        catch (Exception)
        {
            // Console.WriteLine(e);
        }
    }
}

public class H2CServer(IPEndPoint address)
{
    public IPEndPoint address = address;
    public bool h2c = true;
    public int backlog = 10;
    public bool dualmode = false;

    public async Task Serve(Handler handler)
    {
        using Socket listener = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (dualmode) listener.DualMode = true;

        try
        {
            listener.Bind(address);
            listener.Listen(backlog);
        }
        catch (Exception)
        {
            // Console.WriteLine(e);
            return;
        }

        while (true)
        {
            var shandler = await listener.AcceptAsync();

            var _ = Task.Run(async () =>
            {
                Http1Socket socket = new(new TcpSocket(new NetworkStream(shandler, ownsSocket: true)), shandler.RemoteEndPoint);

                var client = socket.Client;

                try
                {
                    while (!client.HeadersComplete) client = await socket.ReadClientAsync();
                }
                catch (Exception)
                {
                    // Console.WriteLine(e);
                }

                try
                {
                    if (h2c && client.Headers.TryGetValue("upgrade", out List<string>? up) && up[0] == "h2c")
                    {
                        using var h2c = await socket.H2CAsync();

                        await h2c.InitAsync();
                        await h2c.SendSettingsAsync(new(4096, null, null, 16777215, 16777215, null));
                        await h2c.SendPingAsync([104, 101, 97, 114, 98, 101, 97, 116]);

                        var upstream = new Http2Stream(1, h2c);
                        await upstream.ReadClientAsync();
                        var _ = Task.Run(async () => await handler(upstream));

                        await Helper.Http2Loop(h2c, handler, false);
                    }
                    else
                    {
                        socket.SetHeader("Connection", "close");
                        await handler(socket);
                    }
                }
                catch(Exception)
                {
                    // Console.WriteLine(e);
                }
            });
        }
    }
}

public class H2Server(IPEndPoint address)
{
    public IPEndPoint address = address;
    public int backlog = 10;
    public bool dualmode = false;

    public async Task Serve(Handler handler)
    {
        using Socket listener = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (dualmode) listener.DualMode = true;

        try
        {
            listener.Bind(address);
            listener.Listen(backlog);
        }
        catch (Exception)
        {
            // Console.WriteLine(e);
            return;
        }

        while (true)
        {
            var shandler = await listener.AcceptAsync();

            var _ = Task.Run(async () =>
            {
                var socket = new TcpSocket(new NetworkStream(shandler, ownsSocket: true));
                using Http2Session h2 = new(socket, Http2Settings.Default(), shandler.RemoteEndPoint);
                await Helper.Http2Loop(h2, handler);
            });
        }
    }
}

public class O9Server(IPEndPoint address)
{
    public IPEndPoint address = address;
    public int backlog = 10;
    public bool dualmode = false;

    public async Task Serve(Handler handler)
    {
        using Socket listener = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (dualmode) listener.DualMode = true;

        try
        {
            listener.Bind(address);
            listener.Listen(backlog);
        }
        catch (Exception)
        {
            // Console.WriteLine(e);
            return;
        }

        while (true)
        {
            var shandler = await listener.AcceptAsync();

            var _ = Task.Run(async () =>
            {
                Http09Socket socket = new(new TcpSocket(new NetworkStream(shandler, ownsSocket: true)), shandler.RemoteEndPoint);

                var client = await socket.ReadClientAsync();

                await handler(socket);
            });
        }
    }
}

public class TlsServer(IPEndPoint address, X509Certificate2 cert)
{
    public IPEndPoint address = address;
    readonly X509Certificate2 cert = cert;
    public int backlog = 10;
    public bool dualmode = false;
    public List<SslApplicationProtocol> alpn = [
        new SslApplicationProtocol("h2"), //SslApplicationProtocol.Http2,
        new SslApplicationProtocol("http/1.1"), //SslApplicationProtocol.Http11,
        new SslApplicationProtocol("http/1.0"),
        new SslApplicationProtocol("http/0.9"),
    ];

    public bool fallback = true;

    public async Task Serve(Handler handler)
    {
        using Socket listener = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (dualmode) listener.DualMode = true;

        try
        {
            listener.Bind(address);
            listener.Listen(backlog);
        }
        catch (Exception)
        {
            // Console.WriteLine(e);
            return;
        }

        SslServerAuthenticationOptions opt = new()
        {
            ServerCertificate = cert,
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            ClientCertificateRequired = false,
            ApplicationProtocols = alpn,
            AllowRenegotiation = false,
            AllowTlsResume = true,
        };

        while (true)
        {
            var shandler = await listener.AcceptAsync();
            NetworkStream stream = new(shandler, true);

            var _ = Task.Run(async () => await TlsUpgrade(handler, stream, opt, shandler.RemoteEndPoint, fallback));
        }
    }
    internal static async Task TlsUpgrade(Handler handler, NetworkStream socket, SslServerAuthenticationOptions opt, EndPoint? end, bool fallback)
    {
        var sslStream = new SslStream(socket, false);
        try
        {
            string alpn = "";
            try
            {
                await sslStream.AuthenticateAsServerAsync(opt);
                alpn = sslStream.NegotiatedApplicationProtocol.ToString();
            }
            catch (Exception)
            {
                // Console.WriteLine(e);
                return;
            }
            TlsSocket tls = new(sslStream);

            if (alpn == "http/0.9")
            {
                Http09Socket sock = new(tls, end);
                await handler(sock);
            }
            else if (alpn == "http/1.0")
            {
                Http1Socket sock = new(tls, end) { Allow09 = false, Allow11 = false, AllowUnknown = false, Allow10 = true, };
                sock.SetHeader("Connection", "close");
                await handler(sock);
            }
            else if (alpn == "http/1.1")
            {
                Http1Socket sock = new(tls, end) { Allow09 = false, Allow10 = false, AllowUnknown = false, Allow11 = true, };
                sock.SetHeader("Connection", "close");
                await handler(sock);
            }
            else if (alpn == "h2")
            {
                using Http2Session h2 = new(tls, Http2Settings.Default(), end);
                await Helper.Http2Loop(h2, handler);
            }
            else if(fallback)
            {
                Http1Socket sock = new(tls, end)
                {
                    Allow09 = true,
                    Allow10 = true,
                    Allow11 = true,
                    AllowUnknown = true,
                };
                sock.SetHeader("Connection", "close");
                await handler(sock);
            }
            else
            {
                // Console.WriteLine("couldnt use any protocol");
            }
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset || e.SocketErrorCode == SocketError.Shutdown || e.SocketErrorCode == SocketError.ConnectionAborted)
        {
            // Console.WriteLine(e);
        }
        catch (Exception)
        {
            // Console.WriteLine(e);
        }
    }
}

public class PolyServer(IPEndPoint address, X509Certificate2 cert)
{
    public IPEndPoint address = address;
    readonly X509Certificate2 cert = cert;
    public int backlog = 10;
    public bool dualmode = false;
    public List<SslApplicationProtocol> alpn = [
        new SslApplicationProtocol("h2"),
        new SslApplicationProtocol("http/1.1"),
        new SslApplicationProtocol("http/1.0"),
        new SslApplicationProtocol("http/0.9"),
    ];

    public bool fallback = true;

    public bool AllowH09 = true;
    public bool AllowH10 = true;
    public bool AllowH11 = true;
    public bool AllowH2c = true;
    public bool AllowH2 = true;
    public bool AllowTls = true;

    public async Task Serve(Handler handler)
    {
        using Socket listener = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (dualmode) listener.DualMode = true;

        try
        {
            listener.Bind(address);
            listener.Listen(backlog);
        }
        catch (Exception)
        {
            // Console.WriteLine(e);
            return;
        }

        SslServerAuthenticationOptions opt = new()
        {
            ServerCertificate = cert,
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            ClientCertificateRequired = false,
            ApplicationProtocols = alpn,
            AllowRenegotiation = false,
            AllowTlsResume = true,
        };

        while (true)
        {
            var socket = await listener.AcceptAsync();
            _ = Task.Run(async ()=>{                
                byte[] snap = new byte[24];
                int r = await socket.ReceiveAsync(snap, SocketFlags.Peek);
                byte[] peek = snap[..r];

                NetworkStream stream = new(socket, true);

                await Detect(peek, handler, stream, opt, socket.RemoteEndPoint, fallback);
            });
        }
    }

    public async Task Detect(byte[] peek, Handler handler, NetworkStream stream, SslServerAuthenticationOptions opt, EndPoint? end, bool fallback)
    {
        if (peek[0] == 22)
        {
            await TlsServer.TlsUpgrade(handler, stream, opt, end, fallback);
        }
        else if (peek.SequenceEqual(Http2Session.MAGIC))
        {
            var socket = new TcpSocket(stream);
            using Http2Session h2 = new(socket, Http2Settings.Default(), end);
            await Helper.Http2Loop(h2, handler);
        }
        else if (AllowH09 || AllowH10 || AllowH11)
        {
            Http1Socket socket = new(new TcpSocket(stream), end) { Allow09 = AllowH09, Allow10 = AllowH10, Allow11 = AllowH11, };

            var client = socket.Client;

            try
            {
                while (!client.HeadersComplete) client = await socket.ReadClientAsync();
            }
            catch (Exception)
            {
                // Console.WriteLine(e);
            }

            try
            {
                if (AllowH2c && client.Headers.TryGetValue("upgrade", out List<string>? up) && up[0] == "h2c")
                {
                    using var h2c = await socket.H2CAsync();

                    await h2c.InitAsync();
                    await h2c.SendSettingsAsync(new(4096, null, null, 16777215, 16777215, null));
                    await h2c.SendPingAsync([104, 101, 97, 114, 98, 101, 97, 116]);

                    var upstream = new Http2Stream(1, h2c);
                    await upstream.ReadClientAsync();
                    var _ = Task.Run(async () => await handler(upstream));

                    await Helper.Http2Loop(h2c, handler, false);
                }
                else
                {
                    socket.SetHeader("Connection", "close");
                    await handler(socket);
                }
            }
            catch(Exception)
            {
                // Console.WriteLine(e);
            }
        }
        else
        {
            // Console.WriteLine("no proto");
        }
    }
}