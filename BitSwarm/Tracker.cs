using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Text.RegularExpressions;
using System.Security.Cryptography.X509Certificates;

using BencodeNET.Parsing;
using BencodeNET.Objects;

namespace SuRGeoNix.BEP
{
    public class Tracker
    {
        public Uri      uri                         { get; private set; }
        public string   host                        { get; private set; }
        public int      port                        { get; private set; }
        public Type     type                        { get; private set; }
        
        public Options  options;

        public struct Options
        {
            public string   InfoHash;
            public byte[]   PeerId;

            public int      ConnectTimeout;
            public int      ReceiveTimeout;

            public int      Verbosity;
            public Logger   LogFile;
        }
        public enum Type
        {
            UDP     = 1,
            HTTP    = 2,
            HTTPS   = 3
        }

        public Dictionary<string, int>  peers       { get; private set; }
        public UInt32                   seeders     { get; private set; }
        public UInt32                   leechers    { get; private set; }
        public UInt32                   completed   { get; private set; }
        public UInt32                   interval    { get; private set; }
        
        private const Int64 CONNECTION_ID = 0x41727101980;

        private TcpClient       tcpClient;
        private NetworkStream   netStream;
        private SslStream       sslStream;
        private UdpClient       udpClient;
        private IPEndPoint      ipEP;
        private byte[]          recvBuff;

        private byte[]          action;
        private byte[]          connID;
        private byte[]          tranID;
        private byte[]          key;
        private byte[]          data;
        
        public static class Action
        {
            public const byte CONNECT   = 0x00;
            public const byte ANNOUNCE  = 0x01;
            public const byte SCRAPE    = 0x02;
        }

        public Tracker(Uri url, Options options)
        {
            this.options    = options;
            uri             = url;
            host            = url.DnsSafeHost;
            port            = url.Port;
            ipEP            = new IPEndPoint(IPAddress.Any, 0);
            key             = Utils.ToBigEndian((Int32) new Random().Next(1, Int32.MaxValue));

            switch ( url.Scheme.ToLower() )
            {
                case "http":
                    type = Type.HTTP;
                    break;
                case "https":
                    type = Type.HTTPS;
                    break;
                case "udp":
                    type = Type.UDP;
                    break;
                default:

                    break;
            }
        }

        // Main Implementation [Connect | Receive | Announce | Scrape]
        private bool ConnectUDP()
        {
            try
            {
                // Socket Connection
                udpClient = new UdpClient();
                udpClient.Connect(host, port);
                if (Utils.IsWindows)
                udpClient.AllowNatTraversal(true);
                udpClient.DontFragment = true;
            
                // Connect Request
                action = Utils.ToBigEndian((Int32) Action.CONNECT);
                tranID = Utils.ToBigEndian((Int32) new Random().Next(1,Int32.MaxValue));
                data   = Utils.ArrayMerge(Utils.ToBigEndian(CONNECTION_ID), action, tranID);

                udpClient.Send(data, data.Length);
                udpClient.Send(data, data.Length);

                // Connect Response
                if ( !ReceiveUDP() ) return false;

                connID = Utils.ArraySub(ref recvBuff, 8, 8); // Valid for 60 seconds

                return true;
            } catch (Exception e)
            {
                Log($"[CONNECT] Error {e.Message}\r\n{e.StackTrace}");
            }

            return false;
        }
        private bool ReceiveUDP()
        {
            try
            {
                do
                {
                    var asyncResult = udpClient.BeginReceive(null, null);
                    asyncResult.AsyncWaitHandle.WaitOne(options.ReceiveTimeout);
                    if (asyncResult.IsCompleted)
                        recvBuff = udpClient.EndReceive(asyncResult, ref ipEP);
                    else
                        return false;
                } while (!Utils.ArrayComp(Utils.ArraySub(ref recvBuff, 0, 4), action)); // Validate reply to our last action request 
            } catch (Exception e)
            {
                Log($"[RECV] Error {e.Message}\r\n{e.StackTrace}");
                return false;
            }

            return true;
        }

        private bool ReceiveTCP()
        {
            try
            {
                string  headers = "";
                int     newLines= 0;
                recvBuff       = new byte[1];

                do
                {
                    if ( type == Type.HTTP)
                        netStream.Read(recvBuff, 0, 1);
                    else
                        sslStream.Read(recvBuff, 0, 1);

                    if ( recvBuff[0] == '\r' || recvBuff[0] == '\n') 
                    {
                        if ( recvBuff[0] == '\n' )
                            newLines++;
                    }
                    else
                    {
                        newLines = 0;
                    }

                    headers += Convert.ToChar(recvBuff[0]);

                } while ( newLines != 2 );

                int len = int.Parse(Regex.Match(headers, @"Content-Length: ([0-9]+)").Groups[1].Value);

                recvBuff = new byte[len];
                if ( type == Type.HTTP)
                    netStream.Read(recvBuff, 0, len);
                else
                    sslStream.Read(recvBuff, 0, len);

            } catch (Exception) { return false; }

            return true;
        }
        private bool ConnectTCP()
        {
            bool connected;
            tcpClient = new TcpClient();

            try
            {
                connected = tcpClient.ConnectAsync(host, port).Wait(options.ConnectTimeout);

                if ( !connected ) return false;

                if ( type == Type.HTTP)
                    netStream               = tcpClient.GetStream();
                else
                {
                    sslStream               = new SslStream(tcpClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate));
                    sslStream.AuthenticateAsClient(host);
                }

                tcpClient.SendBufferSize    = 1024;
                tcpClient.ReceiveBufferSize = 1500;
            }
            catch ( Exception e)
            {
                Log("Failed " + e.Message);
                return false;
            }

