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
            if (args.Length < 1 || args.Length > 2)
            {
                Console.WriteLine("./bswarm TorrentFile|MagnetUrl [SaveDirectory]");
                return;
            }

            // Prepare Options
            opt = BitSwarm.GetDefaultsOptions();
            if (args.Length == 2)
            {
                if (!Directory.Exists(args[1])) Directory.CreateDirectory(args[1]);
                opt.DownloadPath = args[1];
            }
            //opt.Verbosity = 3;
            //opt.LogStats = true;
            //opt.LogPeer = true;
            //opt.LogTracker = true;
            //opt.LogDHT = true;
            
            // Initialize BitSwarm
            if (File.Exists(args[0])) 
                bitSwarm = new BitSwarm(args[0], opt);
            else
                bitSwarm = new BitSwarm(new Uri(args[0]), opt);

            bitSwarm.StatsUpdated       += BitSwarm_StatsUpdated;
            bitSwarm.MetadataReceived   += BitSwarm_MetadataReceived;
            bitSwarm.StatusChanged      += BitSwarm_StatusChanged;

            // Start BitSwarm Until Something Good or Bad happens
            Console.WriteLine("Started at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo));
            bitSwarm.Start();

            while (!sessionFinished)
                Thread.Sleep(500);

            // Clean Up 0 Size Files
            Thread.Sleep(100);
            DirectoryInfo downDir = new DirectoryInfo(opt.DownloadPath);
            foreach (var file in downDir.GetFiles())
                if (file.Length == 0) file.Delete();
        }

        private static void BitSwarm_StatusChanged(object source, BitSwarm.StatusChangedArgs e)
        {
            if (e.Status == 0)
            {
                Console.WriteLine("Finished at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo));
                if (torrent != null && torrent.file.name != null) Console.WriteLine($"Downloaded {torrent.file.name} successfully!\r\n");
            }
            else
            {
                Console.WriteLine("Stopped at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo));
                if (e.Status == 2) Console.WriteLine("An error occured :( " + e.ErrorMsg);
            }

            if (torrent != null) torrent.Dispose();

            sessionFinished = true;
        }
        private static void BitSwarm_MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            torrent = e.Torrent;
            string str = "Metadata Received\r\n=================\r\nName ->\t\t" + torrent.file.name + "\r\nSize ->\t\t" + Utils.BytesToReadableString(torrent.data.totalSize) + "\r\n\r\nFiles\r\n==============================\r\n";

            for (int i=0; i<torrent.data.files.Count; i++)
                str += torrent.data.files[i].FileName + "\t\t(" + Utils.BytesToReadableString(torrent.data.files[i].Size) + ")\r\n";

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
            
            Console.WriteLine(str);
        }
    }
}