using System;
using System.Collections.Generic;
using System.IO;

using CommandLine;
using CommandLine.Text;

using SuRGeoNix;
using SuRGeoNix.BEP;

namespace BitSwarmConsole
{
    class Program
    {
        static BitSwarm                 bitSwarm;
        static BitSwarm.DefaultOptions  opt;
        static Torrent                  torrent;

        static bool                     sessionFinished;
        static int                      prevHeight;
        static object                   lockRefresh     = new object();

        static View                     view            = View.Stats;

        enum View
        {
            Peers,
            Stats,
            Torrent
        }
        private static void Run(Options opt2)
        {
            try
            {
                // Prepare Options
                opt = new BitSwarm.DefaultOptions();

                opt.DownloadPath        = opt2.DownloadPath;

                opt.MinThreads          = opt2.MinThreads;
                opt.MaxThreads          = opt2.MaxThreads;

                opt.BoostThreads        = opt2.BoostThreads;
                opt.BoostTime           = opt2.BoostTime;
                opt.SleepModeLimit      = opt2.SleepModeLimit;

                opt.ConnectionTimeout   = opt2.ConnectionTimeout;
                opt.HandshakeTimeout    = opt2.HandshakeTimeout;
                opt.PieceTimeout        = opt2.PieceTimeout;
                opt.PieceRetries        = opt2.PieceRetries;
            
                opt.PeersFromTracker    = opt2.PeersFromTracker;
                opt.TrackersPath        = opt2.TrackersPath;

                opt.EnableDHT           = !opt2.DisableDHT;
                opt.EnablePEX           = !opt2.DisablePEX;
                opt.EnableTrackers      = !opt2.DisableTrackers;

                opt.BlockRequests       = opt2.BlockRequests;

                opt.Verbosity           = opt2.LogVerbosity;
                if (opt.Verbosity > 0)
                {
                    opt.LogDHT          = !opt2.NoLogDHT;
                    opt.LogPeer         = !opt2.NoLogPeer;
                    opt.LogTracker      = !opt2.NoLogTracker;
                    opt.LogStats        = !opt2.NoLogStats;
                }

                // Initialize & Start BitSwarm
                bitSwarm = new BitSwarm(opt);

                bitSwarm.MetadataReceived   += BitSwarm_MetadataReceived;   // Receives torrent data [on torrent file will fire directly, on magnetlink will fire on metadata received]
                bitSwarm.StatsUpdated       += BitSwarm_StatsUpdated;       // Stats refresh every 2 seconds
                bitSwarm.StatusChanged      += BitSwarm_StatusChanged;      // Paused/Stopped or Finished

                if (File.Exists(opt2.TorrentFile)) 
                    bitSwarm.Initiliaze(opt2.TorrentFile);
                else
                    bitSwarm.Initiliaze(new Uri(opt2.TorrentFile));

                bitSwarm.Start();

                // Stats | Torrent | Peers Views [Until Stop or Finish]
                ConsoleKeyInfo cki;
                Console.TreatControlCAsInput = true;
                prevHeight = Console.WindowHeight;

                while (!sessionFinished)
                {
                    try
                    {
                        cki = Console.ReadKey();

                        if (sessionFinished) break;
                        if ((cki.Modifiers & ConsoleModifiers.Control) != 0 && cki.Key == ConsoleKey.C) break;

                        lock (lockRefresh)
                        switch (cki.Key)
                        {
                            case ConsoleKey.D1:
                                view = View.Stats;
                                Console.Clear();
                                Console.WriteLine(bitSwarm.DumpTorrent() + "\r\n");
                                Console.WriteLine(bitSwarm.DumpStats());
                                PrintMenu();
                                break;

                            case ConsoleKey.D2:
                                view = View.Torrent;
                                Console.Clear();
                                Console.WriteLine(bitSwarm.DumpTorrent());
                                PrintMenu();

                                break;

                            case ConsoleKey.D3:
                                view = View.Torrent;
                                Console.Clear();
                                Console.WriteLine(bitSwarm.DumpPeers());
                                PrintMenu();

                                break;

                            case ConsoleKey.D4:
                                view = View.Peers;
                                Console.Clear();
                                Console.WriteLine(bitSwarm.DumpPeers());
                                PrintMenu();

                                break;

                            default:
                                break;
                        }
                    } catch (Exception) { }
                }

                // Dispose (force) BitSwarm
                if (bitSwarm != null) bitSwarm.Dispose(true);

            } catch (Exception e) { Console.WriteLine($"[ERROR] {e.Message}"); }
        }

