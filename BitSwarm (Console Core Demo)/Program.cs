using System;
using System.Globalization;
using System.IO;
using System.Threading;

using SuRGeoNix;
using SuRGeoNix.BEP;

namespace BitSwarmConsole
{
    class Program
    {
        static BitSwarm                 bitSwarm;
        static BitSwarm.OptionsStruct   opt;
        static Torrent                  torrent;
        static bool                     sessionFinished;

        static void Main(string[] args)
        {
            // For IDE Testing

            //args = new string[7];
            //args[0] = "magnet:?xt=...";   // Magnet Link
            //args[0] = @"file.torrent";    // Torrent File
            //args[1] = @"folder";          // SavePath   [Default:   %temp%]
            //args[2] = "20";               // MaxThreads [Default:       20]
            //args[3] = "200";              // MaxConns   [Default:      200]
            //args[4] = "0";                // DownLimit  [Default: No Limit]
            //args[5] = "2048";             // SleepLimit [Default: No Limit]
            //args[6] = "false";            // Logs       [Default: Disabled]

            if (args.Length < 1 || args.Length > 7)
            {
                Console.WriteLine("./bitswarm TorrentFile|MagnetUrl [SaveDirectory=%temp%] [MaxThreads=20] [MaxConnections=200] [DownLimit=0] [SleepLimit=0] [Logs=false]");
                return;
            }

            // Prepare Options
            opt = BitSwarm.GetDefaultsOptions();
            if (args.Length >= 2)
            {
                if (!Directory.Exists(args[1])) Directory.CreateDirectory(args[1]);
                opt.DownloadPath = args[1];
            }

            if (args.Length >= 3) opt.MaxThreads            = int.Parse(args[2]);
            if (args.Length >= 4) opt.MaxConnections        = int.Parse(args[3]);
            if (args.Length >= 5) opt.DownloadLimit         = int.Parse(args[4]);
            if (args.Length >= 6) opt.SleepDownloadLimit    = int.Parse(args[5]);

            if (args.Length >= 7 && !(args[6] == "0" || args[6] == "false"))
            {
                opt.Verbosity = 1;
                opt.LogStats = true;
                //opt.LogPeer = true;
                opt.LogTracker = true;
                opt.LogDHT = true;
            }

            // More Options
            //opt.EnableDHT = false;
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
            Console.WriteLine("[BITSWARM] Started at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo));
            bitSwarm.Start();

            Console.CancelKeyPress += new ConsoleCancelEventHandler(CtrlC);

            while (!sessionFinished)
                Thread.Sleep(500);

            bitSwarm.Dispose();

            // Clean Up 0 Size Files
            DirectoryInfo downDir = new DirectoryInfo(opt.DownloadPath);
            foreach (var file in downDir.GetFiles())
                try { if (file.Length == 0) file.Delete(); } catch (Exception) { }
                
        }

        protected static void CtrlC(object sender, ConsoleCancelEventArgs args)
        {
            Console.WriteLine("[BITSWARM] Stopping");

            bitSwarm.Pause();
            if (!Utils.IsWindows) sessionFinished = true;
            args.Cancel = true;
        }
        private static void BitSwarm_StatusChanged(object source, BitSwarm.StatusChangedArgs e)
        {
            if (e.Status == 0)
            {
                Console.WriteLine("[BITSWARM] Finished at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo));
                if (torrent != null && torrent.file.name != null) Console.WriteLine($"Downloaded {torrent.file.name} successfully!\r\n");
            }
            else
            {
                Console.WriteLine("[BITSWARM] Stopped at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo));
                if (e.Status == 2) Console.WriteLine("An error occured :( " + e.ErrorMsg);
            }

            sessionFinished = true;
        }
        private static void BitSwarm_MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            torrent = e.Torrent;
            string str = "\n";

            //str += "Torrent Details\r\n=================\r\nName ->\t\t" + torrent.file.name + "\r\nSize ->\t\t" + Utils.BytesToReadableString(torrent.data.totalSize) + "\r\n\r\nFiles\r\n==============================\r\n";

            str += "===============\n";
            str += "Torrent Details\n";
            str += "===============\n";
            str += torrent.file.name + " (" + Utils.BytesToReadableString(torrent.data.totalSize) + ")\n";
            str += "-----\n";
            str += "Files\n";
            str += "-----\n";

            for (int i=0; i<torrent.data.files.Count; i++)
                str += torrent.data.files[i].FileName + " (" + Utils.BytesToReadableString(torrent.data.files[i].Size) + ")\n";

            Console.WriteLine(str);
        }
        private static void BitSwarm_StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e)
        {
            string str = "";
            str += " [DOWN CUR] "   + String.Format("{0:n0}", (e.Stats.DownRate / 1024)) + " KB/s";
            str += " [DOWN AVG] "   + String.Format("{0:n0}", (e.Stats.AvgRate  / 1024)) + " KB/s";
            str += " [DOWN MAX] "   + String.Format("{0:n0}", (e.Stats.MaxRate  / 1024)) + " KB/s";
            str += " ||";
            str += " [ETA] "        + TimeSpan.FromSeconds((e.Stats.ETA + e.Stats.AvgETA)/2).ToString(@"hh\:mm\:ss");
            str += " [ETA AVG] "    + TimeSpan.FromSeconds(e.Stats.AvgETA).ToString(@"hh\:mm\:ss");
            str += " [ETA CUR] "    + TimeSpan.FromSeconds(e.Stats.ETA).ToString(@"hh\:mm\:ss");
            if (torrent != null)
            str += " || [PROGRESS] " + ((int) (torrent.data.progress.setsCounter * 100.0 / torrent.data.progress.size)) + "%";

            Console.WriteLine("[TOTAL] " + (e.Stats.PeersConnecting + e.Stats.PeersConnected + e.Stats.PeersDownloading) + " | [INQUEUE] " + e.Stats.PeersInQueue + " | [CONNECTING] " + e.Stats.PeersConnecting + " | [CHOCKED] " + e.Stats.PeersChoked + " | [DOWNLOADING] " + e.Stats.PeersDownloading + " | [SLEEPMODE] " + (e.Stats.SleepMode ? "On" : "Off") + (!bitSwarm.Options.EnableDHT ? "" : " | [DHT STATUS] " + bitSwarm.dht.status.ToString() + " | [DHT PEERS] " + bitSwarm.dht.CachedPeers.Count));
            Console.WriteLine(str);
            Console.WriteLine("");
        }
    }
}