using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Collections.Generic;

using static SuRGeoNix.TorSwarm.Torrent.TorrentData;
using static SuRGeoNix.TorSwarm.Torrent.MetaData;

namespace SuRGeoNix.TorSwarm
{
    public class TorSwarm
    {
        // ====================== PUBLIC ======================
        public bool             isRunning                   { get { return status == Status.RUNNING; } }
        public bool             isPaused                    { get { return status == Status.PAUSED; } }
        public bool             isStopped                   { get { return status == Status.STOPPED; } }

        public OptionsStruct    Options;
        public StatsStructure   Stats;

        public struct OptionsStruct
        {
            public string   DownloadPath        { get; set; }
            public string   TempPath            { get; set; }

            public int      MaxConnections      { get; set; }
            public int      MinThreads          { get; set; }
            public int      MaxThreads          { get; set; }
            public int      PeersFromTracker    { get; set; }

            public int      DownloadLimit       { get; set; }
            public int      UploadLimit         { get; set; }

            public int      ConnectionTimeout   { get; set; }
            public int      HandshakeTimeout    { get; set; }
            public int      MetadataTimeout     { get; set; }
            public int      PieceTimeout        { get; set; }

            public int      Verbosity           { get; set; }   // 1 -> TorSwarm, 2 -> Peers | Trackers

            public Action<StatsStructure>       StatsCallback       { get; set; }
            public Action<Torrent>              TorrentCallback     { get; set; }
            public Action<int, string>          StatusCallback      { get; set; }
        }
        public struct StatsStructure
        {
            //public long     StartedOn           { get; set; }
            public long     FinishedOn          { get; set; }

            public int      DownRate            { get; set; }
            public int      AvgRate             { get; set; }
            public int      MaxRate             { get; set; }

            public int      AvgETA              { get; set; }
            public int      ETA                 { get; set; }

            public long     BytesDownloaded     { get; set; }
            public long     BytesDownloadedPrev { get; set; }
            public long     BytesUploaded       { get; set; }
            public long     BytesDropped        { get; set; }

            public int      PeersTotal          { get; set; }
            public int      PeersInQueue        { get; set; }
            public int      PeersConnecting     { get; set; }
            public int      PeersConnected      { get; set; }
            public int      PeersFailed1        { get; set; }
            public int      PeersFailed2        { get; set; }
            public int      PeersFailed         { get; set; }
            public int      PeersChoked         { get; set; }
            public int      PeersUnChoked       { get; set; }
            public int      PeersDownloading    { get; set; }
            public int      PeersDropped        { get; set; }
        }

        // ====================== PRIVATE =====================
        // Lockers
        private static readonly object  lockerTorrent       = new object();
        private static readonly object  lockerMetadata      = new object();
        private static readonly object  lockerPeers         = new object();

        // Generators (Hash / Random)
        public  static SHA1             sha1                = new SHA1Managed();
        private static Random           rnd                 = new Random();
        private byte[]                  peerID;

        // Main [Torrent / Trackers / Peers / Options]
        private Torrent                 torrent;
        private List<Tracker>           trackers;
        private Tracker.Options         trackerOpt;
        private List<Peer>              peers;
        private Peer.Options            peerOpt;

        private Logger                  log;
        private Thread                  beggar;
        private Status                  status;

        // More Stats
        private int                     openConnections     = 0;
        private long                    curSecondTicks      = 0;
        private int                     curSeconds          = 0;
        private int                     pieceTimeouts       = 0;
        private int                     pieceRejected       = 0;
        private int                     pieceAlreadyRecv    = 0;
        private int                     sha1Fails           = 0;
        
        private enum Status
        {
            RUNNING     = 0,
            PAUSED      = 1,
            STOPPED     = 2
        }

        // ================= PUBLIC METHODS ===================

        // Constructors
        public TorSwarm(string torrent, OptionsStruct? opt = null)
        { 
            Options         = (opt == null) ? GetDefaultsOptions() : (OptionsStruct) opt;
            Initiliaze();
            this.torrent.FillFromTorrentFile(torrent);
            Setup();
        }
        public TorSwarm(Uri magnetLink, OptionsStruct? opt = null)
        {
            Options         = (opt == null) ? GetDefaultsOptions() : (OptionsStruct) opt;
            Initiliaze();
            torrent.FillFromMagnetLink(magnetLink);
            Setup();
        }

