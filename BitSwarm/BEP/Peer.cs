using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BencodeNET.Parsing;
using BencodeNET.Objects;

namespace SuRGeoNix.BitSwarmLib.BEP
{
    class Peer
    {
        #region Declaration | Properties

        /* [HANDSHAKE]              | http://bittorrent.org/beps/bep_0003.html
         * Known Assigned Numbers   | http://bittorrent.org/beps/bep_0004.html      | Reserved Bit Allocations
         *                                                                                              |-68 bytes--|
         * 13 42 69 74 54 6f 72 72 65 6e 74 20 70 72 6f 74 6f 63 6f 6c  | 0x13 + "BitTorrent protocol"  | 20 bytes  | Static BIT_PROTO
         * 00 00 00 00 00 10 00 05                                      | Reserved Bit Allocations      |  8 bytes  | Static EXT_PROTO
         * e8 3f 49 9d d6 eb 76 94 21 a2 70 17 f3 e1 08 fc 7b 9f 60 f5  | SHA-1 Hash of info dictionary | 20 bytes  | Options.Hash      Set by Client
         * 2d 55 54 33 35 35 57 2d 3c b2 29 aa 15 7d 0b 62 6b b6 ce 56  | Unique Per Session Peer ID    | 20 bytes  | Options.PeerID    Set by Client */
        public static readonly byte[]   BIT_PROTO       = Utils.ArrayMerge(new byte[]   {0x13}, Encoding.UTF8.GetBytes("BitTorrent protocol"));
        public static readonly byte[]   EXT_PROTO       = Utils.ArrayMerge(new byte[]   {0, 0, 0, 0}, new byte[] {0 , 0x10, 0, (0x1 | 0x4)});

        public static readonly int      MAX_DATA_SIZE   = 0x4000;

        // [HANDSHAKE EXTENDED]     | http://bittorrent.org/beps/bep_0010.html      | m-> {"key", "value"}, p, v, yourip, ipv6, ipv4, reqq  | Static EXT_BDIC
        public static byte[]            EXT_BDIC;        // Will be set by bitswarm's setup

        public static byte[]            HANDSHAKE_BYTES; // Will be set by bitswarm's setup
        public static BitSwarm          Beggar;          // Will be change later on for multiple-instances
        

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

            // [msg-id = m -> ut_pex]
            // Peer Exchange (PEX)  | http://bittorrent.org/beps/bep_0011.html
            public const byte EXT_UT_PEX            = 0x01;

            // [msg-id = m -> ut_metadata]
            // Metadata Extenstion  | http://www.bittorrent.org/beps/bep_0009.html  | Extension for Peers to Send Metadata Files
            // Bencoded <msg_type><piece>[<total_size>]
            public const byte EXT_UT_METADATA       = 0x02;
            public const byte METADATA_REQUEST      = 0x00;
            public const byte METADATA_RESPONSE     = 0x01;
            public const byte METADATA_REJECT       = 0x02;

            // [msg-id = m -> lt_donthave]
            // DontHave             | http://bittorrent.org/beps/bep_0054.html      | Extension for advertising that it no longer has a piece (on previous Have/BitField messages)
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
            FAILED          = 4,
            DOWNLOADING     = 5
        }

        public class Stage
        {
            public string       version;

            public Bitfield     bitfield;
            public Extensions   extensions;

            public bool         unchoked;
            public bool         intrested;
            public bool         haveAll;
            public bool         haveNone;

            public bool         metadataRequested;
            public int          metadataPiecesRequested;
        }
        public struct Extensions
        {
            public byte         ut_metadata;
        }

        public string           host        { get; private set; }
        public int              port        { get; private set; }

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

        public  int             PieceTimeouts   { get; private set; }
        public  int             PieceRejects    { get; private set; }

        public List<Tuple<int, int, int>>   lastPieces;
        public List<int>                    allowFastPieces;

