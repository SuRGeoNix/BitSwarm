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
        static int  consoleStatsPos = 0;

        static void Main(string[] args)
        {
            // ----------- For IDE Testing -----------

            //args = new string[6];

            //args[0] = "magnet:?xt=...";   // Magnet Link
            //args[0] = @"file.torrent";    // Torrent File

            //args[1] = @"folder";          // SavePath   [Default:   %temp%]
            //args[2] = "20";               // MaxThreads [Default:       10]
            //args[3] = "200";              // MaxConns   [Default:      150]
            //args[4] = "2048";             // SleepLimit [Default: No Limit] 0: No Limit | -1: Auto | >0: Custom KB/s
            //args[5] = "false";            // Logs       [Default: Disabled]

            if (args.Length < 1 || args.Length > 6)
            {
                Console.WriteLine("./bitswarm TorrentFile|MagnetUrl [SaveDirectory=%temp%] [MinThreads=10] [MaxThreads=150] [SleepLimit=-1 Auto, 0 Disabled, >0 Custom KB/s] [Logs=false]");
                return;
            }
            
            // Prepare Options
            opt = new BitSwarm.DefaultOptions();
            if (args.Length >= 2) opt.DownloadPath      = args[1];
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

            // [Feeders]
            //opt.EnablePEX     = false;
            //opt.EnableDHT     = false;
            //opt.EnableTrackers= false;
            //opt.TrackersPath  = @"c:\root\trackers.txt";

            // [Timeouts]
            //opt.ConnectionTimeout   = 1200;
            //opt.HandshakeTimeout    = 2400;
            //opt.MetadataTimeout     = 1600;
            //opt.PieceTimeout        = 2000;
            //opt.PieceRetries        = 3; // Re-requests timed-out pieces on the first timeout

            // Initialize BitSwarm
            bitSwarm = new BitSwarm(opt);

            bitSwarm.MetadataReceived   += BitSwarm_MetadataReceived;   // Receives torrent data [on torrent file will fire directly, on magnetlink will fire on metadata received]
            bitSwarm.StatsUpdated       += BitSwarm_StatsUpdated;       // Stats refresh every 2 seconds
            bitSwarm.StatusChanged      += BitSwarm_StatusChanged;      // Paused/Stopped or Finished

            if (File.Exists(args[0])) 
                bitSwarm.Initiliaze(args[0]);
            else
                bitSwarm.Initiliaze(new Uri(args[0]));

            // Start BitSwarm Until Something Good or Bad happens
            bitSwarm.Start();

            Console.CancelKeyPress += new ConsoleCancelEventHandler(CtrlC);

            while (!sessionFinished) Thread.Sleep(500);
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
            Console.Clear();
            if (Utils.IsWindows) { Console.WriteLine(bitSwarm.DumpTorrent() + "\r\n"); consoleStatsPos = Console.CursorTop; }
        }
        private static void BitSwarm_StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e)
        {
            if (Utils.IsWindows)
            {
                Console.SetCursorPosition(0, consoleStatsPos);
            }
            else
            {
                Console.SetCursorPosition(0, 0);
                Console.WriteLine(bitSwarm.DumpTorrent() + "\r\n");
            }
            
            Console.WriteLine(bitSwarm.DumpStats());
        }
    }
}