        // Initializers / Setup
        private void Initiliaze()
        {
            peerID                          = new byte[20]; rnd.NextBytes(peerID);

            trackers                        = new List<Tracker>();
            peers                           = new List<Peer>();

            torrent                         = new Torrent(Options.DownloadPath);
            torrent.metadata.progress       = new BitField(2);  // For 1st & 2nd piece
            torrent.metadata.requests       = new BitField(2);  // For 1st & 2nd piece
            torrent.metadata.firstPieceTries= 5;                // Avoid spamming all peers for 1st & 2nd piece

            log                             = new Logger(Path.Combine(Options.DownloadPath, "session" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"), true);

            status                          = Status.STOPPED;
        }
        private void Setup()
        {
            trackerOpt                      = new Tracker.Options();
            trackerOpt.peerID               = peerID;
            trackerOpt.hash                 = torrent.file.infoHash;
            trackerOpt.type                 = Tracker.TYPE.UDP;
            trackerOpt.readTimeout          = Options.HandshakeTimeout;
            trackerOpt.log                  = log;
            trackerOpt.verbosity            = Options.Verbosity;

            peerOpt                         = new Peer.Options();
            peerOpt.PeerID                  = peerID;
            peerOpt.Hash                    = torrent.file.infoHash;
            peerOpt.ConnectionTimeout       = Options.ConnectionTimeout;
            peerOpt.HandshakeTimeout        = Options.HandshakeTimeout;
            peerOpt.PieceTimeout            = Options.PieceTimeout;
            peerOpt.LogFile                 = log;
            peerOpt.Verbosity               = Options.Verbosity;
            peerOpt.MetadataReceivedClbk    = MetadataReceived;
            peerOpt.MetadataRejectedClbk    = MetadataRejected;
            peerOpt.PieceReceivedClbk       = PieceReceived;
            peerOpt.PieceRejectedClbk       = PieceRejected;

            ThreadPool.SetMinThreads        (Options.MinThreads, Options.MinThreads);

            FillTrackersFromTorrent();

            if (  (torrent.file.length > 0      || (torrent.file.lengths != null && torrent.file.lengths.Count > 0))  
                && torrent.file.pieceLength > 0 && (torrent.file.pieces  != null && torrent.file.pieces.Count  > 0))
                { torrent.metadata.isDone = true;  Options.TorrentCallback?.BeginInvoke(torrent, null, null); }
        }
        public static OptionsStruct GetDefaultsOptions()
        {
            OptionsStruct opt       = new OptionsStruct();

            opt.DownloadPath        = Path.GetTempPath();
            opt.TempPath            = Path.GetTempPath();

            opt.MaxConnections      = 40;
            opt.MinThreads          = 150;
            opt.MaxThreads          = Timeout.Infinite;
            opt.PeersFromTracker    = 50;

            opt.DownloadLimit       = Timeout.Infinite;
            opt.UploadLimit         = Timeout.Infinite;

            opt.ConnectionTimeout   = 3000;
            opt.HandshakeTimeout    = 3000;
            opt.MetadataTimeout     = 1300;
            opt.PieceTimeout        = 6000;

            opt.Verbosity           = 1;

            return opt;
        }

        // Start / Pause / Stop
        public void Start()
        {
            if ( status == Status.RUNNING ) return;

            status = Status.RUNNING;

            beggar = new Thread(() =>
            {
                PeerBeggar();
                Beggar();

                FillStats();
                if ( torrent.data.isDone )
                    Options.StatusCallback?.BeginInvoke(0, "", null, null);
                else
                    Options.StatusCallback?.BeginInvoke(1, "", null, null);
            });

            beggar.SetApartmentState(ApartmentState.STA);
            beggar.Priority = ThreadPriority.AboveNormal;
            beggar.Start();
        }
        public void Pause()
        {
            if ( status == Status.PAUSED ) return;

            status = Status.PAUSED;
        }
        public void Stop()
        {
            if ( status == Status.STOPPED ) return;

            status = Status.STOPPED;
            Thread.Sleep(800);
            
            if ( beggar != null ) beggar.Abort();
        }

        // ================ PRIVATE METHODS =================

        // Feeders              [Torrent -> Trackers | Trackers -> Peers | Client -> Stats]
        private void FillTrackersFromTorrent()
        {
            foreach (Uri uri in torrent.file.trackers)
            {
                // Try UDP anyways
                if ( true ) //uri.Scheme.ToLower() == "udp")
                {
                    Log($"[Torrent] [Tracker] [ADD] {uri.ToString().ToLower()}://{uri.DnsSafeHost}:{uri.Port}");
                    trackers.Add(new Tracker(uri.DnsSafeHost, uri.Port, trackerOpt));
                }
                else
                    Log($"[Torrent] [Tracker] [ADD] {uri} Protocol not implemented");
            }
        }
        private void FillPeersFromTracker(int pos)
        {
            if ( !trackers[pos].Announce(torrent.file.infoHash, Options.PeersFromTracker) ) { trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port} Failed"); return; }
            if ( trackers[pos].peers == null) {trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] No peers"); return; }

            lock ( lockerPeers )
            {
                trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] [BEFORE] Adding {trackers[pos].peers.Count} in Peers {peers.Count}");
                foreach (Peer peer in peers)
                    if ( trackers[pos].peers.ContainsKey(peer.host) ) { trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] Peer {peer.host} already exists"); trackers[pos].peers.Remove(peer.host); }

                foreach (KeyValuePair<string, int> peerKV in trackers[pos].peers)
                    peers.Add(new Peer(peerKV.Key, peerKV.Value, peerOpt));

                trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] [AFTER] Peers {peers.Count}");
            }
        }
        private void FillStats()
        {
            List<string> cleanPeers = new List<string>();

            Stats.PeersInQueue      = 0;
            Stats.PeersChoked       = 0;
            Stats.PeersConnected    = 0;
            Stats.PeersConnecting   = 0;
            Stats.PeersDownloading  = 0;
            Stats.PeersDropped      = 0;
            Stats.PeersFailed1      = 0;
            Stats.PeersFailed2      = 0;
            Stats.PeersUnChoked     = 0;
                            
            foreach (Peer peer in peers)
            {
                switch ( peer.status )
                {
                    case Peer.Status.NEW:
                        Stats.PeersInQueue++;

                        break;
                    case Peer.Status.CONNECTING:
                    case Peer.Status.CONNECTED:
                        Stats.PeersConnecting++;

                        break;
                    case Peer.Status.FAILED1:
                        Stats.PeersFailed1++;
                        cleanPeers.Add(peer.host);

                        break;
                    case Peer.Status.FAILED2:
                        Stats.PeersFailed2++;
                        cleanPeers.Add(peer.host);

                        break;
                    case Peer.Status.READY:
                        Stats.PeersConnected++;
                        if ( peer.stageYou.unchoked ) 
                            Stats.PeersUnChoked++;
                        else
                            Stats.PeersChoked++;

                        break;
                    case Peer.Status.DOWNLOADING:
                        Stats.PeersDownloading++;

                        break;
                }
            }

            // Remove FAILED Peers (NOTE: Trackers possible will add them back! Maybe ban them. Also make sure Dispose them!)
            for (int i = peers.Count - 1; i >= 0; i--)
            {
                for (int k = 0; k < cleanPeers.Count; k++)
                    if (peers[i].host == cleanPeers[k]) { cleanPeers.RemoveAt(k); peers[i].Disconnect(); peers.RemoveAt(i); Stats.PeersDropped++; }
            }

            long totalBytesDownloaded = Stats.BytesDownloaded + Stats.BytesDropped;
            Stats.DownRate      = (int) (totalBytesDownloaded - Stats.BytesDownloadedPrev) / 2; // Change this (2 seconds) if you change scheduler
            Stats.AvgRate       = (int) (totalBytesDownloaded / curSeconds);
            
            if ( torrent.data.totalSize - Stats.BytesDownloaded == 0 )
            {
                Stats.ETA       = 0;
                Stats.AvgETA    = 0;
            } 
            else
            {
                if ( Stats.BytesDownloaded - Stats.BytesDownloadedPrev == 0 )
                    Stats.ETA  *= 2; // Kind of infinite | int overflow nice
                else 
                    Stats.ETA   = (int) ( (torrent.data.totalSize - Stats.BytesDownloaded) / ((Stats.BytesDownloaded - Stats.BytesDownloadedPrev) / 2) );

                if ( Stats.BytesDownloaded  == 0 )
                    Stats.AvgETA*= 2; // Kind of infinite
                else
                    Stats.AvgETA = (int) ( (torrent.data.totalSize - Stats.BytesDownloaded) / (Stats.BytesDownloaded / curSeconds ) );
            }
                                
            if ( Stats.DownRate > Stats.MaxRate ) Stats.MaxRate = Stats.DownRate;

            Stats.BytesDownloadedPrev   = totalBytesDownloaded;
            Stats.PeersTotal            = peers.Count;

            // Stats -> UI
            Options.StatsCallback?.BeginInvoke(Stats, null, null);

            // Stats -> Log
            Log($"[STATS] [INQUEUE: {String.Format("{0,3}",Stats.PeersInQueue)}]\t[DROPPED: {String.Format("{0,3}",Stats.PeersDropped)}]\t[CONNECTS: {String.Format("{0,3}",openConnections)}]\t[CONNECTING: {String.Format("{0,3}",Stats.PeersConnecting)}]\t[FAIL1: {String.Format("{0,3}",Stats.PeersFailed1)}]\t[FAIL2: {String.Format("{0,3}",Stats.PeersFailed2)}]\t[READY: {String.Format("{0,3}",Stats.PeersConnected)}]\t[CHOKED: {String.Format("{0,3}",Stats.PeersChoked)}]\t[UNCHOKED: {String.Format("{0,3}",Stats.PeersUnChoked)}]\t[DOWNLOADING: {String.Format("{0,3}",Stats.PeersDownloading)}]");
            Log($"[STATS] [CUR MAX: {String.Format("{0:n0}", (Stats.MaxRate / 1024)) + " KB/s"}]\t[DOWN CUR: {String.Format("{0:n0}", (Stats.DownRate / 1024)) + " KB/s"}]\t[DOWN AVG: {String.Format("{0:n0}", (Stats.AvgRate / 1024)) + " KB/s"}]\t[ETA CUR: {TimeSpan.FromSeconds(Stats.ETA).ToString(@"hh\:mm\:ss")}]\t[ETA AVG: {TimeSpan.FromSeconds(Stats.AvgETA).ToString(@"hh\:mm\:ss")}]\t[ETA R: {TimeSpan.FromSeconds((Stats.ETA + Stats.AvgETA)/2).ToString(@"hh\:mm\:ss")}]");
            Log($"[STATS] [TIMEOUTS: {String.Format("{0,4}",pieceTimeouts)}]\t[ALREADYRECV: {String.Format("{0,3}",pieceAlreadyRecv)}]\t[REJECTED: {String.Format("{0,3}",pieceRejected)}]\t[SHA1FAILS:{String.Format("{0,3}",sha1Fails)}]\t[DROPPED BYTES: {Utils.BytesToReadableString(Stats.BytesDropped)}]");
        }

