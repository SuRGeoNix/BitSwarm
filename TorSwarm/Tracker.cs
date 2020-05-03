using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

namespace SuRGeoNix.TorSwarm
{
    public class Tracker
    {
        public string   host                        { get; private set; }
        public int      port                        { get; private set; }
        
        public Options options;

        public struct Options
        {
            public string   hash;
            public byte[]   peerID;
            public TYPE     type;
            public int      readTimeout;
            public int      verbosity;

            public Logger   log;
        }

        public Dictionary<string, int>  peers       { get; private set; }
        public UInt32                   seeders     { get; private set; }
        public UInt32                   leechers    { get; private set; }
        public UInt32                   completed   { get; private set; }
        public UInt32                   interval    { get; private set; }
        
        private const Int64 CONNECTION_ID = 0x41727101980;

        private UdpClient   udpClient;
        private IPEndPoint  ipEP;
        private byte[]      recvBytes;

        private byte[]      action;
        private byte[]      connID;
        private byte[]      tranID;
        private byte[]      key;
        private byte[]      data;
        
        public enum TYPE
        {
            UDP  = 1,
            TCP  = 2,
            HTTP = 3
        }
        public static class Action
        {
            public const byte CONNECT   = 0x00;
            public const byte ANNOUNCE  = 0x01;
            public const byte SCRAPE    = 0x02;
        }

        public Tracker(string hostIP, int hostPort, Options options)
        {
            this.options    = options;
            this.host       = hostIP;
            this.port       = hostPort;

            ipEP            = new IPEndPoint(IPAddress.Any, 0);
            key             = Utils.ToBigEndian((Int32) new Random().Next(1, Int32.MaxValue));
        }

        // Main Implementation [Connect | Receive | Announce | Scrape]
        private bool Connect()
        {
            try
            {
                if (options.type != TYPE.UDP) return false;

                // Socket Connection
                udpClient = new UdpClient();
                udpClient.Connect(host, port);
                udpClient.AllowNatTraversal(true);
                udpClient.DontFragment = true;
            
                // Connect Request
                action = Utils.ToBigEndian((Int32) Action.CONNECT);
                tranID = Utils.ToBigEndian((Int32) new Random().Next(1,Int32.MaxValue));
                data   = Utils.ArrayMerge(Utils.ToBigEndian(CONNECTION_ID), action, tranID);

                udpClient.Send(data, data.Length);
                udpClient.Send(data, data.Length);

                // Connect Response
                if ( !Receive() ) return false;

                connID = Utils.ArraySub(ref recvBytes, 8, 8); // Valid for 60 seconds

                return true;
            } catch (Exception e)
            {
                Log($"[CONNECT] Error {e.Message}\r\n{e.StackTrace}");
            }

            return false;
        }
        private bool Receive()
        {
            try
            {
                do
                {
                    if (options.type == TYPE.UDP)
                    {
                        var asyncResult = udpClient.BeginReceive(null, null);
                        asyncResult.AsyncWaitHandle.WaitOne(options.readTimeout);
                        if (asyncResult.IsCompleted)
                            recvBytes = udpClient.EndReceive(asyncResult, ref ipEP);
                        else
                            return false;
                    }
                } while (!Utils.ArrayComp(Utils.ArraySub(ref recvBytes, 0, 4), action)); // Validate reply to our last action request 
            } catch (Exception e)
            {
                Log($"[RECV] Error {e.Message}\r\n{e.StackTrace}");
                return false;
            }

            return true;
        }

        public bool Announce(string infoHash, Int32 num_want = -1, Int64 downloaded = 0, Int64 left = 0, Int64 uploaded = 0)
        {
            if ( !Connect() ) return false;

            // Announce Request
            Int32 event_ = 0, externalIp = 0;
            Int16 externalPort = 0;

            action  = Utils.ToBigEndian((Int32) Action.ANNOUNCE);
            data    = Utils.ArrayMerge(connID, action, tranID, Utils.StringHexToArray(infoHash), options.peerID, Utils.ToBigEndian(downloaded), Utils.ToBigEndian(left), Utils.ToBigEndian(uploaded), Utils.ToBigEndian(event_), Utils.ToBigEndian(externalIp), key, Utils.ToBigEndian(num_want), Utils.ToBigEndian(externalPort));
            udpClient.Send(data, data.Length);
            
            // Announce Response
            if ( !Receive() ) return false;
            interval    = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBytes,  8, 4, true), 0);
            seeders     = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBytes, 12, 4, true), 0);
            leechers    = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBytes, 16, 4, true), 0);

            peers = new Dictionary<string, int>();
            for (int i=0; i<(recvBytes.Length - 20) / 6; i++)
            {
                IPAddress curIP = new IPAddress(Utils.ArraySub(ref recvBytes,(uint) (20 + (i*6)), 4, false));
                UInt16 curPort  = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref recvBytes,(uint) (24 + (i*6)), 2, true), 0);
                if ( curPort > 0) peers[curIP.ToString()] = curPort;
            }

            return true;
        }
        public bool Scrape(string infoHash)
        {
            if ( Connect() ) return false;

            // Scrape Request
            action = Utils.ToBigEndian((Int32) Action.SCRAPE);
            data   = Utils.ArrayMerge(connID, action, tranID, Utils.StringHexToArray(infoHash) );

            udpClient.Send(data, data.Length);
            
            // Scrape Response
            if ( !Receive() ) return false;
            leechers    = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBytes,  8, 4, true), 0);
            completed   = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBytes, 12, 4, true), 0);
            seeders     = (UInt32) BitConverter.ToInt32(Utils.ArraySub(ref recvBytes, 16, 4, true), 0);

            return true;
        }

        internal void Log(string msg) { if (options.verbosity > 0) options.log.Write($"[Tracker ] {msg}"); }
    }
}