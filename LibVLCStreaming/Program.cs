using System.Collections.Generic;
using System.Threading;

using SuRGeoNix;
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
            opt.BlockRequests   = 2;    // To avoid timeouts for large byte ranges
            opt.PieceTimeout    = 1000; // It will reset the requested blocks after 1sec of timeout (this means that we might get them twice or more) | More drop bytes vs Faster streaming (should be set only during open/seek)
            opt.PieceRetries    = 5;    // To avoid disconnecting the peer (reset pieces on first retry but keep the peer alive) 
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

            List<string> movieFiles = Utils.GetMoviesSorted(e.Torrent.file.paths);

            // Choose file from available video files
            WriteLine($"======= Available files ({movieFiles.Count}) =======");
            foreach (var file in movieFiles)
            {

                selectedFile = file;
                WriteLine(" + " + selectedFile);
                break;
            }

            // Prepare BitSwarm & LibVLC for the selected file
            bitSwarm.IncludeFiles(new List<string>() { selectedFile});
            using var media = new Media(libVLC, new StreamMediaInput(e.Torrent.GetTorrentStream(selectedFile)));
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
