using System;
using System.IO;
using System.Text;
using System.Net.Sockets;

using BencodeNET.Parsing;
using BencodeNET.Objects;

namespace SuRGeoNix.TorSwarm
{
public class Peer
    {
        /* [HANDSHAKE]              | http://bittorrent.org/beps/bep_0003.html
         * Known Assigned Numbers   | http://bittorrent.org/beps/bep_0004.html      | Reserved Bit Allocations
         *                                                                                              |-68 bytes--|
         * 13 42 69 74 54 6f 72 72 65 6e 74 20 70 72 6f 74 6f 63 6f 6c  | 0x13 + "BitTorrent protocol"  | 20 bytes  | Static BIT_PROTO
         * 00 00 00 00 00 10 00 05                                      | Reserved Bit Allocations      |  8 bytes  | Static EXT_PROTO
         * e8 3f 49 9d d6 eb 76 94 21 a2 70 17 f3 e1 08 fc 7b 9f 60 f5  | SHA-1 Hash of info dictionary | 20 bytes  | Options.Hash      Set by Client
         * 2d 55 54 33 35 35 57 2d 3c b2 29 aa 15 7d 0b 62 6b b6 ce 56  | Unique Per Session Peer ID    | 20 bytes  | Options.PeerID    Set by Client */
        public static readonly byte[]   BIT_PROTO   = Utils.ArrayMerge(new byte[]   {0x13}, Encoding.ASCII.GetBytes("BitTorrent protocol"));
        public static readonly byte[]   EXT_PROTO   = Utils.ArrayMerge(new byte[]   {0, 0, 0, 0}, new byte[] {0 , 0x10, 0, (0x1 | 0x4)});

        // [HANDSHAKE EXTENDED]     | http://bittorrent.org/beps/bep_0010.html      | m-> {"key", "value"}, p, v, yourip, ipv6, ipv4, reqq  | Static EXT_BDIC
        public static readonly byte[]   EXT_BDIC    = (new BDictionary{ {"e", 0 }, {"m" , new BDictionary{ {"ut_metadata", 2 } /*,{"ut_pex" , 1 }*/ } }, { "reqq" , 250 } }).EncodeAsBytes();

        // [PEER MESSAGES]
        // Known Assigned Numbers   | http://bittorrent.org/beps/bep_0004.html      | Reserved Message IDs
        public static class Messages
        {
            // Core Protocol        | http://bittorrent.org/beps/bep_0052.html
            // <len><msg-id>[<payload>] <4 bytes><1 byte>[<X bytes>]                | len = 1 + X | len == 0 => Keep Alive (no msg-id or payload)
            public const byte CHOKE                 = 0x00;
            public const byte UNCHOKE               = 0x01;
            public const byte INTRESTED             = 0x02;
            public const byte NOT_INTRESTED         = 0x03;
            public const byte HAVE                  = 0x04;
            public const byte BITFIELD              = 0x05;
            public const byte REQUEST               = 0x06;
            public const byte PIECE                 = 0x07;
            public const byte CANCEL                = 0x08;

            // DHT Extension        | http://bittorrent.org/beps/bep_0005.html      | reserved[7] |= 0x01 | UDP Port for DHT (2 bytes)
            public const byte PORT                  = 0x09;

            // Hash Transfer 
            public const byte HASH_REQUEST          = 0x15;
            public const byte HASHES                = 0x16;
            public const byte HASH_REJECT           = 0x17;

            // Fast Extension       | http://bittorrent.org/beps/bep_0006.html      | reserved[7] |= 0x04
            public const byte SUGGEST_PIECE         = 0x0d;
            public const byte HAVE_NONE             = 0x0f;
            public const byte HAVE_ALL              = 0x0e;
            public const byte REJECT_REQUEST        = 0x10;
            public const byte ALLOW_FAST            = 0x11;

            // Extension Protocol   | http://bittorrent.org/beps/bep_0010.html      | reserved[5]  & 0x10   | LTEP (Libtorrent Extension Protocol)
            // <len><0x14><msg-id>[<payload>] <4 bytes><1 byte><1 byte>[<X bytes>]  | len = 1 + 1 + X
            public const byte EXTENDED              = 0x14;

            // LTEP Handshake
            public const byte EXTENDED_HANDSHAKE    = 0x00;