        // Main Threads         [Trackers | Peers]
        private void PeerBeggar()
        {
            try
            {
                Log("[PEER-BEGGAR] " + status);

                for (int i=0; i<trackers.Count; i++)
                {
                    int noCache = i;
                    ThreadPool.QueueUserWorkItem(new WaitCallback(delegate(object state) { FillPeersFromTracker(noCache); }), null);
                }

            } catch (ThreadAbortException) {
            } catch (Exception e) { Log($"[PEER-BEGGAR] PeerBeggar(), Msg: {e.StackTrace}"); }
        }
        private void Beggar()
        {
            try
            {
                Utils.TimeBeginPeriod(1);
                
                Stats.MaxRate   = 0;
                curSeconds      = 0;
                log.RestartTime();

                Log("[BEGGAR  ] " + status);

                while ( status == Status.RUNNING )
                {
                    lock ( lockerPeers )
                    {
                        openConnections = 0;

                        // Request Piece | Count Connections
                        foreach (Peer peer in peers)
                        {
                            switch ( peer.status )
                            {
                                case Peer.Status.CONNECTING:
                                case Peer.Status.CONNECTED:
                                case Peer.Status.DOWNLOADING:
                                    openConnections++;

                                    break;    
                                case Peer.Status.READY:
                                    openConnections++;
                                    RequestPiece(peer);

                                    break;
                            }
                        }

                        // Request Piece | New Connections 
                        foreach (Peer peer in peers)
                        {
                            switch ( peer.status )
                            {
                                case Peer.Status.NEW:
                                    if ( openConnections < Options.MaxConnections )
                                    {
                                        openConnections++;
                                        //Log($" Connecting {peer.host}");
                                        peer.status = Peer.Status.CONNECTING;
                                        ThreadPool.QueueUserWorkItem(new WaitCallback(delegate(object state) { peer.Run(); }), null);
                                    }

                                    break;
                                case Peer.Status.READY:

                                    RequestPiece(peer);

                                    break;
                                case Peer.Status.DOWNLOADING:
                                    break;
                            }
                        }

                        // Scheduler
                        //if (DateTime.UtcNow.Ticks - curSecondTicks >= 10000 * 1000 - (10000 * 10))
                        if ( log.GetTime() - (curSeconds * 1000) > 990 )
                        {
                            curSecondTicks = DateTime.UtcNow.Ticks;
                            curSeconds++;

                            // Scheduler Every 2 Seconds    [Stats | Clean Failed Peers]
                            if ( curSeconds %  2 == 0 )
                            {
                                if ( torrent.metadata.isDone ) FillStats();
                            }

                            // Scheduler Every 5 Seconds    [Peers MSG Keep Alive]
                            if ( curSeconds %  5 == 0 )
                            {
                                //Log($"[SCHEDULER][5 SECS] Currents seconds {seconds}");

                                // Keep Alives / Interested
                                foreach (Peer peer in peers)
                                {
                                    switch ( peer.status )
                                    {
                                        case Peer.Status.READY:
                                            if ( (curSecondTicks - peer.lastAction) / 10000 > 3000 )
                                                peer.SendKeepAlive();

                                            break;
                                    }
                                }
                            }

                            // Scheduler Every 16 Seconds   [Peers MSG Intrested | Trackers Announce]
                            if ( curSeconds % 16 == 0 )
                            {
                                // Client-> Peers [MSG Intrested]
                                foreach (Peer peer in peers)
                                {
                                    switch ( peer.status )
                                    {
                                        case Peer.Status.READY:

                                            if ( !peer.stageYou.unchoked )
                                                peer.SendMessage(Peer.Messages.INTRESTED, false, null);

                                            break;
                                    }
                                }

                                // Client -> Trackers [Announce]
                                if ( Stats.PeersDownloading < 25 && Stats.PeersInQueue < 10 && Stats.PeersConnected + Stats.PeersConnecting < 15)
                                    PeerBeggar();
                            }

                            // Scheduler Every 1 Second     [Request Timeouts]

                            // Check Request Timeouts
                            if ( torrent.metadata.isDone )
                                CheckRequestTimeouts();
                            else
                                CheckRequestTimeoutsMetadata();

                        } // Scheduler [Every Second]

                    } // Lock Peers

                    Thread.Sleep(15);
                }

                Log("[BEGGAR  ] " + status);

                // Clean Up
                foreach (Peer peer in peers)
                    peer.Disconnect();

                torrent.Dispose();
                log.Dispose();

            } catch (ThreadAbortException) {
            } catch (Exception e) { Options.StatusCallback?.BeginInvoke(2, e.Message, null, null); Log($"[BEGGAR] Beggar(), Msg: {e.Message}\r\n{e.StackTrace}"); }

            Utils.TimeEndPeriod(1);
        }