        public long lastDownloadAt;
        private long blockRequestedAt;
        private long totalWaitDuration = 0;
        private long totalBytesReceived = 0;

        IAsyncResult taskConnect;
        int curStep = 1;
        int waitingFor = 0;
        long curTimeout = 0;
        long lastRequest;

        public Peer(string host, int port) 
        {  
            this.host   = host;
            this.port   = port;
            status      = Status.NEW;
        }
        #endregion

        #region Connection | Handshake | LTEP Handshake | Disconnect

        
        
        #endregion


        #region Main Execution Flow (Connect -> Handshakes -> [ProcessMessages <-> Receive])
        public void RunNext()
        {
            try
            {
                if (status == Status.FAILED) return;

                if (waitingFor != 0)
                {
                    if (tcpClient.Client.Available < waitingFor)
                    {
                        if (curTimeout != 0 && Beggar.Stats.CurrentTime - lastRequest > curTimeout)
                            Dispose();
                        else if (status == Status.CONNECTED && curStep == 4)
                        {
                            if (Beggar.Stats.CurrentTime - lastRequest > Beggar.Options.PieceTimeout) // Extended Handshake timeout TBR
                                Dispose();
                        }
                        else if (status == Status.DOWNLOADING)
                        {
                            long curTimeout = Beggar.FocusAreInUse && Beggar.Options.EnableBuffering ? Beggar.Options.PieceBufferTimeout * 10000 : Beggar.Options.PieceTimeout * 10000;

                            if (Beggar.Stats.CurrentTime - blockRequestedAt > curTimeout)
                            {
                                PieceTimeouts++;

                                int  curRetries = Beggar.FocusAreInUse && Beggar.Options.EnableBuffering ? Beggar.Options.PieceBufferRetries : Beggar.Options.PieceRetries;
                                bool bufferTimeoutUsed = Beggar.Options.EnableBuffering && Beggar.FocusAreInUse;

                                if (PieceTimeouts == 1 && ((bufferTimeoutUsed && Beggar.Options.PieceBufferRetries > 0) || (!bufferTimeoutUsed && Beggar.Options.PieceRetries > 0)) && lastPieces != null && lastPieces.Count > 0) Beggar.ResetRequests(this, lastPieces);

                                if (Beggar.Options.Verbosity > 0) Log(4, $"[TIMEOUT] {PieceTimeouts} ({tcpClient.Client.Available} < {waitingFor} , Requests: {PiecesRequested}, Pieces: {lastPieces.Count}, Timeouts: {PieceTimeouts})");

                                if (PieceTimeouts > curRetries)
                                {
                                    if (Beggar.Options.Verbosity > 0) Log(3, $"[DROP] Piece Timeout ({tcpClient.Client.Available} < {waitingFor} , Requests: {PiecesRequested}, Pieces: {lastPieces.Count}, Timeouts: {PieceTimeouts})");

                                    Interlocked.Increment(ref Beggar.Stats.PieceTimeouts);
                                    Dispose();
                                }
                                else
                                    blockRequestedAt = Beggar.Stats.CurrentTime;
                            }
                        }

                        return;
                    }
                }

                switch (curStep)
                {
                    case 1:
                        if (Beggar.Options.Verbosity > 0) Log(3, "[CONN] ... ");

                        tcpClient   = new TcpClient();
                        tcpClient.NoDelay = true;
                        taskConnect = tcpClient.BeginConnect(host, port, null, null);

                        status      = Status.CONNECTING;
                        Beggar.Stats.PeersConnecting++;

                        lastRequest = DateTime.UtcNow.Ticks;
                        curTimeout  = Beggar.Options.ConnectionTimeout * (long)10000;
                        curStep++;

                        break;

                    case 2:
                        if (taskConnect.IsCompleted)
                        {
                            Beggar.Stats.PeersConnecting--;

                            if (tcpClient.Connected)
                            {
                                status = Status.CONNECTED;
                                tcpStream= tcpClient.GetStream();

                                if (Beggar.Options.Verbosity > 0) Log(3, "[HAND] Sending");
                                tcpStream.Write(HANDSHAKE_BYTES, 0, HANDSHAKE_BYTES.Length);
                                waitingFor = BIT_PROTO.Length + EXT_PROTO.Length + 20 + 20;

                                lastRequest = DateTime.UtcNow.Ticks;
                                lastDownloadAt = lastRequest; // to avoid dropping it early
                                curTimeout  = Beggar.Options.HandshakeTimeout * (long)10000;
                                curStep++;
                            }
                            else
                                Dispose();
                        }
                        else
                        {
                            if (Beggar.Stats.CurrentTime - lastRequest > curTimeout) { Beggar.Stats.PeersConnecting--; Dispose(); }
                        }

                        break;

                    case 3:
                        Receive(BIT_PROTO.Length + EXT_PROTO.Length + 20 + 20);
                        if (Beggar.Options.Verbosity > 0) Log(3, "[HAND] Received");

                        lastPieces          = new List<Tuple<int, int, int>>();
                        allowFastPieces     = new List<int>();

                        stageYou            = new Stage();
                        stageYou.extensions = new Extensions();

                        tcpStream.ReadTimeout = Timeout.Infinite;

                        lastRequest = DateTime.UtcNow.Ticks; // We need this for Extended handshake timeout
                        waitingFor = 4;
                        curTimeout = 0;
                        curStep++;
                        RunNext();

                        break;

                    case 4:
                        if (status == Status.READY && tcpClient.Client.Available == 0)
                        {
                            if (!Beggar.torrent.metadata.isDone)
                            {
                                if (Beggar.OptionsClone.MetadataParallelReq > 0 && stageYou.extensions.ut_metadata != 0 && stageYou.metadataPiecesRequested == 0 && lastPieces.Count == 0) { stageYou.metadataRequested = false; Beggar.RequestPiece(this); }
                            }
                            else
                            {
                                if (stageYou.unchoked)
                                    Beggar.RequestPiece(this);
                                else if (!Beggar.FocusAreInUse && allowFastPieces.Count > 0)
                                    Beggar.RequestFastPiece(this);
                            }
                        }

                        Receive(4); // MSG Length
                        waitingFor  = Utils.ToBigEndian(recvBuff);
                        waitingFor  = waitingFor < 0 ? -1 : (waitingFor > MAX_DATA_SIZE * 2 ? -1 : waitingFor); // MAX_DATA_SIZE + X + 1 + 4?
                        if (waitingFor == -1)
                        {
                            if (Beggar.Options.Verbosity > 0) Log(1, $"Invalid wait for {Utils.ToBigEndian(recvBuff)} bytes");
                            Dispose();
                        }
                        curStep = 5;
                        RunNext();

                        break;

                    case 5:
                        ProcessMessage(waitingFor);

                        if (status == Status.READY)
                        {
                            if (!Beggar.torrent.metadata.isDone)
                            {
                                if (Beggar.OptionsClone.MetadataParallelReq > 0 && stageYou.extensions.ut_metadata != 0 && stageYou.metadataPiecesRequested == 0 && lastPieces.Count == 0) { stageYou.metadataRequested = false; Beggar.RequestPiece(this); }
                            }
                            else
                            {
                                if (stageYou.unchoked)
                                    Beggar.RequestPiece(this);
                                else if (!Beggar.FocusAreInUse && allowFastPieces.Count > 0)
                                    Beggar.RequestFastPiece(this);
                            }
                        }
                        
                        waitingFor  = 4;
                        curStep     = 4;
                        RunNext();

                        break;
                }
            } catch (Exception e)
            {
                if (Beggar.Options.Verbosity > 0) Log(1, "[ERROR] " + e.Message + "\n" + e.StackTrace);
                Dispose();
            }
        }
        private void ProcessMessage(int msgLen)
        {
            if (msgLen == 0) { if (Beggar.Options.Verbosity > 0) Log(4, "[MSG ] Keep Alive"); return; }

            Receive(1); // MSG Id

            switch (recvBuff[0])
            {
                                        // Core Messages | http://bittorrent.org/beps/bep_0052.html
                case Messages.REQUEST:
                    if (Beggar.Options.Verbosity > 0) Log(4, "[MSG ] Request");
                    // TODO

                    break;

                case Messages.CHOKE:
                    if (Beggar.Options.Verbosity > 0) Log(2, "[MSG ] Choke");

                    stageYou.unchoked = false;

                    if (lastPieces.Count > 0)
                    {
                        if (Beggar.focusArea != null || Beggar.lastFocusArea != null)
                        {
                            Beggar.ResetRequests(this, lastPieces);
                            //piecesRequested = 0; // We should wait for rejects otherwise we loose sync between send/recv pieces (really bad for streaming and FAs)
                            lastPieces = new List<Tuple<int, int, int>>();
                        }
                    }

                    status = Status.READY;

                    return;
                case Messages.UNCHOKE:
                    if (Beggar.Options.Verbosity > 0) Log(2, "[MSG ] Unchoke");

                    stageYou.unchoked = true;

                    return;
                case Messages.INTRESTED:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Intrested");

                    stageYou.intrested = true;

                    break;
                case Messages.HAVE:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Have");
                    Receive(msgLen - 1);

                    stageYou.haveNone = false;

                    if (stageYou.bitfield == null)
                    {
                        if (Beggar.torrent.data.pieces != 0)
                            stageYou.bitfield = new Bitfield(Beggar.torrent.data.pieces);
                        else
                            stageYou.bitfield = new Bitfield(15000); // MAX PIECES GUESS?
                    }

                    int havePiece = Utils.ToBigEndian(recvBuff);
                    stageYou.bitfield.SetBit(havePiece);

                    return;
                case Messages.BITFIELD:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Bitfield");

                    Receive(msgLen - 1);

                    stageYou.haveNone   = false;
                    byte[] bitfield     = new byte[recvBuff.Length];
                    Buffer.BlockCopy(recvBuff, 0, bitfield, 0, recvBuff.Length);
                    stageYou.bitfield   = new Bitfield(bitfield, Beggar.torrent.data.pieces != 0 ? Beggar.torrent.data.pieces : recvBuff.Length * 8);

                    return;
                case Messages.PIECE:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Piece");

                    status = Status.DOWNLOADING; // Bug was noticed Downloading peer was in READY and couldn't get out with timeout

                    Receive(4);         // [Piece Id]
                    int piece   = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Offset]
                    int offset  = Utils.ToBigEndian(recvBuff);
                    Receive(msgLen - 9);// [Data]

                    int lastPiece = FindPiece(piece, offset);
                    if (lastPiece != -1) lastPieces.RemoveAt(lastPiece);

                    PieceRejects = 0;

                    if (piecesRequested > 0) piecesRequested--;

                    Beggar.PieceReceived(msgLen - 9 == MAX_DATA_SIZE ? recvBuffMax : recvBuff, piece, offset, this);

                    lastDownloadAt       = DateTime.UtcNow.Ticks;
                    totalWaitDuration   += lastDownloadAt - blockRequestedAt;
                    totalBytesReceived  += msgLen - 9;

                    if (piecesRequested == 0)
                        status = Status.READY;
                    else
                        blockRequestedAt = lastDownloadAt;

                    return;
                                        // DHT Extension        | http://bittorrent.org/beps/bep_0005.html | reserved[7] |= 0x01 | UDP Port for DHT 
                case Messages.PORT:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Port");

                    // TODO: Add them in DHT as a 3rd Strategy?

                    break;
                                        // Fast Extensions      | http://bittorrent.org/beps/bep_0006.html | reserved[7] |= 0x04
                case Messages.REJECT_REQUEST:// Reject Request
                    if (Beggar.Options.Verbosity > 0) Log(2, "[MSG ] Reject Request");

                    Receive(4);         // [Piece Id]
                    piece   = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Offset]
                    offset  = Utils.ToBigEndian(recvBuff);
                    Receive(4);         // [Length]
                    int len = Utils.ToBigEndian(recvBuff);

