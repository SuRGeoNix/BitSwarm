using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;

using SuRGeoNix.BEP;
using static SuRGeoNix.BEP.Torrent.TorrentData;

namespace SuRGeoNix
{
    /* TODO (Draft Notes)
     * 
     * 1. [Jobs -> Peer] | TimeBeginPeriod(1) is too much, after that we could go in TimeBeginPeriod(>=5) or even remove it completely
     *  Try to avoid all the schedule jobs and transfer the functionality directly in the Peer.ProcessMessage()
     *      eg. Piece Request after Unchoked, PieceReceived, PieceRejected
     *          Similarly should handle possible Piece Cancel/Timeouts/KeepAlives/Interested/"Autokill/drop" etc.
     *          
     *      Additionally, check where possible to Dispatch New Peers when they arrive (fill from trackers/fill from dht etc.)
     *          Randomness on how you dispatch them? (to avoid upload limits and high cpu?)
     * 
     * 2. [Peers Lists]
     *  Αvoid large lists (ban peers, less re-fills)
     * 
     * 3. [Streaming] | Focus Points Requests Algorithm
     *  The problem: We need as soon as possible specific pieces so by requesting the same pieces/blocks from more than one peer,
     *               we ensure we will receive it faster but at the same time we will have more already received pieces/blocks => dropped bytes
     *               
     *               The right adjustment it is required for Piece Timeout, RequestBlocksPerPeer, Piece/Block Randomness
     *               
     * 4. [Threads]
     *  Transfer all the threads (especially .net threadpool) to BSTP
     *      Review also async & thread sleeps (try to replace them with better alternatives)
     *      
     * 5. [Sleep Mode]
     * 
     *  Consider using an automatic approach (eg. after X seconds of current session get [Max Down Rate - 700KB/s]
     *  
     * 6. +++ Allow Fast / PeX / uTP / Proxy / Seeding / Load-Save Session / Multiple-Instances
     */

    public class BitSwarm
    {
        #region BitSwarm's Thread Pool [Short/Long Run]
        public class BSTP
        {
            public static bool          Stop        { get; private set; }
            public static int           MaxThreads  { get; private set; }
            public static int           MinThreads  { get; private set; }
            public static int           Available   => MaxThreads- Running;
            public static int           Running;
            public static int           ShortRun    => Running   - LongRun;
            public static int           LongRun;

            private static BSTPThread[] Threads;
            private static readonly object lockerThreads = new object();
            public static  void Initialize(int MinThreads, int MaxThreads)
            {
                lock (lockerThreads)
                {
                    Dispose();

                    Stop            = false;
                    BSTP.MinThreads = MinThreads;
                    BSTP.MaxThreads = MaxThreads;
                    Running         = MaxThreads;
                    Threads         = new BSTPThread[MaxThreads];

                    for (int i=0; i<MaxThreads; i++)
                    {
                        StartThread(i);

                        if (i % 25 == 0) Thread.Sleep(25);
                    }
                }
            }
            public static  void SetMinThreads(int MinThreads)
            {
                //Console.WriteLine($"[BSTP] MinThreds changing to {MinThreads}");
                lock (lockerThreads) BSTP.MinThreads = MinThreads;
            }
            private static void StartThread(int i)
            {
                int cacheI = i;

                Threads[i]                      = new BSTPThread();
                Threads[i].thread               = new Thread(_ => { ThreadRun(cacheI); });
                Threads[i].thread.IsBackground  = true;
                Threads[i].thread.Start();
            }
            private static void ThreadRun(int index)
            {
                //Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Started");

                Interlocked.Decrement(ref Running);
                Threads[index].IsAlive  = true;

                while (!Stop)
                {
                    Threads[index].resetEvent.WaitOne();
                    if (Stop) break;

                    Threads[index].peer?.Run(Threads[index]);
                    if (Threads != null && Threads[index] != null) Threads[index].IsRunning = false;
                    Interlocked.Decrement(ref Running);
                }

                //Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Stopped");
            }

            public static  bool Dispatch(Peer peer)
            {
                lock (lockerThreads)
                {
                    if (Stop || Running >= MaxThreads || ShortRun >= MinThreads) return false;

                    foreach (var thread in Threads)
                        if (thread != null && thread.IsAlive && !thread.IsRunning)
                        {
                            if (Running >= MaxThreads || ShortRun >= MinThreads) return false;
                            peer.status = Peer.Status.CONNECTING;
                            thread.peer     = peer;
                            thread.IsRunning= true;
                            Interlocked.Increment(ref Running);
                            thread.resetEvent.Set();

                            return true;
                        }

                    return false;
                }
            }
            public static  void Dispose()
            {
                lock (lockerThreads)
                {
                    Stop = true;

                    if (Threads != null)
                    {
                        foreach (var thread in Threads)
                            thread?.resetEvent.Set();

                        int escape = 150;
                        while (Running == 0 && escape > 0) { Thread.Sleep(20); escape--; }
                    }

                    MinThreads  = 0;
                    MaxThreads  = 0;
                    Running     = 0;
                    Threads     = null;
                }
            }

            public class BSTPThread
            {
                public AutoResetEvent   resetEvent = new AutoResetEvent(false);
                public bool             isLongRun   { get; internal set; }
                public bool             IsRunning   { get; internal set; }
                public bool             IsAlive     { get; internal set; }

                public Thread           thread;
                public Peer             peer;
            }
        }
        #endregion


        #region Focus Points [For Streaming]
        public class FocusPoint
        {
            public FocusPoint(long id, int fromPiece, int toPiece) {  this.id = id; this.fromPiece = fromPiece; this.toPiece = toPiece; }
            public long id;
            public int  fromPiece;
            public int  toPiece;
            public bool isDone;
        }
        public Dictionary<long, FocusPoint> FocusPoints { get; private set; } = new Dictionary<long, FocusPoint>();
        public void CreateFocusPoint(FocusPoint fp) { Log($"[FOCUSPOINT] Creating Focus Point from {fp.fromPiece} to {fp.toPiece}"); lock (FocusPoints) if (!FocusPoints.ContainsKey(fp.id)) FocusPoints.Add(fp.id, fp); else FocusPoints[fp.id].toPiece = Math.Max(FocusPoints[fp.id].toPiece, fp.toPiece); }
        public void DeleteFocusPoint(long id)       { Log($"[FOCUSPOINT] Deleting Focus Point from {id}"); lock (FocusPoints) FocusPoints.Remove(id); }
        public void DeleteFocusPoints()             { Log($"[FOCUSPOINT] Deleting Focus Points"); lock (FocusPoints) FocusPoints.Clear(); }

        public FocusPoint FocusArea { get; set; }
        #endregion


        #region Structs | Enums
        public class DefaultOptions
        {
            public string   DownloadPath        { get; set; }   = Path.GetTempPath();
            //public string   TempPath            { get; set; }

            public int      MaxThreads          { get; set; } = 200;    // Max Total  Connection Threads  | Short-Run + Long-Run
            public int      MinThreads          { get; set; } =  20;    // Max New    Connection Threads  | Short-Run