        // Request Timeouts     [Metadata | Torrent]
        private void CheckRequestTimeoutsMetadata()
        {
            for (int i=torrent.metadata.metadataRequests.Count-1; i>=0; i--)
            {
                if ( curSecondTicks - torrent.metadata.metadataRequests[i].timestamp > Options.MetadataTimeout * 10000 )
                {
                    if ( torrent.metadata.metadataRequests[i].piece == 0 )
                    {
                        if ( torrent.metadata.totalSize == 0 ) {
                            Log($"[{torrent.metadata.metadataRequests[i].peer.host.PadRight(15, ' ')}] [REQT][M]\tPiece: {torrent.metadata.metadataRequests[i].piece} Size: {torrent.metadata.metadataRequests[i].size} Piece timeout");
                            torrent.metadata.firstPieceTries--;
                        }
                    } 
                    else
                    {
                        if ( !torrent.metadata.progress.GetBit(torrent.metadata.metadataRequests[i].piece) )
                        {
                            Log($"[{torrent.metadata.metadataRequests[i].peer.host.PadRight(15, ' ')}] [REQT][M]\tPiece: {torrent.metadata.metadataRequests[i].piece} Size: {torrent.metadata.metadataRequests[i].size} Piece timeout");
                            torrent.metadata.requests.UnSetBit(torrent.metadata.metadataRequests[i].piece);
                        }
                    }

                    torrent.metadata.metadataRequests.RemoveAt(i);
                }
            }
        }
        private void CheckRequestTimeouts()
        {
            lock ( lockerTorrent )
            {
                for (int i=torrent.data.pieceRequstes.Count-1; i>=0; i--)
                {
                    if ( curSecondTicks - torrent.data.pieceRequstes[i].timestamp > Options.PieceTimeout * 10000 )
                    {
                        // !(Piece Done | Block Done)
                        bool containsKey = torrent.data.pieceProgress.ContainsKey(torrent.data.pieceRequstes[i].piece);
                        if ( !(( !containsKey && torrent.data.progress.GetBit(torrent.data.pieceRequstes[i].piece) ) 
                                ||(  containsKey && torrent.data.pieceProgress[torrent.data.pieceRequstes[i].piece].progress.GetBit(torrent.data.pieceRequstes[i].block))) )
                        { 
                            Log($"[{torrent.data.pieceRequstes[i].peer.host.PadRight(15, ' ')}] [REQT][P]\tPiece: {torrent.data.pieceRequstes[i].piece} Size: {torrent.data.pieceRequstes[i].size} Block: {torrent.data.pieceRequstes[i].block} Offset: {torrent.data.pieceRequstes[i].block * torrent.data.blockSize} Piece timeout");
                            //if ( torrent.data.pieceRequstes[i].peer.status == Peer.Status.DOWNLOADING ) torrent.data.pieceRequstes[i].peer.status = Peer.Status.READY; // Not sure about that
                            if ( torrent.data.pieceRequstes[i].peer.status != Peer.Status.READY ) torrent.data.pieceRequstes[i].peer.status = Peer.Status.FAILED2; // Not sure about that
                            if ( containsKey ) torrent.data.pieceProgress[torrent.data.pieceRequstes[i].piece].requests.UnSetBit(torrent.data.pieceRequstes[i].block);

                            torrent.data.requests.UnSetBit(torrent.data.pieceRequstes[i].piece);
                            pieceTimeouts++;
                        }

                        torrent.data.pieceRequstes.RemoveAt(i);
                    }
                }
            }
        }