            // [msg-id = m -> ut_metadata]
            // Metadata Extenstion  | http://www.bittorrent.org/beps/bep_0009.html  | Extension for Peers to Send Metadata Files
            // Bencoded <msg_type><piece>[<total_size>]
            public const byte METADATA_REQUEST      = 0x00;
            public const byte METADATA_RESPONSE     = 0x01;
            public const byte METADATA_REJECT       = 0x02;

            // [msg-id = m -> ut_pex]
            // Peer Exchange (PEX)  | http://bittorrent.org/beps/bep_0011.html

            // [msg-id = m -> lt_donthave]
            // DontHave             | http://bittorrent.org/beps/bep_0054.html      | Old alternative of Fast Extension
        }

        /* Main Implementation  | Run()
         * -------------------
         * 
         * 1. TCP Connection    | Connect()         |   Status.NEW          -> Status.CONNECTING | Status.FAILED
         * 
         * 2. Handshake         | SendHandshake()   |   Status.CONNECTING   -> Status.CONNECTED  | Status.FAILED
         * 
         * 3. Process Messages  | ProcessMessages() |   Main Loop Receive(msglen) till next message 
         * 
         *  3.1 EXTENDED_HANDSHAKE                  |   Update StageYou Extensions
         *      SendExtendedHandshake()             |   Status.CONNECTED    -> Status.READY
         *  
         *  3.2 METADATA_RESPONSE
         *      MetadataReceivedClbk()              |   Status.DOWNLOADING  -> Status.READY
         *      
         *  3.3 METADATA_REJECT
         *      MetadataRejectedClbk()              |   Status.DOWNLOADING  -> Status.READY
         *      
         *  3.4 PIECE
         *      PieceReceivedClbk()                 |   Status.DOWNLOADING  -> Status.READY
         *      
         *  3.5 REJECT_REQUEST
         *      PieceRejectedClbk()                 |   Status.DOWNLOADING  -> Status.READY
         *      
         *  3.X BITFIELD | HAVE [ALL|NONE]          |   Update StageYou Bitfield
         *      
         *  3.X [UN]CHOKED | [NOT_]INTRESTED        |   Update StageYou
         * 
         * 
         * 4. Client Messages | Send<>()            |   TorSwarm Commands / Requests when Peer is READY
         * 
         *  4.1 METADATA_REQUEST
         *      RequestMetadata()                   |   Status.READY        -> Status.DOWNLOADING
         *      
         *  4.2 REQUEST
         *      RequestPiece()                      |   Status.READY        -> Status.DOWNLOADING
         *      
         *  4.X KEEP_ALIVE | INTRESTED etc.         |   Update StageMe
         */

        public enum Status
        {
            NEW             = 0,
            CONNECTING      = 1,
            CONNECTED       = 2,
            READY           = 3,
            FAILED1         = 4,
            FAILED2         = 5,
            //BANNED          = 6,
            DOWNLOADING     = 7
         }
        public struct Options
        {
            public string       Hash;
            public byte[]       PeerID;

            public int          Verbosity;
            public Logger       LogFile;

            public int          ConnectionTimeout;
            public int          HandshakeTimeout;
            public int          PieceTimeout;

            public Action<byte[], int, int, int, Peer>      MetadataReceivedClbk;
            public Action<int, string>                      MetadataRejectedClbk;
            public Action<byte[], int, int, Peer>           PieceReceivedClbk;
            public Action<int, int, int, Peer>              PieceRejectedClbk;
        }
        public struct Stage
        {
            public BitField     bitfield;
            public Extensions   extensions;

            public bool         handshake;
            public bool         handshakeEx;
            public bool         unchoked;
            public bool         intrested;
            public bool         haveAll;
            public bool         haveNone;

            public bool         fastMetadata;
        }
        public struct Extensions
        {
            public byte         ut_metadata;
        }

        public const int        MAX_DATA_SIZE   = 0x4000;
        public string           host        { get; private set; }
        public int              port        { get; private set; }

        public long             lastAction  { get; private set; }

        public Status           status;
        public Stage            stageYou;
        public Options          options;

        private static readonly BencodeParser parser = new BencodeParser();

        private TcpClient       tcpClient;
        private NetworkStream   tcpStream;