            public int      BoostThreads        { get; set; } = 120;    // Max New    Connection Threads  | Boot Boost
            public int      BoostTime           { get; set; } =  20;    // Boot Boost Time (Ms)


            // -1: Auto | 0: Disabled | Auto will figure out SleepModeLimit from MaxRate
            public int      SleepModeLimit      { get; set; } =   0;     // Activates Sleep Mode (Low Resources) at the specify DownRate | DHT Stop, Re-Fills Stop (DHT/Trackers) & MinThreads Drop to MinThreads / 2

            //public int      DownloadLimit       { get; set; } = -1;
            //public int      UploadLimit         { get; set; }

            public int      ConnectionTimeout   { get; set; } = 400;
            public int      HandshakeTimeout    { get; set; } = 800;
            public int      MetadataTimeout     { get; set; } = 1600;
            public int      PieceTimeout        { get; set; } = 6666;

            public bool     EnableDHT           { get; set; } = true;
            public bool     EnableTrackers      { get; set; } = true;
            public int      PeersFromTracker    { get; set; } = -1;

            public int      RequestBlocksPerPeer{ get; set; } = 6;

            public int      Verbosity           { get; set; } = 0;   // 1 -> BitSwarm | DHT, 2 -> SavePiece | Peers | Trackers
            public bool     LogTracker          { get; set; } = false;
            public bool     LogPeer             { get; set; } = false;
            public bool     LogDHT              { get; set; } = false;
            public bool     LogStats            { get; set; } = false;

            public string   TrackersPath        { get; set; } = null;
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

            public long     StartTime           { get; set; }
            public long     CurrentTime         { get; set; }
            public long     EndTime             { get; set; }

            public bool     SleepMode           { get; set; }
            public bool     BoostMode           { get; set; }
            public bool     EndGameMode         { get; set; }
        }

        public enum SleepModeState
        {
            Automatic,
            Manual,
            Disabled
        }
        private enum Status
        {
            RUNNING     = 0,
            PAUSED      = 1,
            STOPPED     = 2
        }

        internal enum PeersStorage
        {
            DHT,
            TRACKERS,
            DHTNEW,
            TRACKERSNEW
        }
        #endregion

        #region Event Handlers
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
        #endregion

        #region Properties
        public DefaultOptions   Options;
        public StatsStructure   Stats;
        public bool             isRunning       => status == Status.RUNNING;
        public bool             isPaused        => status == Status.PAUSED;
        public bool             isStopped       => status == Status.STOPPED;
        #endregion

        #region Declaration
        // Lockers
        readonly object  lockerTorrent       = new object();
        readonly object  lockerMetadata      = new object();

        // Generators (Hash / Random)
        public  static SHA1             sha1                = new SHA1Managed();
        private static Random           rnd                 = new Random();
        private byte[]                  peerID;

        // Main [Torrent / Trackers / Peers / Options]
        public ConcurrentDictionary<string, Peer>  peers            {get; private set; } // InQueue  Peers
        public ConcurrentDictionary<string, int>   dhtPeers         {get; private set; } // DHT      Peers
        public ConcurrentDictionary<string, int>   trackersPeers    {get; private set; } // Tracker  Peers

        private Torrent                 torrent;

        private List<Tracker>           trackers;
        private Tracker.Options         trackerOpt;
        
        public  DHT                     dht;                            
        private DHT.Options             dhtOpt;

        private Logger                  log;
        private Logger                  logDHT;
        private Thread                  beggar;
        private Status                  status;

        private long                    metadataLastRequested;
        private long                    lastCheckOfTimeoutsAt;

        // More Stats
        private int                     curSecond           = 0;
        private int                     prevSecond          = 0;
        private long                    prevStatsTicks      = 0;
        private int                     pieceTimeouts       = 0;
        private int                     pieceRejected       = 0;
        private int                     pieceAlreadyRecv    = 0;
        private int                     sha1Fails           = 0;
        #endregion