                    if (piecesRequested > 0) piecesRequested--;

                    Interlocked.Increment(ref Beggar.Stats.Rejects);

                    lastPiece = FindPiece(piece, offset);
                    if (lastPiece != -1)
                    {
                        Beggar.ResetRequest(this, piece, offset, len);
                        lastPieces.RemoveAt(lastPiece);
                    }

                    // Resets to 0 for each PiecesBlock Success
                    PieceRejects++;

                    if (PieceRejects >= Beggar.Options.BlockRequests * 3)
                    {
                        Log(4, $"[DROP] Too many Rejects");
                        Dispose();
                        return;
                    }
                        
                    if (piecesRequested == 0)
                        status = Status.READY;

                    return;
                case Messages.HAVE_NONE:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Have None");
                    stageYou.haveNone = true;

                    return;
                case Messages.HAVE_ALL:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Have All");
                    stageYou.haveAll = true;

                    return;
                case Messages.SUGGEST_PIECE:
                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Suggest Piece");
                    // TODO

                    break;
                case Messages.ALLOW_FAST:
                    Receive(4);         // [Piece Id]
                    int allowFastPiece = Utils.ToBigEndian(recvBuff);
                    if (allowFastPiece < 0) return;

                    allowFastPieces.Add(allowFastPiece);