        private byte[]          tmpBuff;
        private byte[]          sendBuff;
        private byte[]          recvBuff;
        private int             recvLen;

        public Peer(string host, int port, Options options) 
        {  
            this.host           = host;
            this.port           = port;
            this.options        = options;

            stageYou            = new Stage();
            stageYou.extensions = new Extensions();
        }

        // Connection | Handshake | LTEP Handshake
        public bool Connect()
        {
            tcpClient       = new TcpClient();
            status          = Status.CONNECTING;
            bool connected;

            try
            {
                Log(3, "[CONNECTING] ... ");
                connected = tcpClient.ConnectAsync(host, port).Wait(options.ConnectionTimeout);
            }
            catch (AggregateException e1)
            {
                // AggregateException -> No such host is known
                // AggregateException -> No connection could be made because the target machine actively refused it 0.0.0.0:1234
                Log(2, "AggregateException -> " + e1.InnerException.Message);
                status = Status.FAILED2;
                return false;
            }
            catch (Exception e1)
            {
                // Exception-> Specified argument was out of the range of valid values.\r\nParameter name: port
                Log(1, "Exception -> " + e1.Message);
                status = Status.FAILED2; 
                return false;
            }

            // TIMEOUT or TCP RESET?
            if ( !connected ) { status = Status.FAILED1; return false; }

            Log(3, "[CONNECT] Success");
            tcpStream                   = tcpClient.GetStream();
            tcpClient.SendBufferSize    = 1024;
            tcpClient.ReceiveBufferSize = 1500;
            status                      = Status.CONNECTED;

            return true;
        }
        public void SendHandshake()
        {
            try
            {
                Log(3, "[HANDSHAKE] Handshake Sending ...");

                sendBuff = Utils.ArrayMerge(BIT_PROTO, EXT_PROTO, Utils.StringHexToArray(options.Hash), options.PeerID);
                tcpStream.Write(sendBuff, 0, sendBuff.Length);

                lastAction = DateTime.UtcNow.Ticks;
            } catch (Exception e)
            {
                Log(1, "[HANDSHAKE] Handshake Sending Error " + e.Message);
                status = Status.FAILED2;
                Disconnect();
            }
            
        }
        public void SendExtendedHandshake()
        {
            try
            {
                Log(3, "[HANDSHAKE] Extended Handshake Sending ...");

                sendBuff = Utils.ArrayMerge(PrepareMessage(0, true, EXT_BDIC), PrepareMessage(0xf, false, null), PrepareMessage(0x2, false, null)); // EXTDIC, HAVE NONE, INTRESTED
                tcpStream.Write(sendBuff, 0, sendBuff.Length);

                lastAction = DateTime.UtcNow.Ticks;
                status = Status.READY;
            } catch (Exception e)
            {
                Log(1, "[HANDSHAKE] Extended Handshake Sending Error " + e.Message);
                status = Status.FAILED2;
                Disconnect();
            }
        }
        public void Disconnect()
        {
            try
            {
                recvBuff    = new byte[0];
                sendBuff    = new byte[0];
                tmpBuff     = new byte[0];
                stageYou    = new Stage();

                if ( tcpClient != null ) tcpClient.Close();
            } catch ( Exception ) { /*Log($"[Disconnect] {e.Message}");*/ }
        }
        