        // Main Implementation  [RequestPiece | SavePiece]
        private void RequestPiece(Peer peer)
        {
            int piece, block, pieceSize, blockSize;

            // Metadata Requests (Until Done)
            lock ( lockerMetadata )
            {
                if ( !torrent.metadata.isDone )
                {
                    if ( peer.stageYou.extensions.ut_metadata == 0 ) return;
                    if ( torrent.metadata.totalSize == 0 && torrent.metadata.firstPieceTries > 5 ) return;

                    int size = Peer.MAX_DATA_SIZE;

                    if ( torrent.metadata.totalSize != 0 )
                    {
                        piece = torrent.metadata.requests.GetFirst0();
                        if ( piece == -1 ) return;
                    
                        torrent.metadata.requests.SetBit(piece);
                    }
                    else
                    {
                        torrent.metadata.firstPieceTries++; 
                        //piece = rnd.Next(1, 100) / 55; // 55% -> 0 or 45% -> 1
                        piece = 0;    
                    }

                    peer.RequestMetadata(piece);
                    torrent.metadata.metadataRequests.Add( new MetadataRequest(DateTime.UtcNow.Ticks, peer, piece, size) );
                    Log($"[{peer.host.PadRight(15, ' ')}] [REQ ][M]\tPiece: {piece} Size: {size}");

                    return;
                }
            }

            // Torrent Requests
            lock ( lockerTorrent ) { 
                if ( !peer.stageYou.unchoked || peer.stageYou.haveNone || (!peer.stageYou.haveAll && peer.stageYou.bitfield == null) ) return;

                piece = torrent.data.requests.GetFirst0();
                if ( piece == -1 || (!peer.stageYou.haveAll && !peer.stageYou.bitfield.GetBit(piece)) ) return;

                if ( !torrent.data.pieceProgress.ContainsKey(piece) )
                {
                    block = 0;
                    PieceProgress pp = new PieceProgress(torrent.data, piece);
                    torrent.data.pieceProgress.Add(piece, pp);
                } else
                {
                    block = torrent.data.pieceProgress[piece].requests.GetFirst0();
                    if ( block == -1 ) { Log($"Shouldn't be here! Piece: {piece}"); return; }
                }
                
                torrent.data.pieceProgress[piece].requests.SetBit(block);
                if ( torrent.data.pieceProgress[piece].requests.GetFirst0() == -1 ) torrent.data.requests.SetBit(piece);

                if ( block != torrent.data.blocks - 1 )
                    blockSize = torrent.data.blockSize;
                else
                    blockSize = torrent.data.blockLastSize;

                if ( piece == torrent.data.pieces - 1 ) // Last Piece (need to check also in case of last block size?)
                {
                    pieceSize = torrent.data.totalSize % torrent.data.pieceSize != 0 ?  (int) (torrent.data.totalSize % torrent.data.pieceSize) : torrent.data.pieceSize;

                    if ( block == pieceSize / torrent.data.blockSize )
                        blockSize = pieceSize % torrent.data.blockSize;
                }

                Log($"[{peer.host.PadRight(15, ' ')}] [REQ ][P]\tPiece: {piece} Size: {blockSize} Block: {block} Offset: {block * torrent.data.blockSize}");

                peer.RequestPiece(piece, block * torrent.data.blockSize, blockSize);
                torrent.data.pieceRequstes.Add( new Torrent.TorrentData.PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize) );
            }
        }
        private void SavePiece(byte[] data, int piece, int size)
        {
            if ( size > torrent.file.pieceLength ) { Log("[SAVE] pieceSize > chunkSize"); return; }

            long firstByte  = piece * torrent.file.pieceLength;
            long ends       = firstByte + size;
            long sizeLeft   = size;
            int writePos    = 0;
            long curSize    = 0;

            for (int i=0; i<torrent.file.lengths.Count; i++)
            {
                curSize += torrent.file.lengths[i];
                if ( firstByte < curSize ) {
		            int writeSize 	= (int) Math.Min(sizeLeft, curSize - firstByte);
                    int chunkId     = (int) (((firstByte + torrent.file.pieceLength - 1) - (curSize - torrent.file.lengths[i]))/torrent.file.pieceLength);

		            if ( firstByte == curSize - torrent.file.lengths[i] )
                    {
                        Log("[SAVE] F file:\t\t" + (i + 1) + ", chunkId:\t\t" +     0 +     ", pos:\t\t" + writePos + ", len:\t\t" + writeSize);
                        torrent.data.files[i].WriteFirst(data, writePos, writeSize);
                    } else if ( ends < curSize  ) {
                        Log("[SAVE] M file:\t\t" + (i + 1) + ", chunkId:\t\t" + chunkId +   ", pos:\t\t" + writePos + ", len:\t\t" + torrent.file.pieceLength + " | " + writeSize);
                        torrent.data.files[i].Write(chunkId,data);
                    } else if ( ends >= curSize ) {
                        Log("[SAVE] L file:\t\t" + (i + 1) + ", chunkId:\t\t" + chunkId +   ", pos:\t\t" + writePos + ", len:\t\t" + writeSize);
                        torrent.data.files[i].WriteLast(chunkId, data, writePos, writeSize);
                    }

                    if ( ends - 1 < curSize ) break;

		            firstByte = curSize;
                    sizeLeft -= writeSize;
                    writePos += writeSize;
                }
            }
        }

