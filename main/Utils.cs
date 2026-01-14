namespace Samicpp.Pwsh;

using Samicpp.Http;
using Samicpp.Http.Http1;
using Samicpp.Http.Http2;

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Channels;

public static class Utils
{
    public static async Task Main()
    {
        // test
        await Task.CompletedTask;
        // var channel = ServeHttp(7010);
        // SimpleServe simple = new();

        // await IHandler.Serve(simple, channel);
    }

    public static Http1Socket GetHttp1(ushort port)
    {
        using Socket listener = new(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        // listener.DualMode = true;
        listener.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        listener.Listen(1);
        
        if (port == 0) Console.WriteLine(listener.LocalEndPoint);

        SslServerAuthenticationOptions opt = new()
        {
            ServerCertificate = Helper.SelfSigned(),
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            ClientCertificateRequired = false,
            ApplicationProtocols = [ SslApplicationProtocol.Http11, new SslApplicationProtocol("http/1.0"), new SslApplicationProtocol("http/0.9") ],
            AllowRenegotiation = false,
            AllowTlsResume = true,
        };

        var socket = listener.Accept();
        
        Span<byte> snap = stackalloc byte[24];
        int r = socket.Receive(snap, SocketFlags.Peek);
        byte[] peek = snap[..r].ToArray();

        NetworkStream stream = new(socket, true);

        if (peek[0] == 22)
        {
            var sslStream = new SslStream(stream, false);
            TlsSocket tls = new(sslStream);

            sslStream.AuthenticateAsServer(opt);
            string alpn = sslStream.NegotiatedApplicationProtocol.ToString();

            Http1Socket sock = new(tls, socket.RemoteEndPoint)
            {
                Allow09 = true,
                Allow10 = true,
                Allow11 = true,
                AllowUnknown = true,
            };
            sock.SetHeader("Connection", "close");
            return sock;
        }
        else
        {
            Http1Socket http1 = new(new TcpSocket(stream), socket.RemoteEndPoint)
            {
                Allow09 = true,
                Allow10 = true,
                Allow11 = true,
                AllowUnknown = true,
            };

            while (!http1.Client.HeadersComplete || !http1.Client.BodyComplete) http1.ReadClient();

            return http1;
        }
    }

    public static Channel<IDualHttpSocket> ServeHttp(ushort port, IPAddress? address = null, bool dualmode = true, int backlog = 10)
    {
        IPAddress ipa = address ?? IPAddress.IPv6Any;
        IPEndPoint endPoint = new(ipa, port);
        bool usedm = false && dualmode && ipa.AddressFamily == AddressFamily.InterNetworkV6;

        // Console.WriteLine(endPoint);

        var cert = Helper.SelfSigned();

        PolyServer server = new(endPoint, cert) 
        { 
            backlog = backlog, 
            dualmode = usedm, 
            fallback = true,
            alpn = [ SslApplicationProtocol.Http2, SslApplicationProtocol.Http11, new SslApplicationProtocol("http/1.0"), new SslApplicationProtocol("http/0.9") ], 
        };

        var channel = Channel.CreateUnbounded<IDualHttpSocket>();

        var st = server.Serve(async http => await channel.Writer.WriteAsync(http));

        return channel;

        // await foreach (var http in channel.Reader.ReadAllAsync()) yield return http;
        // await st;
    }
}