                    if (Beggar.Options.Verbosity > 0) Log(3, $"[MSG ] Allowed Fast [Piece: {allowFastPiece}]");

                    return;

                                        // Extension Protocol   | http://bittorrent.org/beps/bep_0010.html | reserved_byte[5] & 0x10 | LTEP (Libtorrent Extension Protocol)
                case Messages.EXTENDED:
                    Receive(1); // MSG Extension Id

                    if (Beggar.Options.Verbosity > 0) Log(3, "[MSG ] Extended ...");

                    if (recvBuff[0] == Messages.EXTENDED_HANDSHAKE)
                    {
                        if (Beggar.Options.Verbosity > 0) Log(3, "[HAND] Extended Received");

                        Receive(msgLen - 2);

                        // BEncode Dictionary [Currently fills stageYou.extensions.ut_metadata]
                        BDictionary extDic  = bParser.Parse<BDictionary>(recvBuff);
                        object cur          = Utils.GetFromBDic(extDic, new string[] {"m", "LT_metadata"});
                        if (cur != null)    stageYou.extensions.ut_metadata = (byte) ((int) cur);
                        cur                 = Utils.GetFromBDic(extDic, new string[] {"m", "ut_metadata"});
                        if (cur != null)    stageYou.extensions.ut_metadata = (byte) ((int) cur);
                        //cur                 = Utils.GetFromBDic(extDic, new string[] {"m", "ut_pex"});
                        //if (cur != null)    stageYou.extensions.ut_pex      = (byte) ((int) cur);


                        cur                 = Utils.GetFromBDic(extDic, new string[] {"v"});
                        if (cur != null)    stageYou.version = cur.ToString();

                        // MSG Extended Handshake | Reply
                        SendExtendedHandshake();

                        return;
                    }