            return true;
        }

        public bool Announce(   Int32 num_want = -1, Int64 downloaded = 0, Int64 left = 0, Int64 uploaded = 0)
        {
            if ( type == Type.UDP)
                return AnnounceUDP(num_want, downloaded, left, uploaded);
            else
                return AnnounceTCP(num_want, downloaded, left, uploaded);
        }
        public bool AnnounceTCP(Int32 num_want = -1, Int64 downloaded = 0, Int64 left = 0, Int64 uploaded = 0)
        {
            if ( !ConnectTCP() ) return false;
            
            try
            {
                byte[] sendBuff = System.Text.Encoding.UTF8.GetBytes($"GET {uri.AbsolutePath}?info_hash={Utils.StringHexToUrlEncode(options.InfoHash)}&peer_id={Utils.StringHexToUrlEncode(BitConverter.ToString(options.PeerId).Replace("-",""))}&port=11111&left={left}&downloaded={downloaded}&uploaded={uploaded}&event=started&compact=1&numwant={num_want} HTTP/1.1\r\nHost: {host}:{port}\r\nConection: close\r\n\r\n");

                if ( type == Type.HTTP)
                    netStream.Write(sendBuff, 0, sendBuff.Length);
                else
                    sslStream.Write(sendBuff, 0, sendBuff.Length);

                if ( !ReceiveTCP() ) return false;

                BencodeParser parser = new BencodeParser();
                BDictionary extDic = parser.Parse<BDictionary>(recvBuff);

                byte[] hashBytes = ((BString) extDic["peers"]).Value.ToArray();

                peers = new Dictionary<string, int>();
                for (int i=0; i<hashBytes.Length; i+=6)
                {
                    IPAddress curIP = new IPAddress( Utils.ArraySub(ref hashBytes,(uint) i, 4, false) );
                    UInt16 curPort  = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref hashBytes,(uint) i + 4, 2, true), 0);
                    if ( curPort > 0) peers[curIP.ToString()] = curPort;
                }
            }
            catch ( Exception e)
            {
                Log($"Failed {e.Message}\r\n{e.StackTrace}");
                return false;
            }

            return true;
        }
        public bool AnnounceUDP(Int32 num_want = -1, Int64 downloaded = 0, Int64 left = 0, Int64 uploaded = 0)
        {
            if ( !ConnectUDP() ) return false;

            try
            {
                // Announce Request
                Int32 event_ = 0, externalIp = 0;
                Int16 externalPort = 11111;

                action  = Utils.ToBigEndian((Int32) Action.ANNOUNCE);
                data    = Utils.ArrayMerge(connID, action, tranID, Utils.StringHexToArray(options.InfoHash), options.PeerId, Utils.ToBigEndian(downloaded), Utils.ToBigEndian(left), Utils.ToBigEndian(uploaded), Utils.ToBigEndian(event_), Utils.ToBigEndian(externalIp), key, Utils.ToBigEndian(num_want), Utils.ToBigEndian(externalPort));
                udpClient.Send(data, data.Length);
            
                // Announce Response
                if ( !ReceiveUDP() ) return false;
                interval    = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBuff,  8, 4, true), 0);
                seeders     = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBuff, 12, 4, true), 0);
                leechers    = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBuff, 16, 4, true), 0);

                peers = new Dictionary<string, int>();
                for (int i=0; i<(recvBuff.Length - 20) / 6; i++)
                {
                    IPAddress curIP = new IPAddress(Utils.ArraySub(ref recvBuff,(uint) (20 + (i*6)), 4, false));
                    UInt16 curPort  = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref recvBuff,(uint) (24 + (i*6)), 2, true), 0);
                    if ( curPort > 0) peers[curIP.ToString()] = curPort;
                }
            }
            catch ( Exception e)
            {
                Log($"Failed {e.Message}\r\n{e.StackTrace}");
                return false;
            }

            return true;
        }

        public bool ScrapeUDP(string infoHash)
        {
            if ( ConnectUDP() ) return false;

            // Scrape Request
            action = Utils.ToBigEndian((Int32) Action.SCRAPE);
            data   = Utils.ArrayMerge(connID, action, tranID, Utils.StringHexToArray(infoHash) );

            udpClient.Send(data, data.Length);
            
            // Scrape Response
            if ( !ReceiveUDP() ) return false;
            leechers    = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBuff,  8, 4, true), 0);
            completed   = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBuff, 12, 4, true), 0);
            seeders     = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBuff, 16, 4, true), 0);

            return true;
        }

        // Misc
        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; }
        internal void Log(string msg) { if (options.Verbosity > 0) options.LogFile.Write($"[Tracker ] [{type}:{host}:{port}] {msg}"); }
    }
}