        private static void Main(string[] args)
        {
            var parser          = new Parser(with => with.HelpWriter = null);
            var parserResult    = parser.ParseArguments<Options>(args);
            parserResult.WithParsed<Options>(options => Run(options)).WithNotParsed(errs => PrintHelp(parserResult, errs));
        }
        private static void PrintHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
        {
            var helpText = HelpText.AutoBuild(result, h => { return HelpText.DefaultParsingErrorsHandler(result, h); }, e => e, false, 160);
            helpText.AddPostOptionsText("\r\n" + "USAGE: \r\n\r\n\t" + "./bitswarm [OPTIONS] torrentfile|magnetlink");
            Console.WriteLine(helpText);
        }
        private static void PrintMenu() { Console.WriteLine("[1: Stats] [2: Torrent] [3: Peers] [4: Peers (w/Refresh)] [Ctrl-C: Exit]".PadLeft(100, ' ')); }

        private static void BitSwarm_StatusChanged(object source, BitSwarm.StatusChangedArgs e)
        {
            if (e.Status == 0 && torrent != null && torrent.file.name != null)
                Console.WriteLine($"Download of {torrent.file.name} success!\r\n");
            else if (e.Status == 2)
                Console.WriteLine("An error has been occured :( " + e.ErrorMsg);

            bitSwarm.Dispose();
            bitSwarm = null;
            sessionFinished = true;
        }
        private static void BitSwarm_MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            lock (lockRefresh)
            {
                torrent = e.Torrent;
                Console.Clear();
                Console.WriteLine(bitSwarm.DumpTorrent() + "\r\n");
            }
        }
        private static void BitSwarm_StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e)
        {
            try
            {
                if (view != View.Stats && view != View.Peers) return;

                if (Console.WindowHeight != prevHeight) { prevHeight = Console.WindowHeight; Console.Clear(); }

                lock (lockRefresh)
                if (view == View.Peers)
                {
                    Console.Clear();
                    Console.SetCursorPosition(0, 0);
                    Console.WriteLine(bitSwarm.DumpPeers());
                }
                else if (view == View.Stats)
                {
                    Console.SetCursorPosition(0, 0);
                    Console.WriteLine(bitSwarm.DumpTorrent() + "\r\n");
                    Console.WriteLine(bitSwarm.DumpStats());
                
                }

                PrintMenu();

            } catch (Exception) { }
        }
    }

    class Options
    {
        [Option('o', "output",  Default = ".",  HelpText = "Download directory")]
        public string   DownloadPath    { get; set; }

        [Option("mc",          Default = 15,   HelpText = "Max new connection threads")]
        public int      MinThreads      { get; set; }

        [Option("mt",          Default = 150,  HelpText = "Max total threads")]
        public int      MaxThreads      { get; set; }

        [Option("boostmin",     Default = 60,   HelpText = "Boost new connection threads")]
        public int      BoostThreads    { get; set; }

        [Option("boostsecs",    Default = 30,   HelpText = "Boost time in seconds")]
        public int      BoostTime       { get; set; }
        [Option("sleep",        Default = 0,    HelpText = "Sleep activation at this down rate KB/s (-1 Automatic)")]
        public int      SleepModeLimit  { get; set; }

        [Option("no-dht",       Default = false,HelpText = "Disable DHT")]
        public bool     DisableDHT      { get; set; }

        [Option("no-pex",       Default = false,HelpText = "Disable PEX")]
        public bool     DisablePEX      { get; set; }

        [Option("no-trackers",  Default = false,HelpText = "Disable Trackers")]
        public bool     DisableTrackers { get; set; }

        [Option("trackers-num", Default = -1,   HelpText = "# of peers will be requested from each tracker")]
        public int      PeersFromTracker{ get; set; }

        [Option("trackers-file",                HelpText = "Trackers file to include (format 'scheme://host:port' per line)")]
        public string   TrackersPath    { get; set; }

        [Option("ct",           Default = 600,  HelpText = "Connection timeout in ms")]
        public int      ConnectionTimeout{get; set; }

        [Option("ht",           Default = 800,  HelpText = "Handshake timeout in ms")]
        public int      HandshakeTimeout{ get; set; }

        [Option("pt",           Default = 5000, HelpText = "Piece timeout in ms")]
        public int      PieceTimeout    { get; set; }

        [Option("pr",           Default = 0,    HelpText = "Piece retries")]
        public int      PieceRetries    { get; set; }

        [Option("req-blocks",   Default = 6,    HelpText = "Parallel block requests per peer")]
        public int      BlockRequests   { get; set; }

        [Option("log",          Default = 0,    HelpText = "Log verbosity [0-4]")]
        public int      LogVerbosity    { get; set; }

        [Option("no-log-dht",   Default = false,HelpText = "Disable logging for DHT")]
        public bool     NoLogDHT        { get; set; }

        [Option("no-log-peers", Default = false,HelpText = "Disable logging for Peers")]
        public bool     NoLogPeer       { get; set; }

        [Option("no-log-trackers",Default=false,HelpText = "Disable logging for Trackers")]
        public bool     NoLogTracker    { get; set; }

        [Option("no-log-stats", Default = false,HelpText = "Disable logging for Stats")]
        public bool     NoLogStats      { get; set; }

        [Value(0, MetaName = "Torrent file or Magnet link", Required = true, HelpText = "")]
        public string   TorrentFile     { get; set; }
    }
}