        // Main Loop | Listening for Messages
        public void Run()
        {
            // CONNECT
            if ( !Connect() ) { Disconnect(); return; }

            // HANDSHAKE
            SendHandshake();
            tcpStream.ReadTimeout = options.HandshakeTimeout;
            try
            {
                Receive(BIT_PROTO.Length + EXT_PROTO.Length + 20 + 20);
            }
            catch (Exception e)
            {
                if ( !(status == Status.FAILED1 || status == Status.FAILED2) ) status = Status.FAILED2;

                if ( e.Message == "CUSTOM Connection closed" )
                    Log(1, "[ERROR][Handshake] " + e.Message);
                else
                    Log(1, "[ERROR][Handshake] " + e.Message + "\n" + e.StackTrace);
                Disconnect();
                return;
            }

            // RECV MESSAGES [LOOP Until Failed or Done]
            tcpStream.ReadTimeout = System.Threading.Timeout.Infinite;

            while ( status != Status.FAILED2 )
            {
                try
                {
                    ProcessMessage();
                }
                catch (Exception e)
                {
                    if ( !(status == Status.FAILED1 || status == Status.FAILED2) ) status = Status.FAILED2;
                    if ( e.Message == "CUSTOM Connection closed" )
                        Log(1, "[ERROR][Handshake] " + e.Message);
                    else
                        Log(1, "[ERROR][Handshake] " + e.Message + "\n" + e.StackTrace);
                    
                    Disconnect();
                    return;
                }
            }
        }
        private void ProcessMessage()
        {
            Receive(4); // MSG Length

            int msgLen = Utils.ToBigEndian(recvBuff);
            if ( msgLen == 0 ) { Log(4, "[MSG ] Keep Alive"); return; }

            Receive(1); // MSG Id

            switch ( recvBuff[0] )
            {
                                        // Core Messages | http://bittorrent.org/beps/bep_0052.html
                case Messages.REQUEST:
                    Log(4, "[MSG ] Request");
                    // TODO

                    break;

                case Messages.CHOKE:
                    Log(2, "[MSG ] Choke");
                    stageYou.unchoked = false;

                    break;
                case Messages.UNCHOKE:
                    Log(2, "[MSG ] Unchoke");
                    stageYou.unchoked = true;

                    break;
                case Messages.INTRESTED:
                    Log(3, "[MSG ] Intrested");
                    stageYou.intrested = true;

                    break;
                case Messages.HAVE:
                    Log(3, "[MSG ] Have");
                    Receive(msgLen - 1);

                    stageYou.bitfield.SetBit(Utils.ToBigEndian(recvBuff));

                    return;
                case Messages.BITFIELD:
                    Log(3, "[MSG ] Bitfield");

                    Receive(msgLen - 1);

                    stageYou.bitfield = new BitField(Utils.ArraySub(ref recvBuff,0,(uint) recvBuff.Length), recvBuff.Length * 8);

                    return;
                case Messages.PIECE:
                    Log(2, "[MSG ] Piece");

                    Receive(4);         // [Piece Id]
                    int piece = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Offset]
                    int offset = Utils.ToBigEndian(recvBuff);
                    Receive(msgLen - 9);// [Data]

                    status = Status.READY;
                    options.PieceReceivedClbk.BeginInvoke(recvBuff, piece, offset, this, null, null);

                    return;
                                        // DHT Extension        | http://bittorrent.org/beps/bep_0005.html | reserved[7] |= 0x01 | UDP Port for DHT 
                case Messages.PORT:
                    Log(3, "[MSG ] Port");
                    // TODO

                    break;

                                        // Fast Extensions      | http://bittorrent.org/beps/bep_0006.html | reserved[7] |= 0x04
                case Messages.REJECT_REQUEST:// Reject Request
                    Log(2, "[MSG ] Reject Request");

                    Receive(4);         // [Piece Id]
                    piece   = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Offset]
                    offset  = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Length]
                    int len = Utils.ToBigEndian(recvBuff);

                    status  = Status.READY;
                    options.PieceRejectedClbk.BeginInvoke(piece, offset, len, this, null, null);

                    return;
                case Messages.HAVE_NONE:
                    Log(3, "[MSG ] Have None");
                    stageYou.haveNone = true;

                    break;
                case Messages.HAVE_ALL:
                    Log(3, "[MSG ] Have All");
                    stageYou.haveAll = true;

                    break;
                case Messages.SUGGEST_PIECE:
                    Log(3, "[MSG ] Suggest Piece");
                    // TODO

                    break;
                case Messages.ALLOW_FAST:
                    Log(3, "[MSG ] Allowed Fast");
                    // TODO

                    break;

                                        // Extension Protocol   | http://bittorrent.org/beps/bep_0010.html | reserved_byte[5] & 0x10 | LTEP (Libtorrent Extension Protocol)
                case Messages.EXTENDED:
                    Receive(1); // MSG Extension Id