        // Peer Callbacks       [Meta Data]
        private void MetadataReceived(byte[] data, int piece, int offset, int totalSize, Peer peer)
        {
            Log($"[{peer.host.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Offset: {offset} Size: {totalSize}");

            lock ( lockerMetadata )
            {
                if ( torrent.metadata.totalSize == 0 )
                {
                    torrent.metadata.firstPieceTries    = 100;

                    torrent.metadata.totalSize  = totalSize;
                    torrent.metadata.progress   = new BitField((totalSize/Peer.MAX_DATA_SIZE) + 1);
                    torrent.metadata.requests   = new BitField((totalSize/Peer.MAX_DATA_SIZE) + 1);
                    torrent.metadata.file       = new PartFile(Utils.FindNextAvailableFile(Path.Combine(Options.DownloadPath, string.Join("_", torrent.file.name.Split(Path.GetInvalidFileNameChars())) + ".torrent")), Peer.MAX_DATA_SIZE);
                    torrent.metadata.requests.SetBit(piece);
                }

                if (torrent.metadata.file.chunksCounter + 1 < torrent.metadata.progress.size - 1)
                {
                    Log($"[{peer.host.PadRight(15, ' ')}] [REQ ][M]\tPiece: {piece} Fast request");
                    RequestPiece(peer);
                }

                if ( torrent.metadata.progress.GetBit(piece) ) { Log($"[{peer.host.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Already received"); return; }

                if ( piece == torrent.metadata.progress.size - 1 ) 
                {
                    torrent.metadata.file.WriteLast(piece, data, offset, data.Length - offset);
                } else
                {
                    torrent.metadata.file.Write(piece, data, offset);
                }

                torrent.metadata.progress.SetBit(piece);
                
                //Log("[RECV][METADATA] Bitfield Progress");
                //torrent.metadata.progress.PrintBitField();
                //Log("[RECV][METADATA] Bitfield Requests");
                //torrent.metadata.requests.PrintBitField();
            
                if ( torrent.metadata.file.chunksCounter == torrent.metadata.progress.size - 1 )
                {
                    torrent.metadata.isDone = true;
                    log.RestartTime();
                    curSeconds = 0;

                    Log($"Creating Metadata File {torrent.metadata.file.FileName}");
                    torrent.metadata.file.CreateFile();
                    torrent.FillFromMetadata();

                    Options.TorrentCallback?.BeginInvoke(torrent, null, null);
                }
            }
            
            
        }
        private void MetadataRejected(int piece, string src)
        {
            Log($"[{src.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Rejected");

            // Back to bitfield 0
            // piece can be 1 when bitfield is size 1 because of init bitfield(2)
            // piece < torrent.metadata.progress.size - 1
            if ( !torrent.metadata.progress.GetBit(piece) ) torrent.metadata.requests.UnSetBit(piece);
        }

