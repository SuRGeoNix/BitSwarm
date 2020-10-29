using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using System.Collections.Generic;

using SuRGeoNix.BEP;
using static SuRGeoNix.BEP.Torrent.TorrentData;

namespace SuRGeoNix
{
    public class BitSwarm
    {

        /* BitSwarm's Thread Pool [TODO]
        * 
        * 1. Support Increase Size as well
        * 2. On Increase/Decrease Size remove dead Threads
        * 3. Support also Long-Run (Dedicated) Threads and Transfer from Short-Run to Long-Run
        */
        public class BSTP
        {
            public static BSTPThread[]  Threads;
            public static bool          Stop;
            public static int           Size;
            public static int           Available;
            public static int           Running     => Size - Available;

            public static void SetThreads(int size)
            {
                Size = size;
                if (Threads != null) return; // Currently you can reduce the initial size but not increase it

                Stop        = false;
                Available   = 0;

                Threads = new BSTPThread[size];

                for (int i=0; i<size; i++)
                {
                    int cacheI = i;

                    Threads[i]                      = new BSTPThread();
                    Threads[i].thread               = new Thread(_ => { ThreadRun(cacheI); } );
                    Threads[i].thread.IsBackground  = true;
                    if (i % 25 == 0) Thread.Sleep(25);
                    Threads[i].thread.Start();
                }
            }
            public static void ThreadRun(int index)
            {
                //Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Started");
                Interlocked.Increment(ref Available);
                Threads[index].IsAlive  = true;

                while (!Stop && Available <= Size)
                {
                    if (Threads[index].IsRunning)
                    {
                        Interlocked.Decrement(ref Available);
                        Threads[index].peer?.Run();
                        if (Threads != null) Threads[index].IsRunning = false;
                        Interlocked.Increment(ref Available);
                    }
                    else Thread.Sleep(15);
                }

                if (Threads != null)
                {
                    Threads[index].IsAlive  = false;
                    Threads[index].IsRunning= false;
                }
                Interlocked.Decrement(ref Available);
                //Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Stopped");
            }
            public static bool Dispatch(Peer peer)
            {
                if (Available == 0 || Stop) return false;

                foreach (var thread in Threads)
                    if (!thread.IsRunning && thread.IsAlive)
                    {
                        thread.peer     = peer;
                        thread.IsRunning= true;

                        return true;
                    }

                return false;
            }
            public static void Dispose()
            {
                Stop    = true;
                Threads = null;
            }

            public class BSTPThread
            {
                public bool     IsRunning   { get; internal set; }
                public bool     IsAlive     { get; internal set; }

                public Thread   thread;
                public Peer     peer;
            }
        }

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

            public int      MaxConnections      { get; set; } // Max Total  Connections
            public int      MaxThreads          { get; set; } // Max New    Connections Threads

            public int      SleepDownloadLimit  { get; set; } // Activates Sleep Mode (Low Resources) - Controls the DHT Start/Stop & Re-Fills (DHT/Trackers)

            public int      PeersFromTracker    { get; set; }

            public int      DownloadLimit       { get; set; }
            //public int      UploadLimit         { get; set; }

            public int      ConnectionTimeout   { get; set; }
            public int      HandshakeTimeout    { get; set; }
            public int      MetadataTimeout     { get; set; }
            public int      PieceTimeout        { get; set; }

            public bool     EnableDHT           { get; set; }
            public int      RequestBlocksPerPeer{ get; internal set; }

            public int      Verbosity           { get; set; }   // 1 -> BitSwarm | DHT, 2 -> Peers | Trackers
            public bool     LogTracker          { get; set; }
            public bool     LogPeer             { get; set; }
            public bool     LogDHT              { get; set; }
            public bool     LogStats            { get; set; }
        }

        public event FocusPointCompletedHandler FocusPointCompleted;
        public delegate void FocusPointCompletedHandler(object source, FocusPointCompletedArgs e);
        public class FocusPointCompletedArgs
        {
            public FocusPoint FocusPoint { get; set; }
            public FocusPointCompletedArgs(FocusPoint focusPoint)
            {
                FocusPoint = focusPoint;
            }
        }

        public event MetadataReceivedHandler MetadataReceived;
        public delegate void MetadataReceivedHandler(object source, MetadataReceivedArgs e);
        public class MetadataReceivedArgs
        {
            public Torrent Torrent { get; set; }
            public MetadataReceivedArgs(Torrent torrent)
            {
                Torrent = torrent;
            }
        }

        public event StatusChangedHandler StatusChanged;
        public delegate void StatusChangedHandler(object source, StatusChangedArgs e);
        public class StatusChangedArgs : EventArgs
        {
            public int      Status      { get; set; } // 0: Stopped, 1: Finished, 2: Error + Msg
            public string   ErrorMsg    { get; set; }
            public StatusChangedArgs(int status, string errorMsg = "")
            {
                Status     = status;
                ErrorMsg   = errorMsg;
            }
        }

        public event StatsUpdatedHandler StatsUpdated;
        public delegate void StatsUpdatedHandler(object source, StatsUpdatedArgs e);
        public class StatsUpdatedArgs : EventArgs
        {
            public StatsStructure Stats { get; set; }
            public StatsUpdatedArgs(StatsStructure stats)
            {
                Stats = stats;
            }
        }

        public struct StatsStructure
        {
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

            public bool     SleepMode           { get; set; }
        }

        // Focus Points
        public class FocusPoint
        {
            public FocusPoint(long id, int fromPiece, int toPiece/*, int block*/) {  this.id = id; this.fromPiece = fromPiece; this.toPiece = toPiece; }
            public long id;
            public int fromPiece;
            public int toPiece;
            public bool isDone;
        }
        public Dictionary<long, FocusPoint> FocusPoints { get; private set; } = new Dictionary<long, FocusPoint>();
        public void CreateFocusPoint(FocusPoint fp) { Log($"[FOCUSPOINT] Creating Focus Point from {fp.fromPiece} to {fp.toPiece}"); lock (FocusPoints) if (!FocusPoints.ContainsKey(fp.id)) FocusPoints.Add(fp.id, fp); else FocusPoints[fp.id].toPiece = Math.Max(FocusPoints[fp.id].toPiece, fp.toPiece); }
        public void DeleteFocusPoint(long id)       { Log($"[FOCUSPOINT] Deleting Focus Point from {id}"); lock (FocusPoints) FocusPoints.Remove(id); }
        public void DeleteFocusPoints()             { Log($"[FOCUSPOINT] Deleting Focus Points"); lock (FocusPoints) FocusPoints.Clear(); }

        // Lockers
        readonly object  lockerTorrent       = new object();
        readonly object  lockerMetadata      = new object();
        readonly object  lockerPeers         = new object();