                    if ( recvBuff[0] == Messages.EXTENDED_HANDSHAKE ) {
                        Log(3, "[MSG ] Extended Handshake");

                        Receive(msgLen - 2);

                        // BEncode Dictionary [Currently fills stageYou.extensions.ut_metadata]
                        BDictionary extDic = parser.ParseString<BDictionary>(Encoding.ASCII.GetString(recvBuff));
                        object cur = Utils.GetFromBDic(extDic, new string[] {"m", "LT_metadata"});
                        if ( cur != null ) stageYou.extensions.ut_metadata = (byte) ((int) cur);
                        cur = Utils.GetFromBDic(extDic, new string[] {"m", "ut_metadata"});
                        if ( cur != null ) stageYou.extensions.ut_metadata = (byte) ((int) cur);

                        // MSG Extended Handshake | Reply
                        SendExtendedHandshake();

                        return;
                    }

                    // TODO: recvBuff[0] == extensions.ut_pex   | PEX http://bittorrent.org/beps/bep_0011.html

                    // Extension for Peers to Send Metadata Files | info-dictionary part of the .torrent file | http://bittorrent.org/beps/bep_0009.html
                    else if ( recvBuff[0] == stageYou.extensions.ut_metadata && stageYou.extensions.ut_metadata != 0 ) 
                    {
                        // MSG Extended Metadata
                        Log(3, "[MSG ] Extended Metadata");

                        status = Status.DOWNLOADING;
                        Receive(msgLen - 2);

                        // BEncoded msg_type
                        // MAX size of d8:msg_typei1e5:piecei99ee | d8:msg_typei1e5:piecei99e10:total_sizei1622016ee
                        uint tmp1               = recvBuff.Length > 49 ? 50 : (uint) recvBuff.Length;
                        string mdHeaders        = Encoding.ASCII.GetString(Utils.ArraySub(ref recvBuff, 0, tmp1));
                        BDictionary mdHeadersDic= parser.ParseString<BDictionary>(mdHeaders);

                        switch ( mdHeadersDic.Get<BNumber>("msg_type").Value )
                        {
                            case Messages.METADATA_REQUEST:
                                Log(3, "[MSG ] Extended Metadata Request");
                                break;

                            case Messages.METADATA_RESPONSE: // (Expecting 0x4000 | 16384 bytes - except if last piece)
                                Log(2, "[MSG ] Extended Metadata Data");
                                stageYou.fastMetadata = true;
                                options.MetadataReceivedClbk.BeginInvoke(recvBuff, (int) mdHeadersDic.Get<BNumber>("piece").Value, mdHeadersDic.EncodeAsString().Length, (int) mdHeadersDic.Get<BNumber>("total_size").Value, this, null, null);
                                break;

                            case Messages.METADATA_REJECT:
                                Log(2, "[MSG ] Extended Metadata Reject");
                                stageYou.fastMetadata = false;
                                options.MetadataRejectedClbk.BeginInvoke((int) mdHeadersDic.Get<BNumber>("piece").Value, host, null, null);
                                break;

                            default:
                                Log(4, "[MSG ] Extended Metadata Unknown " + mdHeadersDic.Get<BNumber>("msg_type").Value);
                                break;

                        } // Switch Metadata (msg_type)

                        status = Status.READY;
                        return; 
                    }
                    else
                    {
                        Log(4, "[MSG ] Extended Unknown " + recvBuff[0]);
                    }

                    Receive(msgLen - 2);

                    return; // Case Messages.EXTENDED    

                default:
                    Log(4, "[MSG ] Message Unknown " + recvBuff[0]);

