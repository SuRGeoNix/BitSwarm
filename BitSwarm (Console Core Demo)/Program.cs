using System;
using System.IO;
using System.Threading;

using SuRGeoNix;
using SuRGeoNix.BEP;

namespace BitSwarmConsole
{
    class Program
    {
        static BitSwarm                 bitSwarm;
        static BitSwarm.DefaultOptions  opt;
        static Torrent                  torrent;

        static bool sessionFinished = false;
        static bool preventOnce     = true;
        static bool resized         = false;
        static int  consoleLastTop  = -1;
        static int  prevHeight;

        static void Main(string[] args)
        {
            if (args.Length < 1 || args.Length > 6)
            {
                Console.WriteLine("./bitswarm TorrentFile|MagnetUrl [SaveDirectory=%temp%] [MinThreads=10] [MaxThreads=150] [SleepLimit=-1 Auto, 0 Disabled, >0 Custom KB/s] [Logs=false]");
                return;
            }
            
            // Prepare Options
            opt = new BitSwarm.DefaultOptions();
            if (args.Length >= 2)
            {
                if (!Directory.Exists(args[1])) Directory.CreateDirectory(args[1]);
                opt.DownloadPath = args[1];
            }

            if (args.Length >= 3) opt.MinThreads        = int.Parse(args[2]);
            if (args.Length >= 4) opt.MaxThreads        = int.Parse(args[3]);
            if (args.Length >= 5) opt.SleepModeLimit    = int.Parse(args[4]);

            if (args.Length >= 6 && !(args[5] == "0" || args[5] == "false"))
            {
                opt.Verbosity   = 4;
                opt.LogStats    = true;
                opt.LogPeer     = true;
                opt.LogTracker  = true;
                opt.LogDHT      = true;
            }

            // More Options

            //opt.EnableDHT     = false;
            //opt.EnableTrackers= false;
            //opt.TrackersPath  = @"c:\root\trackers.txt";

            //opt.ConnectionTimeout   = 1200;
            //opt.HandshakeTimeout    = 2400;
            //opt.MetadataTimeout     = 1600;
            //opt.PieceTimeout        = 7000;

            // Initialize BitSwarm
            bitSwarm = new BitSwarm(opt);

            bitSwarm.StatsUpdated       += BitSwarm_StatsUpdated;
            bitSwarm.MetadataReceived   += BitSwarm_MetadataReceived;
            bitSwarm.StatusChanged      += BitSwarm_StatusChanged;

            if (File.Exists(args[0])) 
                bitSwarm.Initiliaze(args[0]);
            else
                bitSwarm.Initiliaze(new Uri(args[0]));

            // Start BitSwarm Until Something Good or Bad happens
            bitSwarm.Start();

            Console.CancelKeyPress += new ConsoleCancelEventHandler(CtrlC);
            prevHeight = Console.WindowHeight;

            while (!sessionFinished)
            {
                if (Console.WindowHeight != prevHeight) { prevHeight = Console.WindowHeight; resized = true; }
                Thread.Sleep(500);
            }
        }
        protected static void CtrlC(object sender, ConsoleCancelEventArgs args)
        {
            bitSwarm.Dispose(true);
            sessionFinished = true;
            if (preventOnce) { args.Cancel = true; preventOnce = false; }
        }
        private static void BitSwarm_StatusChanged(object source, BitSwarm.StatusChangedArgs e)
        {
            if (e.Status == 0)
            {
                //Console.WriteLine("[BITSWARM] Finished at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo) + " | Elapsed: " +  (new TimeSpan(bitSwarm.Stats.CurrentTime - bitSwarm.Stats.StartTime)).ToString(@"hh\:mm\:ss\:fff"));
                if (torrent != null && torrent.file.name != null) Console.WriteLine($"Download of {torrent.file.name} success!\r\n");
            }
            else
            {
                //Console.WriteLine("[BITSWARM] Stopped at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo) + " | Elapsed: " +  (new TimeSpan(bitSwarm.Stats.CurrentTime - bitSwarm.Stats.StartTime)).ToString(@"hh\:mm\:ss\:fff"));
                if (e.Status == 2) Console.WriteLine("An error has been occured :( " + e.ErrorMsg);
            }

            sessionFinished = true;
        }
        private static void BitSwarm_MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            torrent = e.Torrent;
            Console.WriteLine(bitSwarm.DumpTorrent() + "\n");
        }
        private static void BitSwarm_StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e)
        {
            if (resized)
            {
                Console.Clear();
                Console.WriteLine(bitSwarm.DumpTorrent() + "\n");
                resized = false;
            }

            if (consoleLastTop == -1) consoleLastTop = Console.CursorTop;
            Console.SetCursorPosition(0, consoleLastTop);
            for (int i=0; i<10; i++)
                Console.WriteLine(new String(' ', Console.BufferWidth));
            Console.SetCursorPosition(0, consoleLastTop);
            Console.WriteLine(bitSwarm.DumpStats());
        }
    }
}