                                        // Peer Exchange (PEX)  | http://bittorrent.org/beps/bep_0011.html
                    else if (recvBuff[0] == Messages.EXT_UT_PEX)
                    {
                        /* TODO:
                         * 
                         * By adding IPv6 we loose uniquness by host on peers, we should change the uniquness based on remotePeerId (currently possible we connect to the same IPv4 / IPv6 peer ?) - We can also get ipv6 from Extended message (->ipv6)
                         * Possible process also dropped to remove peers from main storage (or even "ban" them to avoid re-push them in the queue)
                         */

                        if (Beggar.Options.Verbosity > 0) Log(3, "[PEX] ...");

                        Receive(msgLen - 2);

                        byte[] buff = msgLen - 2 == MAX_DATA_SIZE ? recvBuffMax : recvBuff;

                        BDictionary extDic              = bParser.Parse<BDictionary>(recvBuff);
                        byte[] buffAdded                = new byte[0];
                        Dictionary<string, int> peers   = new Dictionary<string, int>();

                        if (extDic.ContainsKey("added")) buffAdded = ((BString)extDic["added"]).Value.ToArray();

                        for (int i = 0; i < buffAdded.Length / 6; i++)
                        {
                            System.Net.IPAddress curIP = new System.Net.IPAddress(Utils.ArraySub(ref buffAdded, (uint)i * 6, 4, false));
                            UInt16 curPort = (UInt16)BitConverter.ToInt16(Utils.ArraySub(ref buffAdded, (uint)4 + (i * 6), 2, true), 0);

                            if (curPort < 500) continue; // Drop fake / Avoid DDOS

                            peers[curIP.ToString()] = curPort;
                        }

                        buffAdded = new byte[0];
                        if (extDic.ContainsKey("added6")) buffAdded = ((BString) extDic["added6"]).Value.ToArray();

                        for (int i=0; i<buffAdded.Length / 18; i++)
                        {
                            System.Net.IPAddress curIP = new System.Net.IPAddress(Utils.ArraySub(ref buffAdded,(uint) i*18, 16, false));
                            UInt16 curPort  = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref buffAdded,(uint) 16 + (i*18), 2, true), 0);