                    break;
            } // Switch (MSG Id)

            Receive(msgLen - 1);
        }
        private void Receive(int len)
        {
            int curRead;
            int totalRead   = 0;
            tmpBuff         = new byte[len];
            
            using (MemoryStream ms = new MemoryStream())
            {
                while (totalRead < len)
                {
                    // 1. System.IO.IOException | Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.
                    // 2. System.IO.IOException | Unable to read data from the transport connection: A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond.
                    // 1. Probably RST          | 2. During Handsake that we set ReadTimeout
                    
                    // Catch remote-end FIN/ACK to avoid keeping a closed connection as opened
                    if ( tcpClient.Client.Poll(0, SelectMode.SelectRead) && !tcpClient.Client.Poll(0, SelectMode.SelectError) )
                    {
                        byte[] buff = new byte[1];
                        if( tcpClient.Client.Receive(buff, SocketFlags.Peek) == 0 )
                        { 
                            status = Status.FAILED2; 
                            throw new Exception("CUSTOM Connection closed"); 
                        }
                    }

                    curRead = tcpStream.Read(tmpBuff, 0, (len- totalRead));
                    
                    if ( curRead > 0 )
                    {
                        ms.Write(tmpBuff, 0, curRead);
                        totalRead += curRead;
                    } 
                }
                recvBuff = ms.ToArray();    
            }
            
            recvLen = totalRead;

            if ( recvLen != len )
            {
                Log(1, $"[MSG] Recv Len: {recvLen} != Req Len: {len}");
                throw new Exception($"[MSG] Recv Len: {recvLen} != Req Len: {len}");
            }

            lastAction = DateTime.UtcNow.Ticks;
        }
        
        // Client's Commands / Requests
        public void RequestMetadata(int piece)
        {
            try
            {
                status = Status.DOWNLOADING;
                SendMessage(stageYou.extensions.ut_metadata, true, Encoding.UTF8.GetBytes((new BDictionary { { "msg_type", 0 }, { "piece", piece } }).EncodeAsString()));
                lastAction = DateTime.UtcNow.Ticks;
            } catch (Exception e)
            {
                Log(1, $"[REQ][METADATA] {piece} {e.Message}");
                status = Status.FAILED2;
                Disconnect();
            }
        }
        public void RequestPiece(int piece, int offset, int len)
        {
            try
            {
                status      = Status.DOWNLOADING;
                sendBuff    = PrepareMessage(Messages.REQUEST, false, Utils.ArrayMerge(Utils.ToBigEndian((Int32) piece), Utils.ToBigEndian((Int32) offset), Utils.ToBigEndian((Int32) len))); //Utils.ArrayMerge();

                tcpStream.Write(sendBuff, 0, sendBuff.Length);
                lastAction = DateTime.UtcNow.Ticks;
            } catch (Exception e)
            {
                Log(1, $"[REQ ][PIECE][BLOCK] {piece}\t{offset}\t{len} {e.Message}\r\n{e.StackTrace}");
                status = Status.FAILED2;
                Disconnect();
            }
        }
        public void SendKeepAlive()
        {
            try
            {
                tcpStream.Write(new byte[] { 0, 0, 0, 0}, 0, 4);
                lastAction = DateTime.UtcNow.Ticks;
            } catch (Exception e)
            {
                Log(1, "[KEEPALIVE] Keep Alive Sending Error " + e.Message);
            }
            
        }

        // Misc
        public void SendMessage(byte msgid, bool isExtended, byte[] payload)
        {
            if (payload == null) payload = new byte[0];

            if ( isExtended )
            {
                sendBuff = Utils.ArrayMerge(Utils.ToBigEndian((Int32) (payload.Length + 2)), new byte[] { 20, msgid}, payload);
            }
            else
            {
                sendBuff = Utils.ArrayMerge(Utils.ToBigEndian((Int32) (payload.Length + 1)), new byte[] {msgid}, payload);
            }

            tcpStream.Write(sendBuff, 0, sendBuff.Length);

            lastAction = DateTime.UtcNow.Ticks;
        }
        public byte[] PrepareMessage(byte msgid, bool isExtended, byte[] payload)
        {
            int len = payload == null ? 0 : payload.Length;

            if ( isExtended )
            {
                byte[] tmp = new byte[4 + 2 + len];
                Buffer.BlockCopy((Utils.ToBigEndian((Int32) (len + 2))), 0, tmp, 0, 4);
                Buffer.BlockCopy(new byte[] { 20, msgid }, 0, tmp, 4, 2);
                if ( payload != null) Buffer.BlockCopy(payload, 0, tmp, 6, payload.Length);

                return tmp;
            }
            else
            {
                byte[] tmp = new byte[4 + 1 + len];
                Buffer.BlockCopy((Utils.ToBigEndian((Int32) (len + 1))), 0, tmp, 0, 4);
                Buffer.BlockCopy(new byte[] { msgid }, 0, tmp, 4, 1);
                if ( payload != null) Buffer.BlockCopy(payload, 0, tmp, 5, payload.Length);

                return tmp;
            }
        }
        private void Log(int level, string msg) { if (options.Verbosity > 0 && level <= options.Verbosity) options.LogFile.Write($"[Peer    ] [{host.PadRight(15, ' ')}] {msg}"); }
    }
}
