using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using BencodeNET.Parsing;
using BencodeNET.Objects;

using static SuRGeoNix.BitSwarm.BSTP;

namespace SuRGeoNix.BEP
{
    public class Peer
    {
        #region Declaration | Properties

        /* [HANDSHAKE]              | http://bittorrent.org/beps/bep_0003.html
         * Known Assigned Numbers   | http://bittorrent.org/beps/bep_0004.html      | Reserved Bit Allocations
         *                                                                                              |-68 bytes--|
         * 13 42 69 74 54 6f 72 72 65 6e 74 20 70 72 6f 74 6f 63 6f 6c  | 0x13 + "BitTorrent protocol"  | 20 bytes  | Static BIT_PROTO
         * 00 00 00 00 00 10 00 05                                      | Reserved Bit Allocations      |  8 bytes  | Static EXT_PROTO
         * e8 3f 49 9d d6 eb 76 94 21 a2 70 17 f3 e1 08 fc 7b 9f 60 f5  | SHA-1 Hash of info dictionary | 20 bytes  | Options.Hash      Set by Client
         * 2d 55 54 33 35 35 57 2d 3c b2 29 aa 15 7d 0b 62 6b b6 ce 56  | Unique Per Session Peer ID    | 20 bytes  | Options.PeerID    Set by Client */
        public static readonly byte[]   BIT_PROTO   = Utils.ArrayMerge(new byte[]   {0x13}, Encoding.UTF8.GetBytes("BitTorrent protocol"));
        public static readonly byte[]   EXT_PROTO   = Utils.ArrayMerge(new byte[]   {0, 0, 0, 0}, new byte[] {0 , 0x10, 0, (0x1 | 0x4)});

        // [HANDSHAKE EXTENDED]     | http://bittorrent.org/beps/bep_0010.html      | m-> {"key", "value"}, p, v, yourip, ipv6, ipv4, reqq  | Static EXT_BDIC
        public static readonly byte[]   EXT_BDIC    = (new BDictionary{ {"e", 0 }, {"m" , new BDictionary{ {"ut_metadata", 2 } /*,{"ut_pex" , 1 }*/ } }, { "reqq" , 250 } }).EncodeAsBytes();
        public static readonly int      MAX_DATA_SIZE   = 0x4000;

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
         * 4. Client Messages | Send<>()            |   BitSwarm Commands / Requests when Peer is READY
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
            DOWNLOADING     = 7
        }

        public static class Options
        {
            public static BitSwarm     Beggar;

            public static string       InfoHash;
            public static byte[]       PeerID;
            public static int          Pieces;

            public static int          Verbosity;
            public static Logger       LogFile;

            public static int          ConnectionTimeout;
            public static int          HandshakeTimeout;
        }
        public class Stage
        {
            public BitField     bitfield;
            public Extensions   extensions;

            public bool         handshake;
            public bool         handshakeEx;
            public bool         unchoked;
            public bool         intrested;
            public bool         haveAll;
            public bool         haveNone;

            public bool         metadataRequested;
        }
        public struct Extensions
        {
            public byte         ut_metadata;
        }

        
        public string           host        { get; private set; }
        public int              port        { get; private set; }

        public long             lastAction  { get; private set; }
        public long             connectedAt { get; private set; }
        public long             chokedAt    { get; private set; }

        public Status           status;
        public Stage            stageYou;

        private static readonly BencodeParser   bParser         = new BencodeParser();
        private readonly object                 lockerRequests  = new object();

        private TcpClient       tcpClient;
        private NetworkStream   tcpStream;

        private byte[]          sendBuff;
        private byte[]          recvBuff;
        private byte[]          recvBuffMax;

        public  int             PiecesRequested { get { return piecesRequested; } set { lock (lockerRequests) piecesRequested   = value; } }
        private int             piecesRequested;
        public  int             PiecesTimeout   { get { return piecesTimeout;   } set { lock (lockerRequests) piecesTimeout     = value; } }
        private int             piecesTimeout;