        #region Constructors / Initializers / Setup / IncludeFiles
        public BitSwarm(DefaultOptions opt = null) { Options = (opt == null) ? new DefaultOptions() : opt; }
        public  void Initiliaze(string torrent)
        {
            Initiliaze();
            this.torrent.FillFromTorrentFile(torrent);
            Setup();
        }
        public  void Initiliaze(Uri magnetLink)
        {
            Initiliaze();
            torrent.FillFromMagnetLink(magnetLink);
            Setup();
        }
        private void Initiliaze()
        {
            peerID                          = new byte[20]; rnd.NextBytes(peerID);

            peers                           = new ConcurrentDictionary<string, Peer>();
            dhtPeers                        = new ConcurrentDictionary<string, int>();
            trackersPeers                   = new ConcurrentDictionary<string, int>();
            trackers                        = new List<Tracker>();

            torrent                         = new Torrent(Options.DownloadPath);
            torrent.metadata.progress       = new BitField(20); // Max Metadata Pieces
            torrent.metadata.pieces         = 2;                // Consider 2 Pieces Until The First Response
            torrent.metadata.parallelRequests= 8;               // How Many Peers We Will Ask In Parallel (firstPieceTries/2)

            log                             = new Logger(Path.Combine(Options.DownloadPath, "session" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"    ), true);
            if (Options.EnableDHT)
                logDHT                      = new Logger(Path.Combine(Options.DownloadPath, "session" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_DHT.log"), true);

            status                          = Status.STOPPED;
        }
        private void Setup()
        {
            // DHT
            if (Options.EnableDHT)
            {
                dhtOpt                      = DHT.GetDefaultOptions();
                dhtOpt.Beggar               = this;
                dhtOpt.LogFile              = logDHT;
                dhtOpt.Verbosity            = Options.LogDHT ? Options.Verbosity : 0;
                dht                         = new DHT(torrent.file.infoHash, dhtOpt);
            }

            // Tracker
            trackerOpt                      = new Tracker.Options();
            trackerOpt.PeerId               = peerID;
            trackerOpt.InfoHash             = torrent.file.infoHash;
            trackerOpt.ConnectTimeout       = Options.HandshakeTimeout;
            trackerOpt.ReceiveTimeout       = Options.HandshakeTimeout;
            Tracker.Beggar                  = this;

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

            // Fill from TorrentFile | MagnetLink + TrackersPath to Trackers
            torrent.FillTrackersFromTrackersPath(Options.TrackersPath);
            FillTrackersFromTorrent();

            // TODO: Local ISP SRV _bittorrent-tracker.<> http://bittorrent.org/beps/bep_0022.html

            // Metadata already done?
            if (  (torrent.file.length > 0      || (torrent.file.lengths != null && torrent.file.lengths.Count > 0))  
                && torrent.file.pieceLength > 0 && (torrent.file.pieces  != null && torrent.file.pieces. Count > 0))
                {  torrent.metadata.isDone = true;  MetadataReceived?.Invoke(this, new MetadataReceivedArgs(torrent)); }
        }
        
        public void IncludeFiles(List<string> includeFiles)
        {
            if (!torrent.metadata.isDone) return;

            BitField newProgress = new BitField(torrent.data.pieces);
            BitField newRequests = new BitField(torrent.data.pieces);
            newProgress.SetAll();
            newRequests.SetAll();

            lock (lockerTorrent)
            {
                long curDistance = 0;
                for (int i=0; i<torrent.file.paths.Count; i++)
                { 
                    bool isIncluded = false;

                    foreach (string file in includeFiles)
                    {
                        if (file == torrent.file.paths[i])
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

                    if (torrent.data.filesIncludes.Contains(torrent.file.paths[i]) && !isIncluded)
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
        #endregion


        #region Start / Pause / Dispose
        public void Start()
        {
            if (status == Status.RUNNING || (torrent.data.progress != null && torrent.data.progress.GetFirst0() == - 1)) return;
            
            status = Status.RUNNING;
            Stats.EndGameMode   = false;
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
            if (status == Status.PAUSED) return;

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
                    foreach (Peer peer in peers.Values)
                        peer.Disconnect();

                if (torrent != null) torrent.Dispose();
                if (logDHT  != null) logDHT. Dispose();
                if (log     != null) log.    Dispose();

                BSTP.Dispose();

            } catch (Exception) { }
        }
        #endregion

        #region Feeders [Torrent -> Trackers | Trackers -> Peers | DHT -> Peers | Client -> Stats]
        private void  StartTrackers()
        {
            for (int i=0; i<trackers.Count; i++)
            {
                trackers[i].Announce(Options.PeersFromTracker);
                if (i % 10 == 0) Thread.Sleep(25);
            }
        }
        internal void FillPeersFromStorage(ConcurrentDictionary<string, int> newPeers, PeersStorage storage)
        {
            bool storageNew = storage == PeersStorage.DHTNEW || storage == PeersStorage.TRACKERSNEW;
            int  countNew   = 0;

            foreach (KeyValuePair<string, int> peerKV in newPeers)
            {
                if (storageNew)
                {
                    if (dhtPeers.ContainsKey(peerKV.Key) || trackersPeers.ContainsKey(peerKV.Key))
                    {
                        if (storage == PeersStorage.TRACKERSNEW)
                            trackersPeers[peerKV.Key] = peerKV.Value;
                        else if (storage == PeersStorage.DHTNEW)
                            dhtPeers[peerKV.Key] = peerKV.Value;

                        continue;
                    }
                }

                if (storage == PeersStorage.TRACKERSNEW)
                    trackersPeers[peerKV.Key] = peerKV.Value;
                else if (storage == PeersStorage.DHTNEW)
                    dhtPeers[peerKV.Key] = peerKV.Value;

                if (!peers.ContainsKey($"{peerKV.Key}:{peerKV.Value}")) 
                    if (peers.TryAdd($"{peerKV.Key}:{peerKV.Value}", new Peer(peerKV.Key, peerKV.Value))) countNew++;
            }

            if (Options.Verbosity > 0 && countNew > 0) Log($"[{storage.ToString()}] {countNew} Adding Peers");
        }
        private void  FillTrackersFromTorrent()
        {
            foreach (Uri uri in torrent.file.trackers)
            {
                if (uri.Scheme.ToLower() == "http" || uri.Scheme.ToLower() == "https" || uri.Scheme.ToLower() == "udp")
                {
                    bool found = false;
                    foreach (var tracker in trackers)
                        if (tracker.uri.Scheme == uri.Scheme && tracker.uri.DnsSafeHost == uri.DnsSafeHost && tracker.uri.Port == uri.Port) { found = true; break; }
                    if (found) continue;

                    Log($"[Torrent] [Tracker] [ADD] {uri}");
                    trackers.Add(new Tracker(uri, trackerOpt));
                }
                else
                    Log($"[Torrent] [Tracker] [ADD] {uri} Protocol not implemented");
            }
        }
        private void  FillStats()
        {
            // Stats
            double secondsDiff          = ((Stats.CurrentTime - prevStatsTicks) / 10000000.0); // For more accurancy
            long totalBytesDownloaded   = Stats.BytesDownloaded + Stats.BytesDropped; // Included or Not?
            Stats.DownRate              = (int) ((totalBytesDownloaded - Stats.BytesDownloadedPrev) / secondsDiff); // Change this (2 seconds) if you change scheduler
            Stats.AvgRate               = (int) ( totalBytesDownloaded / curSecond);
            
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
                    Stats.ETA   = (int) ( (torrent.data.totalSize - Stats.BytesDownloaded) / ((Stats.BytesDownloaded - Stats.BytesDownloadedPrev) / secondsDiff) );

                if (Stats.BytesDownloaded  == 0)
                    Stats.AvgETA*= 2; // Kind of infinite
                else
                    if (curSecond > 0) Stats.AvgETA = (int) ( (torrent.data.totalSize - Stats.BytesDownloaded) / (Stats.BytesDownloaded / curSecond ) );
            }

            if (Stats.DownRate > Stats.MaxRate) Stats.MaxRate = Stats.DownRate;

            Stats.BytesDownloadedPrev   = totalBytesDownloaded;
            prevStatsTicks              = Stats.CurrentTime;

            // Stats & Clean-up
            Stats.PeersTotal        = peers.Count;
            Stats.PeersInQueue      = 0;
            Stats.PeersChoked       = 0;
            Stats.PeersConnected    = 0;
            Stats.PeersConnecting   = 0;
            Stats.PeersDownloading  = 0;
            Stats.PeersDropped      = 0;
            Stats.PeersFailed1      = 0;
            Stats.PeersFailed2      = 0;
            Stats.PeersUnChoked     = 0;

            List<string> peerKeys = new List<string>();

            long notHaveTimeout = (long)10000 * (2 * (Options.HandshakeTimeout + Options.ConnectionTimeout));

            foreach (KeyValuePair<string,Peer> peerKV in peers)
            {
                Peer peer = peerKV.Value;

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
                        peer.Disconnect(); Stats.PeersDropped++; peerKeys.Add(peerKV.Key);

                        break;
                    case Peer.Status.FAILED2:
                        Stats.PeersFailed2++;
                        peer.Disconnect(); Stats.PeersDropped++; peerKeys.Add(peerKV.Key);

                        break;
                    case Peer.Status.READY:

                        // TODO: Check from last actually received piece (sometimes could be in Downloading status and do nothing)

                        // Drop No Our Pieces Peers (Ensure handshake finished + bitfield retrieved)
                        if (!Stats.SleepMode && !peer.stageYou.haveAll && (Stats.CurrentTime - peer.connectedAt) > notHaveTimeout && (peer.stageYou.haveNone || peer.stageYou.bitfield == null || torrent.data.requests.GetFirst01(peer.stageYou.bitfield) == -1))
                        {
                            if (Options.Verbosity > 0) Log($"[DROP] No Pieces Peer {peer.host}");
                            Stats.PeersFailed2++;
                            peer.Disconnect(); Stats.PeersDropped++; peerKeys.Add(peerKV.Key);
                        }

                        // Drop Chocked Peers (after 30 seconds)
                        else if (!peer.stageYou.unchoked)
                        {
                            if (!Stats.SleepMode && Stats.CurrentTime - peer.chokedAt > 30 * 1000 * 10000)
                            {
                                if (Options.Verbosity > 0) Log($"[DROP] Choked Peer {peer.host}");
                                Stats.PeersFailed2++;
                                peer.Disconnect(); Stats.PeersDropped++; peerKeys.Add(peerKV.Key);
                            }
                            else
                            {
                                // Interested as Keep Alive (after 7 seconds)
                                if ((Stats.CurrentTime - peer.lastAction) / 10000 > 7000)
                                {
                                    if (Options.Verbosity > 0) peer.Log(4, "[MSG ] Sending Interested");
                                    peer.SendMessage(Peer.Messages.INTRESTED, false, null);
                                }
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

                        // Drop DownLimit Peer (On-Demand)
                        //if (disconnectPeer)
                        //{
                        //    if (Options.Verbosity > 0) Log($"[DROP] DownLimit Peer {peer.host}");
                        //    disconnectPeer = false;
                        //    Stats.PeersFailed2++;
                        //    peer.Disconnect(); Stats.PeersDropped++; peerKeys.Add(peerKV.Key);
                        //}
                        //else
                        Stats.PeersDownloading++;

                        break;
                }
            }

            foreach (string peerKey in peerKeys)
                { Peer tmp; peers.TryRemove(peerKey, out tmp); }

            // Stats -> UI
            StatsUpdated?.Invoke(this, new StatsUpdatedArgs(Stats));

            // Stats -> Log
            if (Options.LogStats)
            {
                Log($"[STATS] [INQUEUE: {String.Format("{0,3}",Stats.PeersInQueue)}]\t[DROPPED: {String.Format("{0,3}",Stats.PeersDropped)}]\t[CONNECTING: {String.Format("{0,3}",Stats.PeersConnecting)}]\t[FAIL1: {String.Format("{0,3}",Stats.PeersFailed1)}]\t[FAIL2: {String.Format("{0,3}",Stats.PeersFailed2)}]\t[READY: {String.Format("{0,3}",Stats.PeersConnected)}]\t[CHOKED: {String.Format("{0,3}",Stats.PeersChoked)}]\t[UNCHOKED: {String.Format("{0,3}",Stats.PeersUnChoked)}]\t[DOWNLOADING: {String.Format("{0,3}",Stats.PeersDownloading)}]");
                Log($"[STATS] [CUR MAX: {String.Format("{0:n0}", (Stats.MaxRate / 1024)) + " KB/s"}]\t[DOWN CUR: {String.Format("{0:n0}", (Stats.DownRate / 1024)) + " KB/s"}]\t[DOWN AVG: {String.Format("{0:n0}", (Stats.AvgRate / 1024)) + " KB/s"}]\t[ETA CUR: {TimeSpan.FromSeconds(Stats.ETA).ToString(@"hh\:mm\:ss")}]\t[ETA AVG: {TimeSpan.FromSeconds(Stats.AvgETA).ToString(@"hh\:mm\:ss")}]\t[ETA R: {TimeSpan.FromSeconds((Stats.ETA + Stats.AvgETA)/2).ToString(@"hh\:mm\:ss")}]");
                Log($"[STATS] [TIMEOUTS: {String.Format("{0,4}",pieceTimeouts)}]\t[ALREADYRECV: {String.Format("{0,3}",pieceAlreadyRecv)}]\t[REJECTED: {String.Format("{0,3}",pieceRejected)}]\t[SHA1FAILS:{String.Format("{0,3}",sha1Fails)}]\t[DROPPED BYTES: {Utils.BytesToReadableString(Stats.BytesDropped)}]\t[DHT: {dht?.status}]\t[DHTPEERS: {dhtPeers.Count}]\t[TRACKERSPEERS: {trackersPeers.Count}]\t[SLEEPMODE: {Stats.SleepMode}]");
                Log($"[STATS] [PROGRESS PIECES: {torrent.data.progress.setsCounter}/{torrent.data.progress.size} | REQ: {torrent.data.requests.setsCounter}]\t[PROGRESS BYTES: {Stats.BytesDownloaded}/{torrent.data.totalSize}]\t[Pieces/Blocks: {torrent.data.pieces}/{torrent.data.blocks}]\t[Piece/Block Length: {torrent.data.pieceSize}|{torrent.data.totalSize % torrent.data.pieceSize}/{torrent.data.blockSize}|{torrent.data.blockLastSize}][Working Pieces: {torrent.data.pieceProgress.Count}]");
            }
        }
        #endregion


        #region ******** BEGGAR *********
        private void Beggar()
        {
            try
            {
                if (Utils.IsWindows) Utils.TimeBeginPeriod(5);
                log.RestartTime();

                curSecond               = 0;
                prevSecond              = 0;
                Stats.MaxRate           = 0;
                Stats.StartTime         = DateTime.UtcNow.Ticks;
                Stats.CurrentTime       = Stats.StartTime;
                prevStatsTicks          = Stats.StartTime;
                metadataLastRequested   = -1;
                bool isAutoSleepMode    = Options.SleepModeLimit == -1;
                int curDispatches       = 0;

                // Peers Clean-up & Re-Fills
                foreach (Peer peer in peers.Values)
                    peers[$"{peer.host}:{peer.port}"] = new Peer(peer.host, peer.port);

                if (Options.EnableTrackers)
                {
                    FillPeersFromStorage(dhtPeers, PeersStorage.TRACKERS);
                    trackersPeers.Clear();
                    StartTrackers();
                }

                if (Options.EnableDHT)
                {
                    logDHT.RestartTime(); 
                    FillPeersFromStorage(dhtPeers, PeersStorage.DHT);
                    dhtPeers.Clear();
                    dht.Start();
                }

                BSTP.Initialize(Math.Max(Options.MinThreads, Options.BoostThreads), Options.MaxThreads);
                if (Options.BoostThreads > Options.MinThreads) { Stats.BoostMode = true; if (Options.Verbosity > 0) Log($"[MODE] Boost Activated"); }

                // --------------- Main Loop ---------------
                if (Options.Verbosity > 0) Log("[BEGGAR  ] " + status);
                //int sleepMs = Math.Max(350, Options.ConnectionTimeout / 3);

                while (status == Status.RUNNING)
                {
                    // Every Loop [Dispatches | RequestPiece] - Review parallel dispatches curDispatches < (BSTP.MinThreads / 5) + 1
                    //if (!Stats.SleepMode)
                    //{
                        curDispatches = 0;

                        foreach (Peer peer in peers.Values)
                        {
                            if (status != Status.RUNNING) break;

                            if (peer.status == Peer.Status.READY)
                                RequestPiece(peer, true);
                            else if (peer.status == Peer.Status.NEW)
                                if (curDispatches < (BSTP.MinThreads / 5) + 1 && BSTP.Dispatch(peer)) curDispatches++;
                        }
                        //Console.WriteLine(curDispatches);
                    //}

                    // Scheduler

                        // Review Timeouts (especially during boot | possible dont use timeouts at all?)
                    // Every 1350ms | Check Request Timeouts (Torrent) | Will be replaces by Peers Embedded
                    if (torrent.metadata.isDone && Stats.CurrentTime - lastCheckOfTimeoutsAt > 1350 * 10000) CheckRequestTimeouts();

                    // Every Second
                    if (curSecond != prevSecond && curSecond > 1)
                    {
                        prevSecond = curSecond;

                        if (!torrent.metadata.isDone)
                        {
                            // Every 1 Second [Check Request Timeouts (Metadata)]
                            if (metadataLastRequested != -1 && Stats.CurrentTime - metadataLastRequested > Options.MetadataTimeout * 10000) { torrent.metadata.parallelRequests += 2; Log($"[REQ ] [M] Timeout"); }
                        }
                        else
                        {
                            // Every 1 Second [Check Boost Mode]

                            //Console.WriteLine($"[THREADS: {BSTP.MinThreads}/{BSTP.MaxThreads}]\t[RUNNING: {BSTP.Running} ({BSTP.ShortRun}/{BSTP.LongRun})]\t[AVAILABLE: {BSTP.Available}]");

                            // [Boost Mode] (Activate only once?)
                            if (Stats.BoostMode)
                            {
                                bool prevBoostMode  = Stats.BoostMode;
                                Stats.BoostMode     = curSecond <= Options.BoostTime;
                                if (Stats.BoostMode!= prevBoostMode)
                                {
                                    if (Options.Verbosity > 0) Log("[MODE] Boost" + (Stats.BoostMode ? "On" : "Off"));
                                    if (!Stats.BoostMode) BSTP.SetMinThreads(Options.MinThreads);
                                }
                            }

                            // Every 2 Second [Stats, Clean-up, EndGame Mode, Sleep Mode + todo DownLimit]
                            if (curSecond % 2 == 0)
                            {
                                // [Stats, Clean-up] + [Keep Alive->Interested in FillStats]
                                FillStats();

                                // [EndGame Mode]
                                if (!Stats.EndGameMode && torrent.data.pieces > 0 &&  (torrent.data.pieces - torrent.data.progress.setsCounter) * torrent.data.blocks < 300)
                                {
                                    if (Options.Verbosity > 0) Log($"[MODE] End Game On");
                                    Stats.SleepMode     = false;
                                    Stats.EndGameMode   = true;
                                }

                                // [Sleep Mode Auto = MaxRate x 3/4]
                                if (isAutoSleepMode && curSecond > 30 && Stats.MaxRate > 0)
                                {
                                    int curLimit = ((Stats.MaxRate * 3)/4) / 1024;
                                    if (Options.SleepModeLimit != curLimit)
                                    {
                                        Options.SleepModeLimit = curLimit;
                                        if (Options.Verbosity > 0) Log($"[MODE] Sleep - New Limit at {Options.SleepModeLimit}");
                                    }
                                }
                                
                                // [Sleep Mode On/Off] | MinThreads/DHT/Trackers | Offset -+ 200 to avoid On/Off very often
                                if (Options.SleepModeLimit > 0 && !Stats.BoostMode && !Stats.EndGameMode)
                                {
                                    bool prevSleepMode  = Stats.SleepMode;
                                    Stats.SleepMode     = Stats.DownRate > 0 && Stats.DownRate / 1024 > Options.SleepModeLimit;

                                    if (Stats.SleepMode != prevSleepMode)
                                    {
                                        if (!prevSleepMode)
                                            Stats.SleepMode = Stats.DownRate > 0 && Stats.DownRate / 1024 > Options.SleepModeLimit + 200;
                                        else
                                            Stats.SleepMode = Stats.DownRate > 0 && Stats.DownRate / 1024 > Options.SleepModeLimit - 200;
                                    }

                                    if (Stats.SleepMode != prevSleepMode)
                                    {
                                        if (Options.Verbosity > 0) Log("[MODE] Sleep" + (Stats.SleepMode ? "On" : "Off"));

                                        if (Stats.SleepMode)
                                        {
                                            if (Options.EnableDHT)
                                            {
                                                if (Options.Verbosity > 0)
                                                {
                                                    if (Options.EnableDHT)      Log($"[Trackers] Re-fill (SleepMode On) {trackersPeers.Count}");
                                                    if (Options.EnableTrackers) Log("[REFILL] From DHT (SleepMode On)");
                                                    Log("[DHT] Stopping (SleepMode)");
                                                }
                                                if (Options.EnableDHT)      FillPeersFromStorage(dhtPeers, PeersStorage.DHT);
                                                if (Options.EnableTrackers) FillPeersFromStorage(dhtPeers, PeersStorage.TRACKERS);
                                                dht.Stop();
                                            }
                                            BSTP.SetMinThreads(Options.MinThreads / 2); // If we enable dispatching in every loop
                                        }
                                        else
                                        {
                                            BSTP.SetMinThreads(Options.MinThreads); // If we enable dispatching in every loop

                                            if (Options.EnableTrackers && Stats.PeersInQueue < 100)
                                            {
                                                if (Options.Verbosity > 0) Log($"[Trackers] Re-fill (SleepMode Off) {trackersPeers.Count}");
                                                FillPeersFromStorage(dhtPeers, PeersStorage.TRACKERS);
                                            }

                                            if (Options.EnableDHT)
                                            {
                                                if (Options.Verbosity > 0)
                                                {
                                                    Log("[DHT] Restarting (SleepMode Off)");
                                                    Log("[REFILL] From DHT (SleepMode Off)");
                                                }
                                                if (Stats.PeersInQueue < 100) FillPeersFromStorage(dhtPeers, PeersStorage.DHT);
                                                dht.Start();
                                            }
                                        }
                                    }
                                }

                                #region DownLimit TODO
                                // Check Downlimit (messy algo - until we have stats from peers)
                                //if (Stats.DownRate > 0 && Stats.DownRate / 1024 > Options.DownloadLimit)
                                //{
                                //    disconnectPeer = true;
                                //    Options.RequestBlocksPerPeer = 3;
                                //    //Console.WriteLine("Diff: " + ((Stats.DownRate / 1024) - Options.DownloadLimit) + " | Down: " + Stats.PeersDownloading + " | Blocks: " + Options.RequestBlocksPerPeer);
                                //}
                                //else
                                //{
                                //    Options.RequestBlocksPerPeer++;

                                //    if (Options.RequestBlocksPerPeer > 5)
                                //        Options.RequestBlocksPerPeer = 6;
                                //}
                                #endregion

                            } // Every 2 Seconds

                        } // !Metadata Received
                            
                        // Every 3 Seconds  [Check DHT Stop/Start - 40 seconds]
                        if (curSecond % 3 == 0)
                        {
                            if (Options.EnableDHT)
                            {
                                if (dht.status == DHT.Status.RUNNING && !Stats.BoostMode && !Stats.EndGameMode && Stats.CurrentTime - dht.StartedAt > 40 * 1000 * 10000)
                                {
                                    if (Options.Verbosity > 0) Log("[DHT] Stopping (40 secs of running)");
                                    dht.Stop();
                                }
                                else if (dht.status == DHT.Status.STOPPED && !Stats.SleepMode && Stats.PeersInQueue < 100 && Stats.CurrentTime - dht.StoppedAt > 40 * 1000 * 10000)
                                {
                                    if (Options.Verbosity > 0) Log("[DHT] Restarting (40 secs of idle)");
                                    dht.Start();
                                }
                            }
                        }

                        // Every 9 Seconds  [Refill from DHT]
                        if (curSecond % 9 == 0)
                        {
                            if (Options.EnableDHT && !Stats.SleepMode && Stats.PeersInQueue < 100)
                            {
                                if (Options.Verbosity > 0) Log($"[DHT] Re-fill {dhtPeers.Count}");
                                FillPeersFromStorage(dhtPeers, PeersStorage.DHT);
                            }
                        }

                        // Every 22 Seconds [Refill from Trackers]
                        if (curSecond % 15 == 0)
                        {
                            if (Options.EnableTrackers && !Stats.SleepMode && Stats.PeersInQueue < 100)
                            {
                                if (Options.Verbosity > 0) Log($"[Trackers] Re-fill {trackersPeers.Count}");
                                FillPeersFromStorage(dhtPeers, PeersStorage.TRACKERS);
                            }
                        }

                        // Every 31 Seconds [Re-request Trackers]
                        if (curSecond % 31 == 0)
                        {
                            if (Options.EnableTrackers)// && ((!Stats.SleepMode && Stats.PeersInQueue < 100) || Stats.BoostMode))
                            {
                                if (Options.Verbosity > 0) Log("[Trackers] Re-requesting");
                                StartTrackers();
                            }
                        }

                    } // Scheduler [Every Second]

                    Thread.Sleep(100 + curDispatches);

                    Stats.CurrentTime = DateTime.UtcNow.Ticks;

                    if (Stats.CurrentTime - (Stats.StartTime + (curSecond * (long)10000000)) > 0) curSecond++;

                } // While

                Stats.EndTime = Stats.CurrentTime;

                if (Options.EnableDHT) dht.Stop();

                foreach (Peer peer in peers.Values)
                    peer.Disconnect();

                //if (torrent.metadata.isDone) FillStats(); Will not be accurate at the end

                BSTP.Dispose();

                Log("[BEGGAR  ] " + status);

            } catch (ThreadAbortException) {
            } catch (Exception e) { Log($"[BEGGAR] Beggar(), Msg: {e.Message}\r\n{e.StackTrace}"); StatusChanged?.Invoke(this, new StatusChangedArgs(2, e.Message + "\r\n"+ e.StackTrace)); }

            if (Utils.IsWindows) Utils.TimeEndPeriod(5);
        }
        #endregion


        #region Piece/Block [Timeout | Request | Receive | Reject | Save]

        // PieceBlock [Request Metadata/Torrent] | EndGame/FocusPoint/Normal
        internal void RequestPiece(Peer peer, bool imBeggar = false)
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

                    int piece2 = (piece + 1) >= torrent.metadata.progress.size ? -1 : torrent.metadata.progress.GetFirst0(piece + 1);

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
            
            if (peer.stageYou.haveNone || (!peer.stageYou.haveAll && peer.stageYou.bitfield == null)) return;

            if (!peer.stageYou.unchoked)
            {
                if (imBeggar && peer.allowFastPieces.Count > 0 && FocusPoints.Count == 0) RequestFastPiece(peer);
                return;
            }

            List<Tuple<int, int, int>> requests = new List<Tuple<int, int, int>>();

            if (Stats.EndGameMode)
            {
                // Find [piece, block] combination of the last pieces
                List<int> piecesLeft;
                if (peer.stageYou.haveAll)
                    lock (lockerTorrent) piecesLeft = torrent.data.progress.GetAll0();
                else
                    lock (lockerTorrent) piecesLeft = torrent.data.progress.GetAll0(peer.stageYou.bitfield);

                if (piecesLeft.Count == 0) { peer.Disconnect(); return; }

                List<Tuple<int, int>> piecesBlocksLeft = new List<Tuple<int, int>>();

                foreach (int pieceLeft in piecesLeft)
                {
                    CreatePieceProgress(pieceLeft);
                    List<int> pieceBlocksLeft = torrent.data.pieceProgress[pieceLeft].progress.GetAll0();
                    foreach (int blockLeft in pieceBlocksLeft)
                        piecesBlocksLeft.Add(new Tuple<int, int>(pieceLeft, blockLeft));
                }

                // Choose Randomly | Review Randomness (probably an issue here)
                int requestsCounter = Math.Min(Options.RequestBlocksPerPeer, piecesBlocksLeft.Count);
                for (int i=0; i<requestsCounter; i++)
                {
                    int curPieceBlock = rnd.Next(0, piecesBlocksLeft.Count);

                    piece = piecesBlocksLeft[curPieceBlock].Item1;
                    block = piecesBlocksLeft[curPieceBlock].Item2;
                    blockSize = GetBlockSize(piece, block);

                    if (Options.Verbosity > 1) Log($"[{peer.host.PadRight(15, ' ')}] [REQE][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                    requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                    lock (lockerTorrent) torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize));

                    piecesBlocksLeft.RemoveAt(curPieceBlock);
                }

                if (requests.Count > 0) { peer.RequestPiece(requests); Thread.Sleep(15); } // Avoid Reaching Upload Limits

                return;
            }

            lock (lockerTorrent)
            { 
                bool fpDone = false;
                for (int i=0; i<Options.RequestBlocksPerPeer; i++)
                {
                    piece           = -1;
                    block           = -1;
                    FocusPoint fp   = null;

                    // Focus points
                    if (!fpDone)
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



                            // ------------ TODO New Approach --------

                            //piece = firstPiece;

                            //CreatePieceProgress(piece);
                            //List<int> pieceBlocksLeft = torrent.data.pieceProgress[piece].progress.GetAll0();

                            //int requestsCounter = Math.Min(Options.RequestBlocksPerPeer, pieceBlocksLeft.Count);

                            //for (int l=0;l<requestsCounter; l++)
                            //{
                            //    int curPieceBlock = rnd.Next(0, pieceBlocksLeft.Count);

                            //    block = pieceBlocksLeft[curPieceBlock];
                            //    blockSize = GetBlockSize(piece, block);

                            //    if (Options.Verbosity > 0) Log($"[{peer.host.PadRight(15, ' ')}] [REQF][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                            //    requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                            //    torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize, true));

                            //    pieceBlocksLeft.RemoveAt(curPieceBlock);
                            //}


                            // --------------------

                            
                            piecesLeft.Add(firstPiece);

                            List<Tuple<int, int>> piecesBlocksLeft = new List<Tuple<int, int>>();
                            foreach (int pieceLeft in piecesLeft)
                            {
                                CreatePieceProgress(pieceLeft);
                                List<int> pieceBlocksLeft = torrent.data.pieceProgress[pieceLeft].progress.GetAll0();
                                foreach (int blockLeft in pieceBlocksLeft)
                                    piecesBlocksLeft.Add(new Tuple<int, int>(pieceLeft, blockLeft));
                            }

                            // Piece | Block Left choose Random | Review Randomness (probably an issue here)
                            int requestsCounter = Math.Min(Options.RequestBlocksPerPeer, piecesBlocksLeft.Count);
                            for (int l=0;l<requestsCounter; l++)
                            {
                                int curPieceBlock = rnd.Next(0, piecesBlocksLeft.Count);

                                piece = piecesBlocksLeft[curPieceBlock].Item1;
                                block = piecesBlocksLeft[curPieceBlock].Item2;
                                blockSize = GetBlockSize(piece, block);

                                if (Options.Verbosity > 0) Log($"[{peer.host.PadRight(15, ' ')}] [REQF][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                                requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                                torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize, true));

                                piecesBlocksLeft.RemoveAt(curPieceBlock);
                            }

                            

                            //if (requests.Count == Options.RequestBlocksPerPeer) { peer.RequestPiece(requests); Thread.Sleep(10); return; } // Avoid Reaching Upload Limits
                            if (requests.Count == Options.RequestBlocksPerPeer) break;

                            // Let it fill the rest requests
                            fpDone = true;
                            i += requests.Count;
                            piece = -1;
                            block = -1;
                        }   
                    }

                    // Normal Requests
                    if (peer.stageYou.haveAll)
                        piece = torrent.data.requests.GetFirst0();
                    else
                        piece = torrent.data.requests.GetFirst01(peer.stageYou.bitfield);

                    if (piece < 0) break;

                    CreatePieceProgress(piece);
                    block = torrent.data.pieceProgress[piece].requests.GetFirst0();
                    if (block < 0) { Log($"Shouldn't be here! Piece: {piece}"); break; }

                    torrent.data.pieceProgress[piece].requests.SetBit(block);
                    if (torrent.data.pieceProgress[piece].requests.GetFirst0() == -1) SetRequestsBit(piece);

                    blockSize = GetBlockSize(piece, block);

                    if (Options.Verbosity > 1) Log($"[{peer.host.PadRight(15, ' ')}] [REQ ][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                    // TODO: Requests Timeouts should be related to <i> | 0 1 2 3 4 5 Requests | i * (timeout/15) ? 
                    requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                    torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize));
                }
            }

            if (requests.Count > 0)
            {
                peer.RequestPiece(requests);
                //if (Stats.SleepMode) Thread.Sleep(25);
            }
        }
        internal void RequestFastPiece(Peer peer)
        {
            if (!torrent.metadata.isDone || peer.stageYou.haveNone) return; // TODO: Probably should also verify that they have the piece?

            List<Tuple<int, int, int>> requests = new List<Tuple<int, int, int>>();

            int piece = -1;
            int block, blockSize;

            lock (lockerTorrent)
            {
                for (int i=0; i<Options.RequestBlocksPerPeer; i++)
                {
                    for (int l=peer.allowFastPieces.Count-1; l>=0; l--)
                    {
                        if (torrent.data.requests.GetBit(peer.allowFastPieces[l]))
                            peer.allowFastPieces.RemoveAt(l);
                        else
                            { piece = peer.allowFastPieces[l];  break; }
                    }

                    if (peer.allowFastPieces.Count == 0) break;

                    CreatePieceProgress(piece);
                    block = torrent.data.pieceProgress[piece].requests.GetFirst0();
                    if (block < 0) { Log($"Shouldn't be here! Piece: {piece}"); break; }

                    torrent.data.pieceProgress[piece].requests.SetBit(block);
                    if (torrent.data.pieceProgress[piece].requests.GetFirst0() == -1) SetRequestsBit(piece);

                    blockSize = GetBlockSize(piece, block);

                    if (Options.Verbosity > 1) Log($"[{peer.host.PadRight(15, ' ')}] [REQA][P]\tPiece: {piece} Block: {block} Offset: {block * torrent.data.blockSize} Size: {blockSize} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");

                    // TODO: Requests Timeouts should be related to <i> | 0 1 2 3 4 5 Requests | i * (timeout/15) ? 
                    requests.Add(new Tuple<int, int, int>(piece, block * torrent.data.blockSize, blockSize));
                    torrent.data.pieceRequests.Add(new PieceRequest(DateTime.UtcNow.Ticks, peer, piece, block, blockSize));
                }
            }

            if (requests.Count > 0)
            {
                peer.RequestPiece(requests);
                if (Stats.SleepMode) Thread.Sleep(25);
            }
        }

        // PieceBlock [Metadata Receive | Reject]
        internal void MetadataPieceReceived(byte[] data, int piece, int offset, int totalSize, Peer peer)
        {
            Log($"[{peer.host.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Offset: {offset} Size: {totalSize}");

            lock (lockerMetadata)
            {
                torrent.metadata.parallelRequests  += 2;
                peer.stageYou.metadataRequested     = false;

                if (torrent.metadata.totalSize == 0)
                {
                    torrent.metadata.totalSize  = totalSize;
                    torrent.metadata.progress   = new BitField((totalSize/Peer.MAX_DATA_SIZE) + 1);
                    torrent.metadata.pieces     = (totalSize/Peer.MAX_DATA_SIZE) + 1;
                    if (torrent.file.name != null) 
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
                    torrent.metadata.file.Dispose();
                    torrent.FillFromMetadata();
                    Peer.Options.Pieces = torrent.data.pieces;
                    MetadataReceived?.Invoke(this, new MetadataReceivedArgs(torrent));

                    //if (Options.EnableDHT) logDHT.RestartTime();
                    //log.RestartTime();
                    //curSeconds = 0;
                    torrent.metadata.isDone = true;
                }
            }
        }
        internal void MetadataPieceRejected(int piece, string src)
        {
            torrent.metadata.parallelRequests += 2;
            Log($"[{src.PadRight(15, ' ')}] [RECV][M]\tPiece: {piece} Rejected");
        }

        // PieceBlock [Torrent  Receive | Reject] 
        internal void PieceReceived(byte[] data, int piece, int offset, Peer peer)
        {
            // [Already Received | SHA-1 Validation Failed] => leave
            int  block = offset / torrent.data.blockSize;
            bool containsKey;
            bool pieceProgress;
            bool blockProgress;

            lock (lockerTorrent)
            {   
                containsKey     = torrent.data.pieceProgress.ContainsKey(piece);
                pieceProgress   = torrent.data.progress.GetBit(piece);
                blockProgress   = containsKey ? torrent.data.pieceProgress[piece].progress.GetBit(block) : false;
    
                // Piece Done | Block Done
                if (   (!containsKey && pieceProgress )     // Piece Done
                    || ( containsKey && blockProgress ) )   // Block Done
                { 
                    Stats.BytesDropped += data.Length; 
                    pieceAlreadyRecv++;
                    if (Options.Verbosity > 0) Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Block: {block} Offset: {offset} Size: {data.Length} Already received"); 

                    return; 
                }

                // Cancel Received Piece From Downloaders | Possible not worth the cpu (few peers will actually cancel them)
                //foreach (var downpeer in peers)
                //    if (downpeer.status == Peer.Status.DOWNLOADING && downpeer.host != peer.host)
                //    {
                //        foreach (var downpiece in downpeer.lastPieces)
                //            if (downpiece.Item1 == piece && downpiece.Item2 == offset) { downpeer.CancelPieces(downpiece.Item1, downpiece.Item2, downpiece.Item3); break; }
                //    }

                if (Options.Verbosity > 1) Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Block: {block} Offset: {offset} Size: {data.Length} Requests: {peer.PiecesRequested} Timeouts: {peer.PiecesTimeout}");
                Stats.BytesDownloaded += data.Length;

                // Parse Block Data to Piece Data
                Buffer.BlockCopy(data, 0, torrent.data.pieceProgress[piece].data, offset, data.Length);

                // SetBit Block | Leave if more blocks required for Piece
                torrent.data.pieceProgress[piece].progress.     SetBit(block);

                // In case of Timed out (to avoid re-requesting)
                torrent.data.pieceProgress[piece].requests.     SetBit(block);
                if (torrent.data.pieceProgress[piece].requests. GetFirst0() == -1) SetRequestsBit(piece);

                if (torrent.data.pieceProgress[piece].progress. GetFirst0() != -1) return;

                // SHA-1 Validation for Piece Data | Failed? -> Re-request whole Piece! | Lock, No thread-safe!
                byte[] pieceHash;
                pieceHash = sha1.ComputeHash(torrent.data.pieceProgress[piece].data);
                if (!Utils.ArrayComp(torrent.file.pieces[piece], pieceHash))
                {
                    Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Block: {block} Offset: {offset} Size: {data.Length} Size2: {torrent.data.pieceProgress[piece].data.Length} SHA-1 validation failed");

                    Stats.BytesDropped += torrent.data.pieceProgress[piece].data.Length;
                    Stats.BytesDownloaded -= torrent.data.pieceProgress[piece].data.Length;
                    sha1Fails++;

                    UnSetRequestsBit(piece);
                    torrent.data.pieceProgress.Remove(piece);

                    return;
                }

                // Save Piece in PartFiles [Thread-safe?]
                SavePiece(torrent.data.pieceProgress[piece].data, piece, torrent.data.pieceProgress[piece].data.Length);
                if (Options.Verbosity > 0) Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P]\tPiece: {piece} Full"); 

                // [SetBit for Progress | Remove Block Progress] | Done => CreateFiles
                SetProgressBit(piece);
                SetRequestsBit(piece);
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
            if (Options.Verbosity > 0) Log($"[{peer.host.PadRight(15, ' ')}] [RECV][P][REJECTED]\tPiece: {piece} Size: {size} Block: {block} Offset: {offset}");
            
            lock (lockerTorrent)
            {
                bool containsKey = torrent.data.pieceProgress.ContainsKey(piece);

                // !(Piece Done | Block Done)
                if ( !(( !containsKey && torrent.data.progress.GetBit(piece) ) 
                    || (  containsKey && torrent.data.pieceProgress[piece].progress.GetBit(block))) )
                { 
                    if (containsKey) torrent.data.pieceProgress[piece].requests.UnSetBit(block);
                    UnSetRequestsBit(piece);
                }

                //if (!peer.stageYou.unchoked) peer.PiecesTimeout--; // Avoid dropping peer
            }

            pieceRejected++;
        }

        // PieceBlock [Timeouts]
        private void CheckRequestTimeouts()
        {
            lock (lockerTorrent)
            {
                long curTimeout = Options.PieceTimeout * 10000;
                string lastPeer = "";

                for (int i=torrent.data.pieceRequests.Count-1; i>=0; i--)
                {
                    if (Stats.CurrentTime - torrent.data.pieceRequests[i].timestamp > curTimeout)
                    {
                        // !(Piece Done | Block Done)
                        bool containsKey = torrent.data.pieceProgress.ContainsKey(torrent.data.pieceRequests[i].piece);
                        if ( !(( !containsKey && torrent.data.progress.GetBit(torrent.data.pieceRequests[i].piece)) 
                             ||(  containsKey && torrent.data.pieceProgress[torrent.data.pieceRequests[i].piece].progress.GetBit(torrent.data.pieceRequests[i].block))) )
                        {
                            torrent.data.pieceRequests[i].peer.PiecesTimeout++;
                            if (Options.Verbosity > 0) Log($"[{torrent.data.pieceRequests[i].peer.host.PadRight(15, ' ')}] [REQT][P]\tPiece: {torrent.data.pieceRequests[i].piece} Block: {torrent.data.pieceRequests[i].block} Offset: {torrent.data.pieceRequests[i].block * torrent.data.blockSize} Size: {torrent.data.pieceRequests[i].size} Requests: {torrent.data.pieceRequests[i].peer.PiecesRequested} Timeouts: {torrent.data.pieceRequests[i].peer.PiecesTimeout} Piece timeout");

                            if (torrent.data.pieceRequests[i].peer.host != lastPeer)
                            {
                                lastPeer = torrent.data.pieceRequests[i].peer.host;

                                if (torrent.data.pieceRequests[i].peer.status != Peer.Status.READY && torrent.data.pieceRequests[i].peer.PiecesTimeout >= Options.RequestBlocksPerPeer)
                                {
                                    if (Options.Verbosity > 0) Log($"[DROP] [{torrent.data.pieceRequests[i].peer.host.PadRight(15, ' ')}] [REQT][P]\tRequests: {torrent.data.pieceRequests[i].peer.PiecesRequested} Timeouts: {torrent.data.pieceRequests[i].peer.PiecesTimeout} Piece timeout");
                                    torrent.data.pieceRequests[i].peer.Disconnect();
                                }
                                else if (torrent.data.pieceRequests[i].peer.status == Peer.Status.DOWNLOADING)
                                     torrent.data.pieceRequests[i].peer.status  = Peer.Status.READY;
                                    //{ torrent.data.pieceRequests[i].peer.status  = Peer.Status.READY; torrent.data.pieceRequests[i].peer.PiecesTimeout = 0; }
                            }

                            if (containsKey && !torrent.data.pieceRequests[i].aggressive) torrent.data.pieceProgress[torrent.data.pieceRequests[i].piece].requests.UnSetBit(torrent.data.pieceRequests[i].block);

                            if (!torrent.data.pieceRequests[i].aggressive) UnSetRequestsBit(torrent.data.pieceRequests[i].piece);
                            pieceTimeouts++;
                        }

                        torrent.data.pieceRequests.RemoveAt(i);
                    }
                }
            }

            lastCheckOfTimeoutsAt = DateTime.UtcNow.Ticks;
        }

        // Piece      [Save]
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
                if (firstByte < curSize) {
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
        #endregion



        #region Set/UnSet Bits on Main Progress/Requests [Keep Sync with Previous]
        private void SetProgressBit(int piece)
        {
            torrent.data.progress    .SetBit(piece);
            torrent.data.progressPrev.SetBit(piece);
        }
        private void SetRequestsBit(int piece)
        {
            torrent.data.requests    .SetBit(piece);
            torrent.data.requestsPrev.SetBit(piece);
        }
        private void UnSetProgressBit(int piece)
        {
            torrent.data.progress    .UnSetBit(piece);
            torrent.data.progressPrev.UnSetBit(piece);
        }
        private void UnSetRequestsBit(int piece)
        {
            torrent.data.requests    .UnSetBit(piece);
            torrent.data.requestsPrev.UnSetBit(piece);
        }
        #endregion

        #region Misc
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
        #endregion
    }
}