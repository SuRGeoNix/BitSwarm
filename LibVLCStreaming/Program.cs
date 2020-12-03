using System.Collections.Generic;
using System.Threading;

using SuRGeoNix.BitSwarmLib;
using LibVLCSharp.Shared;

using static System.Console;

namespace LibVLCStreaming
{
    class Program
    {
        // ADD ME
        static string       TORRENT_MAGNET_OR_HASH = "";
        // ADD ME

        static BitSwarm     bitSwarm;
        static LibVLC       libVLC;
        static MediaPlayer  mediaPlayer;

        static void Main(string[] args)
        {
            // Initialize LibVLC
            Core.Initialize();
            libVLC      = new LibVLC();
            libVLC.Log += (s, e) => WriteLine($"LibVLC -> {e.FormattedLog}");

            // Initialize BitSwarm
            Options opt = new Options();
            //opt.Verbosity = 2;
            //opt.LogStats = true;
            bitSwarm    = new BitSwarm(opt);
            bitSwarm.MetadataReceived   += BitSwarm_MetadataReceived;
            bitSwarm.StatsUpdated       += BitSwarm_StatsUpdated;
            bitSwarm.Open(TORRENT_MAGNET_OR_HASH);
            bitSwarm.Start();

            ReadKey();
        }

        private static void BitSwarm_MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            string selectedFile = null;

            // Choose file from available video files
            WriteLine($"======= Available files ({e.Torrent.StreamFiles.Count}) =======");
            foreach (var file in e.Torrent.StreamFiles.Values)
            {
                if (selectedFile == null) selectedFile = file.Stream.Filename; // Choose first one
                WriteLine(" + " + file.Stream.Filename);
            }

            // Prepare BitSwarm & LibVLC for the selected file
            bitSwarm.IncludeFiles(new List<string>() { selectedFile});
            using var media = new Media(libVLC, new StreamMediaInput(e.Torrent.StreamFiles[selectedFile].Stream));
            mediaPlayer = new MediaPlayer(media);

            // Start VLC Player
            Thread player = new Thread(() => { mediaPlayer.Play(); });
            player.IsBackground = true;
            player.Start();

            // Simulate Seek after 40secs at 50% of the movie
            //Thread seekSample = new Thread(() =>
            //{
            //    Thread.Sleep(40000);
            //    WriteLine("Seeking Sample at 50%");
            //    mediaPlayer.Position = 0.5f;
            //});
            //seekSample.IsBackground = true;
            //seekSample.Start();
        }

        private static void BitSwarm_StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e)
        {
            WriteLine(bitSwarm.DumpStats());
        }
    }
}