        private long            curTimeoutTicks;

        public List<Tuple<int, int, int>>   lastPieces;
        public List<int>                    allowFastPieces;

        public Peer(string host, int port) 
        {  
            this.host   = host;
            this.port   = port;
            status      = Status.NEW;
        }
        #endregion

        #region Connection | Handshake | LTEP Handshake | Disconnect
        public bool Connect()
        {
            tcpClient           = new TcpClient();
            tcpClient.NoDelay   = true;
            //status              = Status.CONNECTING;

            bool connected;

            try
            {
                if (Options.Verbosity > 0) Log(3, "[CONNECTING] ... ");
                connected = tcpClient.ConnectAsync(host, port).Wait(Options.ConnectionTimeout);
                if (!connected) tcpClient.Close();

                // Is Spin Worse?

                //var done = tcpClient.ConnectAsync(host, port);

                //int sleptMs = 0;
                //int stepMs = Options.ConnectionTimeout / 10;

                //while (!done.IsCompleted && sleptMs < Options.ConnectionTimeout)
                //{
                //    Thread.Sleep(stepMs);
                //    sleptMs += stepMs;
                //}

                //connected = done.IsCompleted && tcpClient.Connected;
            }
            catch (AggregateException e1)
            {
                // AggregateException -> No such host is known
                // AggregateException -> No connection could be made because the target machine actively refused it 0.0.0.0:1234
                if (Options.Verbosity > 0) Log(2, "AggregateException -> " + e1.InnerException.Message);
                status = Status.FAILED2;
                return false;
            }
            catch (Exception e1)
            {
                // Exception-> Specified argument was out of the range of valid values.\r\nParameter name: port
                if (Options.Verbosity > 0) Log(1, "Exception -> " + e1.Message);
                status = Status.FAILED2; 
                return false;
            }

            // TIMEOUT or TCP RESET?
            if (!connected) { status = Status.FAILED1; return false; }

            if (Options.Verbosity > 0) Log(3, "[CONNECT] Success");

            curTimeoutTicks = (long) Options.HandshakeTimeout * 10000;
            tcpStream       = tcpClient.GetStream();
            status          = Status.CONNECTED;

            return true;
        }
        public void SendHandshake()
        {
            try
            {
                if (Options.Verbosity > 0) Log(3, "[HANDSHAKE] Handshake Sending ...");

                sendBuff = Utils.ArrayMerge(BIT_PROTO, EXT_PROTO, Utils.StringHexToArray(Options.InfoHash), Options.PeerID);
                tcpStream.Write(sendBuff, 0, sendBuff.Length);

                lastAction          = DateTime.UtcNow.Ticks;
                connectedAt         = lastAction;
                chokedAt            = lastAction;

                lastPieces          = new List<Tuple<int, int, int>>();
                allowFastPieces     = new List<int>();

                stageYou            = new Stage();
                stageYou.extensions = new Extensions();
            } catch (Exception e)
            {
                if (Options.Verbosity > 0) Log(1, "[HANDSHAKE] Handshake Sending Error " + e.Message);
                Disconnect();
            }
            
        }
        public void SendExtendedHandshake()
        {
            try
            {
                if (Options.Verbosity > 0) Log(3, "[HANDSHAKE] Extended Handshake Sending ...");

                sendBuff = Utils.ArrayMerge(PrepareMessage(0, true, EXT_BDIC), PrepareMessage(0xf, false, null), PrepareMessage(0x2, false, null)); // EXTDIC, HAVE NONE, INTRESTED
                tcpStream.Write(sendBuff, 0, sendBuff.Length);

                //tcpClient.SendBufferSize    = 1500;
                tcpClient.ReceiveBufferSize = MAX_DATA_SIZE * 4;
                curTimeoutTicks             = (long)Options.Beggar.Options.PieceTimeout * 10000;
                recvBuffMax                 = new byte[MAX_DATA_SIZE];
                lastAction                  = DateTime.UtcNow.Ticks;
                status                      = Status.READY;
            } catch (Exception e)
            {
                if (Options.Verbosity > 0) Log(1, "[HANDSHAKE] Extended Handshake Sending Error " + e.Message);
                status = Status.FAILED2;
                Disconnect();
            }
        }
        public void Disconnect()
        {
            try
            {
                status      = Status.FAILED2;
                recvBuff    = null;
                sendBuff    = null;
                //stageYou    = null; // Currently Not Synch with Requests will drop null references

                if (tcpClient != null) tcpClient.Close();

            } catch (Exception) { }
        }