        // Generators (Hash / Random)
        public  static SHA1             sha1                = new SHA1Managed();
        private static Random           rnd                 = new Random();
        private byte[]                  peerID;

        // Main [Torrent / Trackers / Peers / Options]
        private Torrent                 torrent;
        private List<Tracker>           trackers;
        private Tracker.Options         trackerOpt;
        private List<Peer>              peers;
        public  DHT                     dht;
        private DHT.Options             dhtOpt;

        private Logger                  log;
        private Logger                  logDHT;
        private Thread                  beggar;
        private Status                  status;
        private Mode                    mode;

        private long                    metadataLastRequested;
        private long                    lastCheckOfTimeoutsAt;

        // More Stats
        private long                    curSecondTicks      = 0;
        private int                     curSeconds          = 0;
        private int                     pieceTimeouts       = 0;
        private int                     pieceRejected       = 0;
        private int                     pieceAlreadyRecv    = 0;
        private int                     sha1Fails           = 0;
        private int                     dhtPeers            = 0;

        private enum Status
        {
            RUNNING     = 0,
            PAUSED      = 1,
            STOPPED     = 2
        }
        private enum Mode
        {
            NORMAL,
            STARTGAME,
            ENDGAME
        }

        // Constructors / Initializers / Setup / IncludeFiles
        public BitSwarm() { Options = GetDefaultsOptions(); }
        public BitSwarm(OptionsStruct? opt = null)
        {
            Options = (opt == null) ? GetDefaultsOptions() : (OptionsStruct) opt;
        }
        public void Initiliaze(string torrent)
        {
            Initiliaze();
            this.torrent.FillFromTorrentFile(torrent);
            Setup();
        }
        public void Initiliaze(Uri magnetLink)
        {
            Initiliaze();
            torrent.FillFromMagnetLink(magnetLink);
            Setup();
        }
        private void Initiliaze()
        {
            peerID                          = new byte[20]; rnd.NextBytes(peerID);

            trackers                        = new List<Tracker>();
            peers                           = new List<Peer>();

            torrent                         = new Torrent(Options.DownloadPath);
            torrent.metadata.progress       = new BitField(20); // Max Metadata Pieces
            torrent.metadata.pieces         = 2;                // Consider 2 Pieces Until The First Response
            torrent.metadata.parallelRequests= 8;               // How Many Peers We Will Ask In Parallel (firstPieceTries/2)

            log                             = new Logger(Path.Combine(Options.DownloadPath, "session" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"), true);
            if ( Options.EnableDHT )
                logDHT                      = new Logger(Path.Combine(Options.DownloadPath, "session" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_DHT.log"), true);

            status                          = Status.STOPPED;
            mode                            = Mode.NORMAL;
        }
        private void Setup()
        {
            // DHT
            if (Options.EnableDHT)
            {
                dhtOpt                      = DHT.GetDefaultOptions();
                dhtOpt.LogFile              = logDHT;
                dhtOpt.Verbosity            = Options.LogDHT ? Options.Verbosity : 0;
                dhtOpt.NewPeersClbk         = FillPeersFromDHT;
                dht                         = new DHT(torrent.file.infoHash, dhtOpt);
            }

            // Tracker
            trackerOpt                      = new Tracker.Options();
            trackerOpt.PeerId               = peerID;
            trackerOpt.InfoHash             = torrent.file.infoHash;
            trackerOpt.ConnectTimeout       = Options.ConnectionTimeout;
            trackerOpt.ReceiveTimeout       = Options.HandshakeTimeout;

            trackerOpt.LogFile              = log;
            trackerOpt.Verbosity            = Options.LogTracker ? Options.Verbosity : 0;

            // Peer
            Peer.Options.Beggar             = this;
            Peer.Options.PeerID             = peerID;
            Peer.Options.InfoHash           = torrent.file.infoHash;
            Peer.Options.ConnectionTimeout  = Options.ConnectionTimeout;
            Peer.Options.HandshakeTimeout   = Options.HandshakeTimeout;
            Peer.Options.Pieces             = torrent.data.pieces;
            Peer.Options.LogFile            = log;
            Peer.Options.Verbosity          = Options.LogPeer ? Options.Verbosity : 0;

            // Misc
            //ThreadPool.SetMinThreads        (Options.MinThreads, Options.MinThreads);

            FillTrackersFromTorrent();

            if (  (torrent.file.length > 0      || (torrent.file.lengths != null && torrent.file.lengths.Count > 0))  
                && torrent.file.pieceLength > 0 && (torrent.file.pieces  != null && torrent.file.pieces.Count  > 0))
                { torrent.metadata.isDone = true; MetadataReceived?.Invoke(this, new MetadataReceivedArgs(torrent)); }

            // TODO: 
            // 1. Default Trackers List
            // 2. Local ISP SRV _bittorrent-tracker.<> http://bittorrent.org/beps/bep_0022.html
        }
        public static OptionsStruct GetDefaultsOptions()
        {
            OptionsStruct opt       = new OptionsStruct();

            opt.DownloadPath        = Path.GetTempPath();
            opt.TempPath            = Path.GetTempPath();

            opt.MaxConnections      = 200;
            opt.MaxThreads          = 20;

            opt.SleepDownloadLimit  = Timeout.Infinite;

            opt.PeersFromTracker    =  -1;

            opt.DownloadLimit       = Timeout.Infinite;
            //opt.UploadLimit         = Timeout.Infinite;

            opt.ConnectionTimeout   = 1200;
            opt.HandshakeTimeout    = 2200;
            opt.MetadataTimeout     = 1600;
            opt.PieceTimeout        = 6200;

            opt.EnableDHT           = true;
            opt.RequestBlocksPerPeer=   6;

            opt.Verbosity           =   0;

            opt.LogTracker          = false;
            opt.LogPeer             = false;
            opt.LogDHT              = false;
            opt.LogStats            = false;

            return opt;
        }
        public void IncludeFiles(List<string> includeFiles)
        {
            if ( !torrent.metadata.isDone ) return;

            BitField newProgress = new BitField(torrent.data.pieces);
            BitField newRequests = new BitField(torrent.data.pieces);
            newProgress.SetAll();
            newRequests.SetAll();

            lock ( lockerTorrent)
            {
                long curDistance = 0;
                for (int i=0; i<torrent.file.paths.Count; i++)
                { 
                    bool isIncluded = false;

                    foreach (string file in includeFiles)
                    {
                        if ( file == torrent.file.paths[i] )
                        {
                            if (!torrent.data.filesIncludes.Contains(torrent.file.paths[i]))
                            {
                                newProgress.CopyFrom(torrent.data.progressPrev, (int) (curDistance/torrent.file.pieceLength), (int) ((curDistance + torrent.file.lengths[i])/torrent.file.pieceLength));
                                newRequests.CopyFrom(torrent.data.requestsPrev, (int) (curDistance/torrent.file.pieceLength), (int) ((curDistance + torrent.file.lengths[i])/torrent.file.pieceLength));
                            }
                            else if (torrent.data.filesIncludes.Contains(torrent.file.paths[i]))
                            {
                                newProgress.CopyFrom(torrent.data.progress,     (int) (curDistance/torrent.file.pieceLength), (int) ((curDistance + torrent.file.lengths[i])/torrent.file.pieceLength));
                                newRequests.CopyFrom(torrent.data.requests,     (int) (curDistance/torrent.file.pieceLength), (int) ((curDistance + torrent.file.lengths[i])/torrent.file.pieceLength));
                            }

                            isIncluded = true;
                            break; 
                        }
                    }

                    if (torrent.data.filesIncludes.Contains(torrent.file.paths[i]) && !isIncluded )
                    {
                        torrent.data.progressPrev.CopyFrom(torrent.data.progress, (int) (curDistance/torrent.file.pieceLength), (int) ((curDistance + torrent.file.lengths[i])/torrent.file.pieceLength));
                        torrent.data.requestsPrev.CopyFrom(torrent.data.requests, (int) (curDistance/torrent.file.pieceLength), (int) ((curDistance + torrent.file.lengths[i])/torrent.file.pieceLength));
                    }

                    curDistance += torrent.file.lengths[i];
                }

                torrent.data.filesIncludes = includeFiles;
                torrent.data.progress.CopyFrom(newProgress);
                torrent.data.requests.CopyFrom(newRequests);
            }
        }

        // Start / Pause / Dispose
        public void Start()
        {
            if (status == Status.RUNNING || (torrent.data.progress != null && torrent.data.progress.GetFirst0() == - 1)) return;
            
            status = Status.RUNNING;
            mode = Mode.NORMAL;
            torrent.data.isDone = false;

            Utils.EnsureThreadDoneNoAbort(beggar);

            beggar = new Thread(() =>
            {
                Beggar();

                if (torrent.data.isDone)
                    StatusChanged?.Invoke(this, new StatusChangedArgs(0));
                else
                    StatusChanged?.Invoke(this, new StatusChangedArgs(1));
            });

            beggar.IsBackground = true;
            beggar.Start();
        }
        public void Pause()
        {
            if ( status == Status.PAUSED ) return;

            status = Status.PAUSED;
            Utils.EnsureThreadDoneNoAbort(beggar);
        }
        public void Dispose()
        {
            try
            {
                status = Status.STOPPED;
                Utils.EnsureThreadDoneNoAbort(beggar);

                if (peers != null)
                    foreach (Peer peer in peers)
                        peer.Disconnect();

                if (torrent != null) torrent.Dispose();
                if (logDHT  != null) logDHT. Dispose();
                if (log     != null) log.    Dispose();

                BSTP.Dispose();

            } catch (Exception) { }
        }

        // Feeders [Torrent -> Trackers | Trackers -> Peers | DHT -> Peers | Client -> Stats]
        private void FillTrackersFromTorrent()
        {
            foreach (Uri uri in torrent.file.trackers)
            {
                if (uri.Scheme.ToLower() == "http" || uri.Scheme.ToLower() == "https" || uri.Scheme.ToLower() == "udp")
                {
                    Log($"[Torrent] [Tracker] [ADD] {uri}");
                    trackers.Add(new Tracker(uri, trackerOpt));
                }
                else
                    Log($"[Torrent] [Tracker] [ADD] {uri} Protocol not implemented");
            }
        }
        private void FillPeersFromTracker(int pos)
        {
            if (!trackers[pos].Announce(Options.PeersFromTracker) ) 
            {
                if ( Options.LogTracker) trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port} Failed");
                return;
            }

            if (trackers[pos].peers == null)
            {
                trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] No peers");
                return;
            }

            lock (lockerPeers)
            {
                if (Options.LogTracker) trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] [BEFORE] Adding {trackers[pos].peers.Count} in Peers {peers.Count}");

                foreach (Peer peer in peers)
                    if (trackers[pos].peers.ContainsKey(peer.host))
                    {
                        if ( Options.LogTracker) trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] Peer {peer.host} already exists"); 
                        trackers[pos].peers.Remove(peer.host);
                    }

                foreach (KeyValuePair<string, int> peerKV in trackers[pos].peers)
                    peers.Add(new Peer(peerKV.Key, peerKV.Value));

                if (Options.LogTracker) trackers[pos].Log($"[{trackers[pos].host}:{trackers[pos].port}] [AFTER] Peers {peers.Count}");
            }
        }
        private void FillPeersFromTrackers()
        {
            for (int i=0; i<trackers.Count; i++)
            {
                int noCache = i;
                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate(object state) { Thread.Sleep(rnd.Next(0, 200)); FillPeersFromTracker(noCache); }), null);
            }
        }
        private void FillPeersFromDHT(ConcurrentDictionary<string, int> newPeers, bool removeFromSource = true)
        {
            Dictionary<string, int> restorePeers = new Dictionary<string, int>();

            lock (lockerPeers)
            {
                if (Options.LogDHT) dht.Log($"[BEFORE] Adding {newPeers.Count} in Peers {peers.Count}");

                foreach (Peer peer in peers)
                    if ( newPeers.ContainsKey(peer.host) )
                    {
                        if (Options.LogDHT) dht.Log($"Peer {peer.host} already exists");
                        if(newPeers.TryRemove(peer.host, out int peervalue) && !removeFromSource)
                            restorePeers.Add(peer.host, peer.port);
                    }

                foreach (KeyValuePair<string, int> peerKV in newPeers)
                    peers.Add(new Peer(peerKV.Key, peerKV.Value));    

                dhtPeers += newPeers.Count;
                if ( Options.LogDHT) dht.Log($"[AFTER] Peers {peers.Count}");
            }

            if(!removeFromSource)
            {
                foreach (KeyValuePair<string, int> peerKV in restorePeers)
                    newPeers.TryAdd(peerKV.Key, peerKV.Value);
            }
        }
        private void FillStats()
        {
            long totalBytesDownloaded = Stats.BytesDownloaded + Stats.BytesDropped;
            
            if (curSeconds > 0) 
                Stats.AvgRate   = (int) (totalBytesDownloaded / curSeconds);
            
            if (torrent.data.totalSize - Stats.BytesDownloaded == 0)
            {
                Stats.ETA       = 0;
                Stats.AvgETA    = 0;
            } 
            else
            {
                if (Stats.BytesDownloaded - Stats.BytesDownloadedPrev == 0)
                    Stats.ETA  *= 2; // Kind of infinite | int overflow nice
                else 
                    Stats.ETA   = (int) ( (torrent.data.totalSize - Stats.BytesDownloaded) / ((Stats.BytesDownloaded - Stats.BytesDownloadedPrev) / 2) );

                if (Stats.BytesDownloaded  == 0)
                    Stats.AvgETA*= 2; // Kind of infinite
                else
                    if (curSeconds > 0) Stats.AvgETA = (int) ( (torrent.data.totalSize - Stats.BytesDownloaded) / (Stats.BytesDownloaded / curSeconds ) );
            }

            if (Stats.DownRate > Stats.MaxRate) Stats.MaxRate = Stats.DownRate;

            Stats.BytesDownloadedPrev   = totalBytesDownloaded;
            Stats.PeersTotal            = peers.Count;

            // Stats -> UI
            StatsUpdated?.Invoke(this, new StatsUpdatedArgs(Stats));

            // Stats -> Log
            if (Options.LogStats)
            {
                Log($"[STATS] [INQUEUE: {String.Format("{0,3}",Stats.PeersInQueue)}]\t[DROPPED: {String.Format("{0,3}",Stats.PeersDropped)}]\t[CONNECTING: {String.Format("{0,3}",Stats.PeersConnecting)}]\t[FAIL1: {String.Format("{0,3}",Stats.PeersFailed1)}]\t[FAIL2: {String.Format("{0,3}",Stats.PeersFailed2)}]\t[READY: {String.Format("{0,3}",Stats.PeersConnected)}]\t[CHOKED: {String.Format("{0,3}",Stats.PeersChoked)}]\t[UNCHOKED: {String.Format("{0,3}",Stats.PeersUnChoked)}]\t[DOWNLOADING: {String.Format("{0,3}",Stats.PeersDownloading)}]");
                Log($"[STATS] [CUR MAX: {String.Format("{0:n0}", (Stats.MaxRate / 1024)) + " KB/s"}]\t[DOWN CUR: {String.Format("{0:n0}", (Stats.DownRate / 1024)) + " KB/s"}]\t[DOWN AVG: {String.Format("{0:n0}", (Stats.AvgRate / 1024)) + " KB/s"}]\t[ETA CUR: {TimeSpan.FromSeconds(Stats.ETA).ToString(@"hh\:mm\:ss")}]\t[ETA AVG: {TimeSpan.FromSeconds(Stats.AvgETA).ToString(@"hh\:mm\:ss")}]\t[ETA R: {TimeSpan.FromSeconds((Stats.ETA + Stats.AvgETA)/2).ToString(@"hh\:mm\:ss")}]");
                Log($"[STATS] [TIMEOUTS: {String.Format("{0,4}",pieceTimeouts)}]\t[ALREADYRECV: {String.Format("{0,3}",pieceAlreadyRecv)}]\t[REJECTED: {String.Format("{0,3}",pieceRejected)}]\t[SHA1FAILS:{String.Format("{0,3}",sha1Fails)}]\t[DROPPED BYTES: {Utils.BytesToReadableString(Stats.BytesDropped)}]\t[DHTPEERS: {dhtPeers}]");
                Log($"[STATS] [PROGRESS PIECES: {torrent.data.progress.setsCounter}/{torrent.data.progress.size} | REQ: {torrent.data.requests.setsCounter}]\t[PROGRESS BYTES: {Stats.BytesDownloaded}/{torrent.data.totalSize}]\t[Pieces/Blocks: {torrent.data.pieces}/{torrent.data.blocks}]\t[Piece/Block Length: {torrent.data.pieceSize}|{torrent.data.totalSize % torrent.data.pieceSize}/{torrent.data.blockSize}|{torrent.data.blockLastSize}][Working Pieces: {torrent.data.pieceProgress.Count}]");
            }
        }


        // \******** BEGGAR *********
        private void Beggar()
        {
            try
            {
                if (Utils.IsWindows) Utils.TimeBeginPeriod(1);

                BSTP.SetThreads(Options.MaxThreads);

                bool disconnectPeer     = false; // for downlimit

                Stats.MaxRate           = 0;
                curSeconds              = 0;
                metadataLastRequested   = DateTime.UtcNow.Ticks;

                Options.DownloadLimit       = Options.DownloadLimit     <= 0 ? Int32.MaxValue : Options.DownloadLimit;
                Options.SleepDownloadLimit  = Options.SleepDownloadLimit<= 0 ? Int32.MaxValue : Options.SleepDownloadLimit;
                if (Options.SleepDownloadLimit > Options.DownloadLimit) Options.SleepDownloadLimit = Options.DownloadLimit;

                for (int i=0; i<peers.Count; i++)
                    peers[i] = new Peer(peers[i].host, peers[i].port);

                log.RestartTime();
                if (Options.EnableDHT) { logDHT.RestartTime(); FillPeersFromDHT(dht.CachedPeers); dht.ClearCachedPeers(); dht.Start(); }
                FillPeersFromTrackers();

                Log("[BEGGAR  ] " + status);
                while (status == Status.RUNNING)
                {
                    lock (lockerPeers)
                    {
                        curSecondTicks = DateTime.UtcNow.Ticks;
                        Stats.SleepMode= Stats.DownRate > 0 && Stats.DownRate / 1024 > Options.SleepDownloadLimit;

                        for (int i = peers.Count - 1; i >= 0; i--)
                        {
                            Peer peer = peers[i];
                            if (status != Status.RUNNING) break;

                            if (peer.status == Peer.Status.READY)
                                RequestPiece(peer);
                            else if (peer.status == Peer.Status.NEW && (BSTP.Running + Stats.PeersConnected + Stats.PeersDownloading) < Options.MaxConnections)
                                if (BSTP.Dispatch(peer)) peer.status = Peer.Status.CONNECTING;
                        }

                        // Scheduler

                        // Every 1350 ms | Check Request Timeouts (Torrent)
                        if (torrent.metadata.isDone && curSecondTicks - lastCheckOfTimeoutsAt > 1350 * 10000) CheckRequestTimeouts();

                        // Per Second
                        if (log.GetTime() - (curSeconds * 1000) > 990)
                        {
                            curSeconds++;

                            if (!torrent.metadata.isDone)
                            {
                                // Scheduler Every 1 Second | Check Request Timeouts (Metadata)
                                if (curSecondTicks - metadataLastRequested > Options.MetadataTimeout * 10000) { torrent.metadata.parallelRequests += 2; Log($"[REQ ] [M] Timeout"); }
                            }
                            else
                            {
                                // Scheduler Every 1 Second
                                //Console.WriteLine($"New Cons -> {BSTP.Size - BSTP.Available}");

                                // Scheduler Every 2 Second
                                if (curSeconds % 2 == 0)
                                {
                                    Stats.DownRate = (int) ((Stats.BytesDownloaded + Stats.BytesDropped) - Stats.BytesDownloadedPrev) / 2; // Change this (2 seconds) if you change scheduler
                                    
                                    // Check Downlimit (messy algo - until we have stats from peers)
                                    if (Stats.DownRate > 0 && Stats.DownRate / 1024 > Options.DownloadLimit)
                                    {
                                        disconnectPeer = true;
                                        Options.RequestBlocksPerPeer = 3;
                                        Console.WriteLine("Diff: " + ((Stats.DownRate / 1024) - Options.DownloadLimit) + " | Down: " + Stats.PeersDownloading + " | Blocks: " + Options.RequestBlocksPerPeer);
                                    }
                                    else
                                    {
                                        Options.RequestBlocksPerPeer++;

                                        if (Options.RequestBlocksPerPeer > 5)
                                            Options.RequestBlocksPerPeer = 6;
                                    }

                                    // Check Mode (-> END GAME)
                                    if (torrent.data.pieces > 0 &&  (torrent.data.pieces - torrent.data.progress.setsCounter) * torrent.data.blocks < 300)
                                    {
                                        Log($"[MODE] Getting in End Game Mode");
                                        mode = Mode.ENDGAME;
                                    }

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
                                        switch (peer.status)
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

                                                // Drop No Our Pieces Peers (after 3 seconds)
                                                if (curSecondTicks - peer.connectedAt > 7 * 1000 * 10000 && (peer.stageYou.haveNone || (!peer.stageYou.haveAll && peer.stageYou.bitfield == null) || torrent.data.requests.GetFirst01(peer.stageYou.bitfield) == -1 ))
                                                {
                                                    Log($"[DROP] No Pieces Peer {peer.host}");
                                                    cleanPeers.Add(peer.host);
                                                    Stats.PeersFailed2++;
                                                }

                                                // Drop Chocked Peers (after 32 seconds)
                                                else if (!peer.stageYou.unchoked) 
                                                {
                                                    if (curSecondTicks - peer.chokedAt > 130 * 1000 * 10000)
                                                    {
                                                        Log($"[DROP] Choked Peer {peer.host}");
                                                        cleanPeers.Add(peer.host);
                                                        Stats.PeersFailed2++;
                                                    }
                                                    else
                                                    {
                                                        Stats.PeersConnected++;
                                                        Stats.PeersChoked++;
                                                    }
                                                }
                                                else if (peer.stageYou.unchoked)
                                                {
                                                    Stats.PeersConnected++;
                                                    Stats.PeersUnChoked++;
                                                }

                                                break;
                                            case Peer.Status.DOWNLOADING:
                                                if (disconnectPeer)
                                                {
                                                    disconnectPeer = false;

                                                    Log($"[DROP] Downlimit Peer {peer.host}");
                                                    cleanPeers.Add(peer.host);
                                                    
                                                    Stats.PeersFailed2++;
                                                }
                                                else
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

                                    FillStats();

                                } // Every 2 Seconds

                            } // !Metadata Received
                            
                            // Scheduler Every 5 Seconds    [DHT Stop/Start | Peers MSG Keep Alive]
                            if (curSeconds % 5 == 0)
                            {
                                // DHT Start / Stop
                                if (Options.EnableDHT)
                                {
                                    if (     dht.status == DHT.Status.RUNNING && (Stats.DownRate / 1024 > Options.SleepDownloadLimit || Stats.DownRate / 1024 > Options.DownloadLimit))
                                    {
                                        Log("[DHT FORCE STOP]");
                                        dht.Stop();
                                    }
                                    else if (dht.status == DHT.Status.STOPPED && !Stats.SleepMode)
                                    {
                                        Log("[DHT FORCE START]");
                                        dht.Start();
                                    }
                                }

                                // Keep Alives / Interested
                                foreach (Peer peer in peers)
                                {
                                    switch (peer.status)
                                    {
                                        case Peer.Status.READY:
                                            if ( (curSecondTicks - peer.lastAction) / 10000 > 3000 )
                                                peer.SendKeepAlive();

                                            break;
                                    }
                                }
                            }

                            // Scheduler Every 9 Seconds    [Refill Tracker/DHT if required]
                            if (curSeconds % 9 == 0)
                            {
                                if (!Stats.SleepMode)
                                    if (Options.EnableDHT && Stats.PeersInQueue < 100)
                                    {
                                        Log("[REFILL] From DHT");
                                        dhtPeers = 0;
                                        FillPeersFromDHT(dht.CachedPeers, false);
                                    }
                            }

                            // Scheduler Every 16 Seconds   [Peers MSG Intrested]
                            if (curSeconds % 15 == 0)
                            {   
                                if (!Stats.SleepMode && Stats.PeersInQueue < 100)
                                    // Client -> Trackers [Announce]
                                    {Log("[REFILL] From Trackers"); FillPeersFromTrackers(); }

                                foreach (Peer peer in peers)
                                {
                                    // Client-> Peers [MSG Intrested]
                                    if (peer.status == Peer.Status.READY && !peer.stageYou.unchoked)
                                        peer.SendMessage(Peer.Messages.INTRESTED, false, null);
                                }
                            }

                        } // Scheduler [Every Second]

                    } // Lock Peers

                    Thread.Sleep(20);

                } // While

                if (Options.EnableDHT) dht.Stop();

                Log("[BEGGAR  ] " + status);

                // Clean Up
                foreach (Peer peer in peers)
                    peer.Disconnect();

                if (torrent.metadata.isDone) FillStats();

                BSTP.Dispose();

            } catch (ThreadAbortException) {
            } catch (Exception e) { Log($"[BEGGAR] Beggar(), Msg: {e.Message}\r\n{e.StackTrace}"); StatusChanged?.Invoke(this, new StatusChangedArgs(0, e.Message)); }

            if (Utils.IsWindows) Utils.TimeEndPeriod(1);
        }

        //  ******** BEGGAR ********/


        // Pieces [Timeouts]
        private void CheckRequestTimeouts()
        {
            lock (lockerTorrent)
            {
                for (int i=torrent.data.pieceRequests.Count-1; i>=0; i--)
                {
                    if (curSecondTicks - torrent.data.pieceRequests[i].timestamp > Options.PieceTimeout * 10000)
                    {
                        // !(Piece Done | Block Done)
                        bool containsKey = torrent.data.pieceProgress.ContainsKey(torrent.data.pieceRequests[i].piece);
                        if ( !(( !containsKey && torrent.data.progress.GetBit(torrent.data.pieceRequests[i].piece)) 
                             ||(  containsKey && torrent.data.pieceProgress[torrent.data.pieceRequests[i].piece].progress.GetBit(torrent.data.pieceRequests[i].block))) )
                        { 
                            torrent.data.pieceRequests[i].peer.PiecesTimeout++;
                            Log($"[{torrent.data.pieceRequests[i].peer.host.PadRight(15, ' ')}] [REQT][P]\tPiece: {torrent.data.pieceRequests[i].piece} Block: {torrent.data.pieceRequests[i].block} Offset: {torrent.data.pieceRequests[i].block * torrent.data.blockSize} Size: {torrent.data.pieceRequests[i].size} Requests: {torrent.data.pieceRequests[i].peer.PiecesRequested} Timeouts: {torrent.data.pieceRequests[i].peer.PiecesTimeout} Piece timeout");

                            if (torrent.data.pieceRequests[i].peer.status != Peer.Status.READY && torrent.data.pieceRequests[i].peer.PiecesTimeout >= Options.RequestBlocksPerPeer)
                            {
                                Log($"[DROP] [{torrent.data.pieceRequests[i].peer.host.PadRight(15, ' ')}] [REQT][P]\tRequests: {torrent.data.pieceRequests[i].peer.PiecesRequested} Timeouts: {torrent.data.pieceRequests[i].peer.PiecesTimeout} Piece timeout");
                                torrent.data.pieceRequests[i].peer.Disconnect();
                            }
                            else if (torrent.data.pieceRequests[i].peer.status == Peer.Status.DOWNLOADING)
                                 torrent.data.pieceRequests[i].peer.status  = Peer.Status.READY;

                            if (containsKey && !torrent.data.pieceRequests[i].aggressive) torrent.data.pieceProgress[torrent.data.pieceRequests[i].piece].requests.UnSetBit(torrent.data.pieceRequests[i].block);

                            if (!torrent.data.pieceRequests[i].aggressive) { torrent.data.requests.UnSetBit(torrent.data.pieceRequests[i].piece); torrent.data.requestsPrev.UnSetBit(torrent.data.pieceRequests[i].piece); }
                            pieceTimeouts++;
                        }

                        torrent.data.pieceRequests.RemoveAt(i);
                    }
                }
            }

            lastCheckOfTimeoutsAt = DateTime.UtcNow.Ticks;
        }

        // Pieces [Request]
        internal void RequestPiece(Peer peer)
        {
            int piece, block, blockSize;

            // Metadata Requests (Until Done)
            if (!torrent.metadata.isDone)
            {
                if (peer.stageYou.metadataRequested || peer.stageYou.extensions.ut_metadata == 0) return;
                if (torrent.metadata.parallelRequests < 1) return;

                if (torrent.metadata.totalSize == 0)
                {   
                    torrent.metadata.parallelRequests -= 2;
                    peer.RequestMetadata(0, 1);
                    Log($"[{peer.host.PadRight(15, ' ')}] [REQ ][M]\tPiece: 0, 1");
                }
                else
                {
                    piece = torrent.metadata.progress.GetFirst0();
                    if (piece < 0) return;

                    int piece2 = torrent.metadata.progress.GetFirst0(piece + 1);

                    if (piece > torrent.metadata.pieces - 1 || piece2 > torrent.metadata.pieces - 1) return;

                    if (piece2 >= 0)
                    {
                        torrent.metadata.parallelRequests -= 2;
                        peer.RequestMetadata(piece, piece2);
                    }
                    else
                    {
                        torrent.metadata.parallelRequests--;
                        peer.RequestMetadata(piece);
                    }

                    Log($"[{peer.host.PadRight(15, ' ')}] [REQ ][M]\tPiece: {piece},{piece2}");
                }

                metadataLastRequested = DateTime.UtcNow.Ticks;
                peer.stageYou.metadataRequested = true;

                return;
            }

            // Torrent Requests
            // TODO: start/end-game mode
            List<Tuple<int, int, int>> requests = new List<Tuple<int, int, int>>();

            if ( mode == Mode.ENDGAME )
            {
                if (!peer.stageYou.unchoked) return; // Give it Some Time

                if (peer.stageYou.haveNone || (!peer.stageYou.haveAll && peer.stageYou.bitfield == null)) { peer.Disconnect(); return; }

                // Find [piece, block] combination of the last pieces
                List<int> piecesLeft;
                if (peer.stageYou.haveAll)
                    lock ( lockerTorrent ) piecesLeft = torrent.data.progress.GetAll0();
                else
                    lock ( lockerTorrent ) piecesLeft = torrent.data.progress.GetAll0(peer.stageYou.bitfield);

                if (piecesLeft.Count == 0) { peer.Disconnect(); return; }

                List<Tuple<int, int>> piecesBlocksLeft = new List<Tuple<int, int>>();

                foreach (int pieceLeft in piecesLeft)
                {
                    CreatePieceProgress(pieceLeft);
                    List<int> pieceBlocksLeft = torrent.data.pieceProgress[pieceLeft].progress.GetAll0();
                    foreach ( int blockLeft in pieceBlocksLeft )
                        piecesBlocksLeft.Add(new Tuple<int, int>(pieceLeft, blockLeft));
                }

                // Choose Randomly
                int requestsCounter = Math.Min(Options.RequestBlocksPerPeer, piecesBlocksLeft.Count);
                for (int i=0; i<requestsCounter; i++)
                {
                    int curPieceBlock = rnd.Next(0, piecesBlocksLeft.Count);

                    piece = piecesBlocksLeft[curPieceBlock].Item1;
                    block = piecesBlocksLeft[curPieceBlock].Item2;
                    blockSize = GetBlockSize(piece, block);

                    if (Options.Verbosity > 1) Log($"[{peer.host.PadRight(15, ' ')}] [REQE][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                    requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                    lock ( lockerTorrent ) torrent.data.pieceRequests.Add( new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize) );

                    piecesBlocksLeft.RemoveAt(curPieceBlock);
                }

                if (requests.Count > 0) { peer.RequestPiece(requests); Thread.Sleep(15); } // Avoid Reaching Upload Limits

                return;
            }

            if (!peer.stageYou.unchoked || peer.stageYou.haveNone || (!peer.stageYou.haveAll && peer.stageYou.bitfield == null)) return;

            lock (lockerTorrent)
            { 
                for (int i=0; i<Options.RequestBlocksPerPeer; i++)
                {
                    piece           = -1;
                    block           = -1;
                    FocusPoint fp   = null;

                    // Focus points
                    lock (FocusPoints)
                    {
                        if (FocusPoints.Count > 0)
                        {
                            // Choose Random FP
                            fp = FocusPoints.ElementAt(rnd.Next(0, FocusPoints.Count)).Value;

                            // Piece Left (if not Delete FP & return)
                            List<int> piecesLeft = new List<int>();
                            int firstPiece = -1;

                            if (peer.stageYou.haveAll)
                            {
                                firstPiece = torrent.data.progress.GetFirst0(fp.fromPiece, fp.toPiece);

                            }
                            else
                            {
                                firstPiece =  torrent.data.progress.GetFirst01(peer.stageYou.bitfield, fp.fromPiece, fp.toPiece);
                                if (firstPiece < 0 && torrent.data.progress.GetFirst0(fp.fromPiece, fp.toPiece) > -1) { peer.Disconnect(); return; }
                            }

                            if (firstPiece < 0) { DeleteFocusPoint(fp.id); FocusPointCompleted?.Invoke(this, new FocusPointCompletedArgs(fp)); return; }



                            // --------------------

                            piece = firstPiece;

                            CreatePieceProgress(piece);
                            List<int> pieceBlocksLeft = torrent.data.pieceProgress[piece].progress.GetAll0();

                            int requestsCounter = Math.Min(Options.RequestBlocksPerPeer, pieceBlocksLeft.Count);

                            for (int l=0;l<requestsCounter; l++)
                            {
                                int curPieceBlock = rnd.Next(0, pieceBlocksLeft.Count);

                                block = pieceBlocksLeft[curPieceBlock];
                                blockSize = GetBlockSize(piece, block);

                                Log($"[{peer.host.PadRight(15, ' ')}] [REQF][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                                requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                                torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize, true));

                                pieceBlocksLeft.RemoveAt(curPieceBlock);
                            }


                            // --------------------

                            /*
                            piecesLeft.Add(firstPiece);

                            List<Tuple<int, int>> piecesBlocksLeft = new List<Tuple<int, int>>();
                            foreach (int pieceLeft in piecesLeft)
                            {
                                CreatePieceProgress(pieceLeft);
                                List<int> pieceBlocksLeft = torrent.data.pieceProgress[pieceLeft].progress.GetAll0();
                                foreach (int blockLeft in pieceBlocksLeft)
                                    piecesBlocksLeft.Add(new Tuple<int, int>(pieceLeft, blockLeft));
                            }

                            // Piece | Block Left choose Random


                            int requestsCounter = Math.Min(Options.RequestBlocksPerPeer, piecesBlocksLeft.Count);
                            for (int l=0;l<requestsCounter; l++)
                            {
                                int curPieceBlock = rnd.Next(0, piecesBlocksLeft.Count);

                                piece = piecesBlocksLeft[curPieceBlock].Item1;
                                block = piecesBlocksLeft[curPieceBlock].Item2;
                                blockSize = GetBlockSize(piece, block);

                                Log($"[{peer.host.PadRight(15, ' ')}] [REQF][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                                requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                                torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize, true));

                                piecesBlocksLeft.RemoveAt(curPieceBlock);
                            }

                            */

                            //if (requests.Count == Options.RequestBlocksPerPeer) { peer.RequestPiece(requests); Thread.Sleep(10); return; } // Avoid Reaching Upload Limits
                            if (requests.Count == Options.RequestBlocksPerPeer) break;

                            // Let it fill the rest requests
                            i += requests.Count;
                            piece = -1;
                            block = -1;
                        }   
                    }

                    //if (requests.Count == Options.RequestBlocksPerPeer) break;
                    if (requests.Count == Options.RequestBlocksPerPeer) { Thread.Sleep(15); break; }

                    // Normal Requests
                    if (peer.stageYou.haveAll)
                        piece = torrent.data.requests.GetFirst0();
                    else
                        piece = torrent.data.requests.GetFirst01(peer.stageYou.bitfield);

                    if ( piece < 0 ) break;

                    CreatePieceProgress(piece);
                    block = torrent.data.pieceProgress[piece].requests.GetFirst0();
                    if (block < 0) { Log($"Shouldn't be here! Piece: {piece}"); break; }

                    torrent.data.pieceProgress[piece].requests.SetBit(block);
                    if ( torrent.data.pieceProgress[piece].requests.GetFirst0() == -1 ) torrent.data.requests.SetBit(piece);

                    blockSize = GetBlockSize(piece, block);

                    if (Options.Verbosity > 1) Log($"[{peer.host.PadRight(15, ' ')}] [REQ ][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                    // TODO: Requests Timeouts should be related to <i> | 0 1 2 3 4 5 Requests | i * (timeout/15) ? 
                    requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                    torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize));
                }
            }
            
            //Thread.Sleep(10);
            if (requests.Count > 0) peer.RequestPiece(requests);
        }

        // Pieces [Metadata]
        internal void MetadataPieceReceived(byte[] data, int piece, int offset, int totalSize, Peer peer)
        {
            Log($"[{peer.host.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Offset: {offset} Size: {totalSize}");

            lock (lockerMetadata)
            {
                torrent.metadata.parallelRequests  += 2;
                peer.stageYou.metadataRequested     = false;

                if ( torrent.metadata.totalSize == 0 )
                {
                    torrent.metadata.totalSize  = totalSize;
                    torrent.metadata.progress   = new BitField((totalSize/Peer.MAX_DATA_SIZE) + 1);
                    torrent.metadata.pieces     = (totalSize/Peer.MAX_DATA_SIZE) + 1;
                    if ( torrent.file.name != null ) 
                        torrent.metadata.file       = new PartFile(Utils.FindNextAvailablePartFile(Path.Combine(Options.DownloadPath, string.Join("_", torrent.file.name.Split(Path.GetInvalidFileNameChars())) + ".torrent")), Peer.MAX_DATA_SIZE, totalSize, false);
                    else
                        torrent.metadata.file       = new PartFile(Utils.FindNextAvailablePartFile(Path.Combine(Options.DownloadPath, "metadata" + rnd.Next(10000) + ".torrent")), Peer.MAX_DATA_SIZE, totalSize, false);
                }

                if (torrent.metadata.progress.GetBit(piece)) { Log($"[{peer.host.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Already received"); return; }

                if (piece == torrent.metadata.pieces - 1)
                {
                    torrent.metadata.file.WriteLast(piece, data, offset, data.Length - offset);
                } else
                {
                    torrent.metadata.file.Write(piece, data, offset);
                }

                torrent.metadata.progress.SetBit(piece);
            
                if (torrent.metadata.progress.setsCounter == torrent.metadata.pieces)
                {
                    // TODO: Validate Torrent's SHA-1 Hash with Metadata Info
                    torrent.metadata.parallelRequests = -1000;
                    Log($"Creating Metadata File {torrent.metadata.file.FileName}");
                    torrent.metadata.file.CreateFile();
                    torrent.metadata.file.CloseFile();
                    torrent.FillFromMetadata();
                    Peer.Options.Pieces = torrent.data.pieces;
                    MetadataReceived?.Invoke(this, new MetadataReceivedArgs(torrent));

                    if ( Options.EnableDHT ) logDHT.RestartTime();
                    log.RestartTime();
                    curSeconds = 0;
                    torrent.metadata.isDone = true;
                }
            }
        }
        internal void MetadataPieceRejected(int piece, string src)
        {
            torrent.metadata.parallelRequests += 2;
            Log($"[{src.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Rejected");
        }

        // Pieces [Torrent]
        internal void PieceReceived(byte[] data, int piece, int offset, Peer peer)
        {
            // [Already Received | SHA-1 Validation Failed] => leave
            int  block = offset / torrent.data.blockSize;
            bool containsKey;
            bool pieceProgress;
            bool blockProgress = false;

            lock (lockerTorrent)
            {   
                containsKey                     = torrent.data.pieceProgress.ContainsKey(piece);
                pieceProgress                   = torrent.data.progress.GetBit(piece);
                if (containsKey) blockProgress  = torrent.data.pieceProgress[piece].progress.GetBit(block);
    
                // Piece Done | Block Done
                if (   (!containsKey && pieceProgress )     // Piece Done
                    || ( containsKey && blockProgress ) )   // Block Done
                { 
                    Stats.BytesDropped += data.Length; 
                    pieceAlreadyRecv++;
                    Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Block: {block} Offset: {offset} Size: {data.Length} Already received"); 

                    return; 
                }
            
                if (Options.Verbosity > 1) Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Block: {block} Offset: {offset} Size: {data.Length} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");
                Stats.BytesDownloaded += data.Length;

                // Parse Block Data to Piece Data
                Buffer.BlockCopy(data, 0, torrent.data.pieceProgress[piece].data, offset, data.Length);

                // SetBit Block | Leave if more blocks required for Piece
                torrent.data.pieceProgress[piece].progress.     SetBit(block);

                // In case of Timed out (to avoid re-requesting)
                torrent.data.pieceProgress[piece].requests.     SetBit(block);
                if (torrent.data.pieceProgress[piece].requests. GetFirst0() == -1) torrent.data.requests.SetBit(piece);

                if (torrent.data.pieceProgress[piece].progress. GetFirst0() != -1) return;
            
                // SHA-1 Validation for Piece Data | Failed? -> Re-request whole Piece! | Lock, No thread-safe!
                byte[] pieceHash;
                pieceHash = sha1.ComputeHash(torrent.data.pieceProgress[piece].data);
                if (!Utils.ArrayComp(torrent.file.pieces[piece], pieceHash))
                {
                    Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Block: {block} Offset: {offset} Size: {data.Length} Size2: {torrent.data.pieceProgress[piece].data.Length} SHA-1 validation failed");

                    Stats.BytesDropped      += torrent.data.pieceProgress[piece].data.Length;
                    Stats.BytesDownloaded   -= torrent.data.pieceProgress[piece].data.Length;
                    sha1Fails++;

                    torrent.data.requests.      UnSetBit(piece);
                    torrent.data.pieceProgress. Remove(piece);

                    return;
                }

                // Save Piece in PartFiles [Thread-safe?]
                SavePiece(torrent.data.pieceProgress[piece].data, piece, torrent.data.pieceProgress[piece].data.Length);
                Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Full"); 

                // [SetBit for Progress | Remove Block Progress] | Done => CreateFiles
                torrent.data.progress.      SetBit(piece);
                torrent.data.requests.      SetBit(piece); // In case of Timed out (to avoid re-requesting)
                torrent.data.progressPrev.  SetBit(piece);
                torrent.data.requestsPrev.  SetBit(piece);
                torrent.data.pieceProgress. Remove(piece);

                if (torrent.data.progress.GetFirst0() == - 1) // just compare with pieces size
                {
                    Log("[FINISH]");
                    //for (int i = 0; i < torrent.data.files.Count; i++)
                        //torrent.data.files[i].CreateFile();

                    torrent.data.isDone = true;
                    status              = Status.STOPPED;
                }
            }
        }
        internal void PieceRejected(int piece, int offset, int size, Peer peer)
        {
            int block = offset / torrent.data.blockSize;
            Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P][REJECTED]\tPiece: {piece} Size: {size} Block: {block} Offset: {offset}");
            
            lock (lockerTorrent)
            {
                bool containsKey = torrent.data.pieceProgress.ContainsKey(piece);

                // !(Piece Done | Block Done)
                if ( !(( !containsKey && torrent.data.progress.GetBit(piece) ) 
                    || (  containsKey && torrent.data.pieceProgress[piece].progress.GetBit(block))) )
                { 
                    if (containsKey) torrent.data.pieceProgress[piece].requests.UnSetBit(block);
                    torrent.data.requests.UnSetBit(piece);
                }
            }

            pieceRejected++;
        }

        // Pieces [Save]
        private void SavePiece(byte[] data, int piece, int size)
        {
            if (size > torrent.file.pieceLength) { Log("[SAVE] pieceSize > chunkSize"); return; }

            long firstByte  = (long)piece * torrent.file.pieceLength;
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

		            if (firstByte == curSize - torrent.file.lengths[i])
                    {
                        if (Options.Verbosity > 2) Log("[SAVE] F file:\t\t" + (i + 1) + ", chunkId:\t\t" +     0 +     ", pos:\t\t" + writePos + ", len:\t\t" + writeSize);
                        torrent.data.files[i].WriteFirst(data, writePos, writeSize);
                    } else if (ends < curSize) {
                        if (Options.Verbosity > 2) Log("[SAVE] M file:\t\t" + (i + 1) + ", chunkId:\t\t" + chunkId +   ", pos:\t\t" + writePos + ", len:\t\t" + torrent.file.pieceLength + " | " + writeSize);
                        torrent.data.files[i].Write(chunkId,data);
                    } else if (ends >= curSize) {
                        if (Options.Verbosity > 2) Log("[SAVE] L file:\t\t" + (i + 1) + ", chunkId:\t\t" + chunkId +   ", pos:\t\t" + writePos + ", len:\t\t" + writeSize);
                        torrent.data.files[i].WriteLast(chunkId, data, writePos, writeSize);
                    }

                    if (ends - 1 < curSize) break;

		            firstByte = curSize;
                    sizeLeft -= writeSize;
                    writePos += writeSize;
                }
            }
        }

        // Misc
        private void CreatePieceProgress(int piece)
        {
            bool containsKey;
            lock (lockerTorrent) containsKey = torrent.data.pieceProgress.ContainsKey(piece);

            if (!containsKey)
            {
                PieceProgress pp = new PieceProgress(ref torrent.data, piece);
                torrent.data.pieceProgress.Add(piece, pp);
            } 
        }
        private int GetBlockSize(int piece, int block)
        {
            if (piece == torrent.data.pieces - 1 && block == torrent.data.blocksLastPiece - 1)
                return torrent.data.blockLastSize;
            else
                return torrent.data.blockSize;
        }
        private void Log(string msg) { if (Options.Verbosity > 0) log.Write($"[BitSwarm] {msg}"); }
    }
}