                            if (curPort < 500) continue; // Drop fake / Avoid DDOS

                            peers[curIP.ToString()] = curPort;
                        }

                        if (peers.Count > 0) Beggar.FillPeers(peers, BitSwarm.PeersStorage.PEX);

                        //if (Beggar.Options.Verbosity > 0) Log(3, $"[PEX] {peers.Count}");

                        return;
                    }


                    // Extension for Peers to Send Metadata Files | info-dictionary part of the .torrent file | http://bittorrent.org/beps/bep_0009.html
                    else if (recvBuff[0] == Messages.EXT_UT_METADATA)
                    {
                        // MSG Extended Metadata
                        if (Beggar.Options.Verbosity > 0) Log(3, "[META] ...");

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
                            case Messages.METADATA_RESPONSE: // (Expecting 0x4000 | 16384 bytes - except if last piece)
                                if (Beggar.Options.Verbosity > 0) Log(2, "[META] Received");
                                Beggar.MetadataPieceReceived(buffSize == MAX_DATA_SIZE ? recvBuffMax : recvBuff, (int) mdHeadersDic.Get<BNumber>("piece").Value, mdHeadersDic.EncodeAsString().Length, (int) mdHeadersDic.Get<BNumber>("total_size").Value, this);

                                stageYou.metadataPiecesRequested--;


                                break;

                            case Messages.METADATA_REJECT:
                                if (Beggar.Options.Verbosity > 0) Log(2, "[META] Rejected");
                                Beggar.MetadataPieceRejected((int) mdHeadersDic.Get<BNumber>("piece").Value, host);

                                stageYou.metadataPiecesRequested--;

                                break;

                            case Messages.METADATA_REQUEST:
                                if (Beggar.Options.Verbosity > 0) Log(3, "[META] Request");
                                break;

                            default:
                                if (Beggar.Options.Verbosity > 0) Log(4, "[META] Unknown " + mdHeadersDic.Get<BNumber>("msg_type").Value);
                                break;

                        } // Switch Metadata (msg_type)

                        if (!wasDownloading || piecesRequested < 1) // In case of late response of Metadata when Metadata already done and we already requested pieces
                            status = Status.READY;
                        return; 
                    }
                    else
                    {
                        if (Beggar.Options.Verbosity > 0) Log(4, "[MSG ] Extended Unknown " + recvBuff[0]);
                    }

                    Receive(msgLen - 2);

                    return; // Case Messages.EXTENDED    

                default:
                    if (Beggar.Options.Verbosity > 0) Log(4, "[MSG ] Message Unknown " + recvBuff[0]);

                    break;
            } // Switch (MSG Id)

            Receive(msgLen - 1); // Ensure Len > 0
        }
        private void Receive(int len)
        {
            int read;
            if (len == MAX_DATA_SIZE)
                read = tcpStream.Read(recvBuffMax, 0, len);
            else
                { recvBuff = new byte[len]; read = tcpStream.Read(recvBuff, 0, len); }

            if (read != len)
                Log(1, $"[ERROR] Read {read} < {len}");
        }
        private void SendExtendedHandshake()
        {
            try
            {
                if (Beggar.Options.Verbosity > 0) Log(3, "[HAND] Extended Sending");

                sendBuff = Utils.ArrayMerge(PrepareMessage(0, true, EXT_BDIC), PrepareMessage(0xf, false, null), PrepareMessage(0x2, false, null)); // EXTDIC, HAVE NONE, INTRESTED
                tcpStream.Write(sendBuff, 0, sendBuff.Length);

                //tcpClient.SendBufferSize    = 1500;
                tcpClient.ReceiveBufferSize = MAX_DATA_SIZE * 4;
                recvBuffMax                 = new byte[MAX_DATA_SIZE];
                status                      = Status.READY;
            }
            catch (Exception e)
            {
                if (Beggar.Options.Verbosity > 0) Log(1, "[HAND] Extended Sending Error " + e.Message);
                Dispose();
            }
        }
        public void Dispose()
        {
            try
            {
                if (lastPieces != null && lastPieces.Count > 0 /*&& Beggar.isRunning*/) Beggar.ResetRequests(this, lastPieces);

                status      = Status.FAILED;
                recvBuff    = null;
                sendBuff    = null;
                //stageYou    = null; // Currently Not Synch with Requests will drop null references
                
                tcpStream?.Close();
                tcpClient?.Close();
                tcpStream = null;
                tcpClient = null;
            } catch (Exception) { status = Status.FAILED; }
        }
        #endregion

        #region Outgoing Messages | Requests
        public void RequestMetadata(int piece, int piece2 = -1)
        {
            try
            {
                stageYou.metadataPiecesRequested++;
                if (piece2 != -1) stageYou.metadataPiecesRequested++;

                sendBuff = PrepareMessage(stageYou.extensions.ut_metadata, true, Encoding.UTF8.GetBytes((new BDictionary { { "msg_type", 0 }, { "piece", piece } }).EncodeAsString()));
                if (piece2 != -1)
                    sendBuff = Utils.ArrayMerge(sendBuff, PrepareMessage(stageYou.extensions.ut_metadata, true, Encoding.UTF8.GetBytes((new BDictionary { { "msg_type", 0 }, { "piece", piece2 } }).EncodeAsString())));

                tcpStream.Write(sendBuff, 0, sendBuff.Length);
            } catch (Exception e)
            {
                if (Beggar.Options.Verbosity > 0) Log(1, $"[REQ][METADATA] {piece},{piece2} {e.Message}");
                Dispose();
            }
        }
        public void RequestPiece(List<Tuple<int, int, int>> pieces) // piece, offset, len
        {
            try
            {
                lock (lockerRequests)
                {
                    if (tcpClient == null || !tcpClient.Connected) { Dispose(); return; }

                    status          = Status.DOWNLOADING;
                    sendBuff        = new byte[0];
                    lastPieces      = pieces;
                    piecesRequested+= pieces.Count;
                    blockRequestedAt= DateTime.UtcNow.Ticks;

                    foreach (Tuple<int, int, int> piece in pieces)
                        sendBuff = Utils.ArrayMerge(sendBuff, PrepareMessage(Messages.REQUEST, false, Utils.ArrayMerge(Utils.ToBigEndian((Int32) piece.Item1), Utils.ToBigEndian((Int32) piece.Item2), Utils.ToBigEndian((Int32) piece.Item3))));

                    tcpStream.Write(sendBuff, 0, sendBuff.Length);
                }
            }
            catch (Exception e)
            {
                if (Beggar.Options.Verbosity > 0) Log(1, $"[REQ ] Send Failed - {e.Message}\r\n{e.StackTrace}");
                Dispose();
            }
        }
        #endregion


        #region Currently Disabled
        public void SendKeepAlive()
        {
            try
            {
                if (Beggar.Options.Verbosity > 0) Log(4, "[MSG ] Sending Keep Alive");

                tcpStream.Write(new byte[] { 0, 0, 0, 0}, 0, 4);
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
            } catch (Exception e)
            {
                Log(1, $"[REQ ][PIECE][BLOCK] {piece}\t{offset}\t{len} {e.Message}\r\n{e.StackTrace}");
                Dispose();
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
                    {
                        if (Beggar.Options.Verbosity > 0) Log(4, $"[REQC] [P]\tPiece: {piece.Item1} Block: {piece.Item2 / Beggar.torrent.data.blockSize} Offset: {piece.Item2} Size: {piece.Item3} Requests: {piecesRequested}");
                        sendBuff = Utils.ArrayMerge(sendBuff, PrepareMessage(Messages.CANCEL, false, Utils.ArrayMerge(Utils.ToBigEndian((Int32)piece.Item1), Utils.ToBigEndian((Int32)piece.Item2), Utils.ToBigEndian((Int32)piece.Item3))));
                    }

                    tcpStream.Write(sendBuff, 0, sendBuff.Length);
                }
            }
            catch (Exception e)
            {
                if (Beggar.Options.Verbosity > 0) Log(1, $"[REQ ] Send Cancel Failed - {e.Message}\r\n{e.StackTrace}");
                Dispose();
            }
        }
        public void CancelPieces(int piece, int offset, int len)
        {
            try
            {
                lock (lockerRequests)
                {
                    if (Beggar.Options.Verbosity > 0) Log(2, $"[CANCELING PIECE] Piece: {piece} Offset: {offset}");

                    sendBuff = new byte[0];
                    Utils.ArrayMerge(sendBuff, PrepareMessage(Messages.CANCEL, false, Utils.ArrayMerge(Utils.ToBigEndian((Int32) piece), Utils.ToBigEndian((Int32) offset), Utils.ToBigEndian((Int32) len))));

                    tcpStream.Write(sendBuff, 0, sendBuff.Length);

                }
            }
            catch (Exception e)
            {
                if (Beggar.Options.Verbosity > 0) Log(1, $"[REQ ] Send Cancel Failed - {e.Message}\r\n{e.StackTrace}");
                Dispose();
            }
        }
        #endregion

        #region Misc
        public long GetDownRate() // Bytes (Per Second)
        {
            if (totalBytesReceived == 0) return 0;

            return (long) (((double)totalBytesReceived / (double)totalWaitDuration) * 10000000);
        }
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

            } catch (Exception e)
            {
                if (Beggar.Options.Verbosity > 0) Log(1, "[SENDMESSAGE] Sending Error " + e.Message);
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

        private int FindPiece(int piece, int offset)
        {
            for (int i=0; i<lastPieces.Count; i++)
                if (lastPieces[i].Item1 == piece && lastPieces[i].Item2 == offset) return i;

            return -1;
        }

        internal void Log(int level, string msg) { if (Beggar.Options.Verbosity > 0 && Beggar.Options.LogPeer && level <= Beggar.Options.Verbosity) Beggar.log.Write($"[Peer    ] [{host.PadRight(15, ' ')}] {msg}"); }
        #endregion
    }
}