        #endregion


        #region Main Execution Flow (Connect -> Handshakes -> [ProcessMessages <-> Receive])
        public void Run(BSTPThread bstpThread)
        {
            // CONNECT
            if (!Connect()) { Disconnect(); return; }

            // HANDSHAKE
            SendHandshake();

            try
            {
                Receive(BIT_PROTO.Length + EXT_PROTO.Length + 20 + 20);
            }
            catch (Exception e)
            {
                if (e.Message == "CUSTOM Connection closed")
                    if (Options.Verbosity > 0) Log(1, "[ERROR][Handshake] " + e.Message);
                else
                    if (Options.Verbosity > 0) Log(1, "[ERROR][Handshake] " + e.Message + "\n" + e.StackTrace);

                Disconnect();

                return;
            }

            // RECV MESSAGES [LOOP Until Failed or Done]

            // Thread Transfer from Short-Run (BSTP) to Long-Run (Dedicated)
            tcpStream.ReadTimeout   = Timeout.Infinite;
            bstpThread.isLongRun    = true;
            Interlocked.Increment(ref LongRun);

            while (status != Status.FAILED2)
            {
                try
                {
                    ProcessMessage();
                }
                catch (Exception e)
                {
                    if (e.Message == "CUSTOM Connection closed")
                        if (Options.Verbosity > 0) Log(1, "[ERROR][Handshake] " + e.Message);
                    else
                        if (Options.Verbosity > 0) Log(1, "[ERROR][Handshake] " + e.Message + "\n" + e.StackTrace);
                    
                    Disconnect();

                    break;
                }
            }

            bstpThread.isLongRun = false;
            Interlocked.Decrement(ref LongRun);
        }
        private void ProcessMessage()
        {
            lastAction = DateTime.UtcNow.Ticks;

            Receive(4); // MSG Length

            int msgLen  = Utils.ToBigEndian(recvBuff);
            if (msgLen == 0) { if (Options.Verbosity > 0) Log(4, "[MSG ] Keep Alive"); return; }

            Receive(1); // MSG Id

            switch (recvBuff[0])
            {
                                        // Core Messages | http://bittorrent.org/beps/bep_0052.html
                case Messages.REQUEST:
                    if (Options.Verbosity > 0) Log(4, "[MSG ] Request");
                    // TODO

                    break;

                case Messages.CHOKE:
                    if (Options.Verbosity > 0) Log(2, "[MSG ] Choke");
                    stageYou.unchoked = false;
                    chokedAt = DateTime.UtcNow.Ticks;

                    piecesTimeout -= piecesRequested; // Avoid dropping peer (until new timeouts)

                    return;
                case Messages.UNCHOKE:
                    if (Options.Verbosity > 0) Log(2, "[MSG ] Unchoke");
                    stageYou.unchoked = true;

                    if (Options.Beggar.isRunning && status == Status.READY) Options.Beggar.RequestPiece(this);

                    return;
                case Messages.INTRESTED:
                    if (Options.Verbosity > 0) Log(3, "[MSG ] Intrested");
                    stageYou.intrested = true;

                    break;
                case Messages.HAVE:
                    if (Options.Verbosity > 0) Log(3, "[MSG ] Have");
                    Receive(msgLen - 1);

                    stageYou.haveNone = false;

                    if (stageYou.bitfield == null)
                    {
                        if (Options.Pieces != 0)
                            stageYou.bitfield = new BitField(Options.Pieces);
                        else
                            stageYou.bitfield = new BitField(15000); // MAX PIECES GUESS?
                    }

                    int havePiece = Utils.ToBigEndian(recvBuff);
                    stageYou.bitfield.SetBit(havePiece);

                    return;
                case Messages.BITFIELD:
                    if (Options.Verbosity > 0) Log(3, "[MSG ] Bitfield");

                    Receive(msgLen - 1);

                    stageYou.haveNone   = false;
                    byte[] bitfield     = new byte[recvBuff.Length];
                    Buffer.BlockCopy(recvBuff, 0, bitfield, 0, recvBuff.Length);
                    stageYou.bitfield   = new BitField(bitfield, Options.Pieces != 0 ? Options.Pieces : recvBuff.Length * 8);

                    return;
                case Messages.PIECE:
                    if (Options.Verbosity > 0) Log(2, "[MSG ] Piece");

                    Receive(4);         // [Piece Id]
                    int piece   = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Offset]
                    int offset  = Utils.ToBigEndian(recvBuff);
                    Receive(msgLen - 9);// [Data]

                    lock (lockerRequests) piecesRequested--;

                    if (piecesRequested == 0)
                    {
                        status = Status.READY;
                        //else if (piecesRequested <= 4) 
                        //{
                        
                        if (Options.Beggar.isRunning)
                        {
                            if (stageYou.unchoked)
                                Options.Beggar.RequestPiece(this);
                            else if (allowFastPieces.Count > 0)
                                Options.Beggar.RequestFastPiece(this);
                        }
                    }

                    Options.Beggar.PieceReceived(msgLen - 9 == MAX_DATA_SIZE ? recvBuffMax : recvBuff, piece, offset, this);

                    return;
                                        // DHT Extension        | http://bittorrent.org/beps/bep_0005.html | reserved[7] |= 0x01 | UDP Port for DHT 
                case Messages.PORT:
                    if (Options.Verbosity > 0) Log(3, "[MSG ] Port");

                    // TODO: Add them in DHT as a 3rd Strategy?

                    break;
                                        // Fast Extensions      | http://bittorrent.org/beps/bep_0006.html | reserved[7] |= 0x04
                case Messages.REJECT_REQUEST:// Reject Request
                    if (Options.Verbosity > 0) Log(2, "[MSG ] Reject Request");

                    Receive(4);         // [Piece Id]
                    piece   = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Offset]
                    offset  = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Length]
                    int len = Utils.ToBigEndian(recvBuff);

                    lock (lockerRequests) piecesRequested--;

                    Options.Beggar.PieceRejected(piece, offset, len, this);

                    if (piecesRequested == 0)
                    {
                        status = Status.READY;
                        if (Options.Beggar.isRunning)
                        {
                            if (stageYou.unchoked)
                                Options.Beggar.RequestPiece(this);
                            else if (allowFastPieces.Count > 0)
                                Options.Beggar.RequestFastPiece(this);
                        }
                    }

                    return;
                case Messages.HAVE_NONE:
                    if (Options.Verbosity > 0) Log(3, "[MSG ] Have None");
                    stageYou.haveNone = true;

                    return;
                case Messages.HAVE_ALL:
                    if (Options.Verbosity > 0) Log(3, "[MSG ] Have All");
                    stageYou.haveAll = true;

                    return;
                case Messages.SUGGEST_PIECE:
                    if (Options.Verbosity > 0) Log(3, "[MSG ] Suggest Piece");
                    // TODO

                    break;
                case Messages.ALLOW_FAST:
                    Receive(4);         // [Piece Id]
                    int allowFastPiece = Utils.ToBigEndian(recvBuff);
                    if (allowFastPiece < 0) return;

                    allowFastPieces.Add(allowFastPiece);

                    if (Options.Verbosity > 0) Log(3, $"[MSG ] Allowed Fast [Piece: {allowFastPiece}]");

                    if (Options.Beggar.isRunning && status == Status.READY)
                    {
                        if (stageYou.unchoked)
                            Options.Beggar.RequestPiece(this);
                        else if (allowFastPieces.Count > 0)
                            Options.Beggar.RequestFastPiece(this);
                    }

                    return;

                                        // Extension Protocol   | http://bittorrent.org/beps/bep_0010.html | reserved_byte[5] & 0x10 | LTEP (Libtorrent Extension Protocol)
                case Messages.EXTENDED:
                    Receive(1); // MSG Extension Id

                    if (recvBuff[0] == Messages.EXTENDED_HANDSHAKE)
                    {
                        if (Options.Verbosity > 0) Log(3, "[MSG ] Extended Handshake");

                        Receive(msgLen - 2);

                        // BEncode Dictionary [Currently fills stageYou.extensions.ut_metadata]
                        BDictionary extDic  = bParser.Parse<BDictionary>(recvBuff);
                        object cur          = Utils.GetFromBDic(extDic, new string[] {"m", "LT_metadata"});
                        if (cur != null)    stageYou.extensions.ut_metadata = (byte) ((int) cur);
                        cur                 = Utils.GetFromBDic(extDic, new string[] {"m", "ut_metadata"});
                        if (cur != null)    stageYou.extensions.ut_metadata = (byte) ((int) cur);

                        // MSG Extended Handshake | Reply
                        SendExtendedHandshake();

                        return;
                    }

                    // TODO: recvBuff[0] == extensions.ut_pex   | PEX http://bittorrent.org/beps/bep_0011.html

                    // Extension for Peers to Send Metadata Files | info-dictionary part of the .torrent file | http://bittorrent.org/beps/bep_0009.html
                    else if (recvBuff[0] == stageYou.extensions.ut_metadata && stageYou.extensions.ut_metadata != 0) 
                    {
                        // MSG Extended Metadata
                        if (Options.Verbosity > 0) Log(3, "[MSG ] Extended Metadata");

                        bool wasDownloading = status == Status.DOWNLOADING;
                        int buffSize        = msgLen - 2;
                        status              = Status.DOWNLOADING;

                        Receive(buffSize);

                        // BEncoded msg_type
                        // MAX size of d8:msg_typei1e5:piecei99ee | d8:msg_typei1e5:piecei99e10:total_sizei1622016ee
                        uint tmp1               = buffSize > 49 ? 50 : (uint) buffSize;
                        byte[] mdheadersBytes   = buffSize == MAX_DATA_SIZE ? Utils.ArraySub(ref recvBuffMax, 0, tmp1) : Utils.ArraySub(ref recvBuff, 0, tmp1);
                        BDictionary mdHeadersDic= bParser.Parse<BDictionary>(mdheadersBytes);

                        switch (mdHeadersDic.Get<BNumber>("msg_type").Value)
                        {
                            case Messages.METADATA_REQUEST:
                                if (Options.Verbosity > 0) Log(3, "[MSG ] Extended Metadata Request");
                                break;

                            case Messages.METADATA_RESPONSE: // (Expecting 0x4000 | 16384 bytes - except if last piece)
                                if (Options.Verbosity > 0) Log(2, "[MSG ] Extended Metadata Data");
                                Options.Beggar.MetadataPieceReceived(buffSize == MAX_DATA_SIZE ? recvBuffMax : recvBuff, (int) mdHeadersDic.Get<BNumber>("piece").Value, mdHeadersDic.EncodeAsString().Length, (int) mdHeadersDic.Get<BNumber>("total_size").Value, this);

                                break;

                            case Messages.METADATA_REJECT:
                                if (Options.Verbosity > 0) Log(2, "[MSG ] Extended Metadata Reject");
                                Options.Beggar.MetadataPieceRejected((int) mdHeadersDic.Get<BNumber>("piece").Value, host);

                                break;

                            default:
                                if (Options.Verbosity > 0) Log(4, "[MSG ] Extended Metadata Unknown " + mdHeadersDic.Get<BNumber>("msg_type").Value);
                                break;

                        } // Switch Metadata (msg_type)

                        if (!wasDownloading || piecesRequested < 1) // In case of late response of Metadata when Metadata already done and we already requested pieces
                            status = Status.READY;
                        return; 
                    }
                    else
                    {
                        if (Options.Verbosity > 0) Log(4, "[MSG ] Extended Unknown " + recvBuff[0]);
                    }

                    Receive(msgLen - 2);

                    return; // Case Messages.EXTENDED    

                default:
                    if (Options.Verbosity > 0) Log(4, "[MSG ] Message Unknown " + recvBuff[0]);

                    break;
            } // Switch (MSG Id)

            Receive(msgLen - 1); // Ensure Len > 0
        }
        
        private void Receive(int len)
        {
            long startedAt  = 0;//  = DateTime.UtcNow.Ticks;
            int  curLoop    = 0;
            int  tooManyTmp = 0;
            while (tcpClient.Client.Available < len)
            {
                Thread.Sleep(20);
                curLoop ++;

                if (startedAt == 0) 
                    startedAt = DateTime.UtcNow.Ticks;
                else
                {
                    // Check Timeout
                    if (curLoop % 5 == 0 && DateTime.UtcNow.Ticks - startedAt > curTimeoutTicks)
                    {
                        if (status == Status.READY || status == Status.DOWNLOADING)
                        {
                            if (Options.Verbosity > 0) Log(4, $"[NEWTIMEOUT] Checking"); // TODO
                            startedAt = DateTime.UtcNow.Ticks; // Currently let Beggar to control it
                            tooManyTmp++;

                            if (tooManyTmp == 4)
                            {
                                if (Options.Verbosity > 0) Log(4, $"[DROP] [NEWTIMEOUT] Beggar should already drop it");
                                status = Status.FAILED2;
                                throw new Exception("CUSTOM Connection closed");
                            }
                        }
                        else
                        {
                            if (Options.Verbosity > 0) Log(4, $"[DROP] [NEWTIMEOUT] Handshake? {len == 68}");
                            status = Status.FAILED2;
                            throw new Exception("CUSTOM Connection closed");
                        }

                    }
                }
                
                // Check Disconnect
                if (curLoop % 7 == 0) // && !(status == Status.READY || status == Status.DOWNLOADING))
                {
                    // 1. System.IO.IOException | Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.
                    // 2. System.IO.IOException | Unable to read data from the transport connection: A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond.
                    // 1. Probably RST          | 2. During Handsake that we set ReadTimeout

                    // Catch remote-end FIN/ACK to avoid keeping a closed connection as opened
                    if (tcpClient.Client.Poll(0, SelectMode.SelectRead) && !tcpClient.Client.Poll(0, SelectMode.SelectError))
                    {
                        byte[] buff = new byte[1];
                        if (tcpClient.Client.Receive(buff, SocketFlags.Peek) == 0)
                        {
                            status = Status.FAILED2;
                            throw new Exception("CUSTOM Connection closed");
                        }
                    }
                }
            }

            if (len == MAX_DATA_SIZE)
                tcpStream.Read(recvBuffMax, 0, len);
            else
                { recvBuff = new byte[len]; tcpStream.Read(recvBuff, 0, len); }

            //if (Options.Beggar.Stats.SleepMode && len >= MAX_DATA_SIZE) Thread.Sleep(25);
        }
        private void ReceiveAlternative(int len)
        {
            // Testing another approach for unix platforms

            long timeout    = len == 68 ? (long)Options.HandshakeTimeout * 10000 : (long)Options.Beggar.Options.PieceTimeout * 10000;
            long startedAt  = DateTime.UtcNow.Ticks;
            recvBuff        = new byte[len];
            int read = 0;
            string err = "";
            SocketError se;
            int offset = 0;
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);

            tcpClient.ReceiveTimeout = len == 68 ? Options.HandshakeTimeout : Options.Beggar.Options.PieceTimeout;
                
            while (offset != len)
            {
                tcpClient.Client.BeginReceive(recvBuff, offset, recvBuff.Length - offset, SocketFlags.None, ar =>
                {   
                    try
                    {
                        read = tcpClient.Client.EndReceive(ar, out se);
                            
                        if (!ar.IsCompleted || se != SocketError.Success) { err = "1"; read = 0; }
                    } 
                    catch (Exception e)
                    {
                        err = e.Message;
                        read = 0;
                        Log(0, "D001 " + e.Message);
                    }
                    finally
                    {
                        offset += read;
                        autoResetEvent.Set();
                    }
                    
                } , null);

                autoResetEvent.WaitOne();
                if (read == 0) break;
            }

            if (offset != len) throw new Exception("D001 Receive Error");
        }

        #endregion

        #region Outgoing Messages | Requests
        public void RequestMetadata(int piece, int piece2 = -1)
        {
            try
            {
                sendBuff = PrepareMessage(stageYou.extensions.ut_metadata, true, Encoding.UTF8.GetBytes((new BDictionary { { "msg_type", 0 }, { "piece", piece } }).EncodeAsString()));
                if (piece2 != -1)
                    sendBuff = Utils.ArrayMerge(sendBuff, PrepareMessage(stageYou.extensions.ut_metadata, true, Encoding.UTF8.GetBytes((new BDictionary { { "msg_type", 0 }, { "piece", piece2 } }).EncodeAsString())));

                tcpStream.Write(sendBuff, 0, sendBuff.Length);
                lastAction = DateTime.UtcNow.Ticks;
            } catch (Exception e)
            {
                if (Options.Verbosity > 0) Log(1, $"[REQ][METADATA] {piece},{piece2} {e.Message}");
                Disconnect();
            }
        }
        public void RequestPiece(List<Tuple<int, int, int>> pieces) // piece, offset, len
        {
            try
            {
                lock (lockerRequests)
                {
                    status      = Status.DOWNLOADING;
                    sendBuff    = new byte[0];

                    foreach (Tuple<int, int, int> piece in pieces)
                        sendBuff = Utils.ArrayMerge(sendBuff, PrepareMessage(Messages.REQUEST, false, Utils.ArrayMerge(Utils.ToBigEndian((Int32) piece.Item1), Utils.ToBigEndian((Int32) piece.Item2), Utils.ToBigEndian((Int32) piece.Item3))));

                    tcpStream.Write(sendBuff, 0, sendBuff.Length);

                    lastPieces      = pieces;
                    piecesRequested+= pieces.Count;
                    lastAction      = DateTime.UtcNow.Ticks;
                }

            } catch (Exception e)
            {
                if (Options.Verbosity > 0) Log(1, $"[REQ ] Send Failed - {e.Message}\r\n{e.StackTrace}");
                Disconnect();
            }
        }
        #endregion


        #region Currently Disabled
        public void SendKeepAlive()
        {
            try
            {
                if (Options.Verbosity > 0) Log(4, "[MSG ] Sending Keep Alive");

                tcpStream.Write(new byte[] { 0, 0, 0, 0}, 0, 4);
                lastAction = DateTime.UtcNow.Ticks;
            } catch (Exception e)
            {
                Log(1, "[KEEPALIVE] Keep Alive Sending Error " + e.Message);
            }
            
        }
        public void RequestPieceRemovedJustOneBlock(int piece, int offset, int len)
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
                Disconnect();
            }
        }
        public void CancelPieces()
        {
            try
            {
                lock (lockerRequests)
                {
                    sendBuff = new byte[0];

                    foreach (Tuple<int, int, int> piece in lastPieces)
                        sendBuff = Utils.ArrayMerge(sendBuff, PrepareMessage(Messages.CANCEL, false, Utils.ArrayMerge(Utils.ToBigEndian((Int32)piece.Item1), Utils.ToBigEndian((Int32)piece.Item2), Utils.ToBigEndian((Int32)piece.Item3))));

                    tcpStream.Write(sendBuff, 0, sendBuff.Length);

                    lastAction = DateTime.UtcNow.Ticks;
                }
            }
            catch (Exception e)
            {
                if (Options.Verbosity > 0) Log(1, $"[REQ ] Send Cancel Failed - {e.Message}\r\n{e.StackTrace}");
                status = Status.FAILED2;
                Disconnect();
            }
        }
        public void CancelPieces(int piece, int offset, int len)
        {
            try
            {
                lock (lockerRequests)
                {
                    if (Options.Verbosity > 0) Log(2, $"[CANCELING PIECE] Piece: {piece} Offset: {offset}");

                    sendBuff = new byte[0];
                    Utils.ArrayMerge(sendBuff, PrepareMessage(Messages.CANCEL, false, Utils.ArrayMerge(Utils.ToBigEndian((Int32) piece), Utils.ToBigEndian((Int32) offset), Utils.ToBigEndian((Int32) len))));

                    tcpStream.Write(sendBuff, 0, sendBuff.Length);

                    lastAction = DateTime.UtcNow.Ticks;
                }
            }
            catch (Exception e)
            {
                if (Options.Verbosity > 0) Log(1, $"[REQ ] Send Cancel Failed - {e.Message}\r\n{e.StackTrace}");
                status = Status.FAILED2;
                Disconnect();
            }
        }
        #endregion

        #region Misc        
        public void SendMessage(byte msgid, bool isExtended, byte[] payload)
        {
            try
            {
                if (payload == null) payload = new byte[0];

                if (isExtended)
                {
                    sendBuff = Utils.ArrayMerge(Utils.ToBigEndian((Int32) (payload.Length + 2)), new byte[] { 20, msgid}, payload);
                }
                else
                {
                    sendBuff = Utils.ArrayMerge(Utils.ToBigEndian((Int32) (payload.Length + 1)), new byte[] {msgid}, payload);
                }

                tcpStream.Write(sendBuff, 0, sendBuff.Length);

                lastAction = DateTime.UtcNow.Ticks;
            } catch (Exception e)
            {
                if (Options.Verbosity > 0) Log(1, "[SENDMESSAGE] Sending Error " + e.Message);
            }
        }
        public byte[] PrepareMessage(byte msgid, bool isExtended, byte[] payload)
        {
            int len = payload == null ? 0 : payload.Length;

            if (isExtended)
            {
                byte[] tmp = new byte[4 + 2 + len];
                Buffer.BlockCopy((Utils.ToBigEndian((Int32) (len + 2))), 0, tmp, 0, 4);
                Buffer.BlockCopy(new byte[] { 20, msgid }, 0, tmp, 4, 2);
                if (payload != null) Buffer.BlockCopy(payload, 0, tmp, 6, payload.Length);

                return tmp;
            }
            else
            {
                byte[] tmp = new byte[4 + 1 + len];
                Buffer.BlockCopy((Utils.ToBigEndian((Int32) (len + 1))), 0, tmp, 0, 4);
                Buffer.BlockCopy(new byte[] { msgid }, 0, tmp, 4, 1);
                if (payload != null) Buffer.BlockCopy(payload, 0, tmp, 5, payload.Length);

                return tmp;
            }
        }
        internal void Log(int level, string msg) { if (Options.Verbosity > 0 && level <= Options.Verbosity) Options.LogFile.Write($"[Peer    ] [{host.PadRight(15, ' ')}] {msg}"); }
        #endregion
    }
}