        // Peer Callbacks       [Torrent Data]
        private void PieceReceived(byte[] data, int piece, int offset, Peer peer)
        {
            int block = offset / torrent.data.blockSize;

            // [Already Received | SHA-1 Validation Failed] => leave
            lock ( lockerTorrent )
            {   
                bool containsKey = torrent.data.pieceProgress.ContainsKey(piece);

                Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Size: {data.Length} Block: {block} Offset: {offset}");
                
                // Piece Done | Block Done
                if (   (!containsKey && torrent.data.progress.GetBit(piece) ) // Piece Done
                    || ( containsKey && torrent.data.pieceProgress[piece].progress.GetBit(block)) ) // Block Done
                { 
                    Stats.BytesDropped += data.Length; 
                    pieceAlreadyRecv++;
                    Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Size: {data.Length} Block: {block} Offset: {offset}, GetBit: {torrent.data.progress.GetBit(piece)} Already received"); 

                    return; 
                }

                if ( !torrent.data.progress.GetBit(piece) && !torrent.data.pieceProgress.ContainsKey(piece) )
                    { Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Size: {data.Length} Block: {block} Offset: {offset} Unknown piece arrived!"); return; }

                Stats.BytesDownloaded += data.Length;

                // Parse Block Data to Piece Data
                Buffer.BlockCopy(data, 0, torrent.data.pieceProgress[piece].data, offset, data.Length);

                // SetBit Block | Leave if more blocks required for Piece
                torrent.data.pieceProgress[piece].progress.     SetBit(block);
                torrent.data.pieceProgress[piece].requests.     SetBit(block); // In case of Timed out (to avoid re-requesting)
                if ( torrent.data.pieceProgress[piece].progress.GetFirst0() != -1 ) return;

                // SHA-1 Validation for Piece Data | Failed? -> Re-request whole Piece! | Lock, No thread-safe!
                byte[] pieceHash = sha1.ComputeHash(torrent.data.pieceProgress[piece].data);

                if ( !Utils.ArrayComp(torrent.file.pieces[piece], pieceHash) )
                {
                    Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Size: {data.Length} Block: {block} Offset: {offset} Size2: {torrent.data.pieceProgress[piece].data.Length} SHA-1 validation failed");

                    Stats.BytesDropped      += torrent.data.pieceProgress[piece].data.Length;
                    Stats.BytesDownloaded   -= torrent.data.pieceProgress[piece].data.Length;
                    sha1Fails++;

                    torrent.data.requests.      UnSetBit(piece);
                    torrent.data.pieceProgress. Remove(piece);

                    return;
                }
            }

            //RequestPiece(peer); // Fast Request?

            // Save Piece in PartFiles [No lock | Thread-safe]
            SavePiece(torrent.data.pieceProgress[piece].data, piece, torrent.data.pieceProgress[piece].data.Length);

            // [SetBit for Progress | Remove Block Progress] | Done => CreateFiles
            lock ( lockerTorrent ) { 
                torrent.data.progress.      SetBit(piece);
                torrent.data.requests.      SetBit(piece); // In case of Timed out (to avoid re-requesting)
                torrent.data.pieceProgress. Remove(piece);
            
                if ( torrent.data.progress.GetFirst0() == - 1 ) // just compare with pieces size
                {
                    Log("[FINISH] Creating Files ...");
                    for (int i = 0; i < torrent.data.files.Count; i++)
                        torrent.data.files[i].CreateFile();

                    torrent.data.isDone = true;
                    Stats.FinishedOn    = DateTime.UtcNow.Ticks;
                    status              = Status.STOPPED;
                }
            }
        }
        private void PieceRejected(int piece, int offset, int size, Peer peer)
        {
            int block = offset / torrent.data.blockSize;

            Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P][REJECTED]\tPiece: {piece} Size: {size} Block: {block} Offset: {offset}");
            
            lock ( lockerTorrent )
            {
                bool containsKey = torrent.data.pieceProgress.ContainsKey(piece);

                // !(Piece Done | Block Done)
                if ( !(( !containsKey && torrent.data.progress.GetBit(piece) ) 
                    || (  containsKey && torrent.data.pieceProgress[piece].progress.GetBit(block))) )
                { 
                    Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P][REJECTE2]\tPiece: {piece} Size: {size} Block: {block} Offset: {offset}");
                    if ( containsKey ) torrent.data.pieceProgress[piece].requests.UnSetBit(block);
                    torrent.data.requests.UnSetBit(piece);
                }

                pieceRejected++;
            }
        }
        
        // Misc
        private void Log(string msg) { if (Options.Verbosity > 0) log.Write($"[TorSwarm] {msg}"); }

    }
}
