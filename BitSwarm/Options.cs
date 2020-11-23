using System;
using System.IO;
using System.Xml.Serialization;

namespace SuRGeoNix.BitSwarmLib
{
    public class Options
    {
        public string   FolderComplete      { get; set; } = Directory.GetCurrentDirectory();
        public string   FolderIncomplete    { get; set; } = Path.Combine(Path.GetTempPath(), "BitSwarm", ".data");
        public string   FolderTorrents      { get; set; } = Path.Combine(Path.GetTempPath(), "BitSwarm", ".torrents");
        public string   FolderSessions      { get; set; } = Path.Combine(Path.GetTempPath(), "BitSwarm", ".sessions");

        public int      MaxThreads          { get; set; } =  150;   // Max Total  Connection Threads  | Short-Run + Long-Run
        public int      MinThreads          { get; set; } =   15;   // Max New    Connection Threads  | Short-Run

        public int      BoostThreads        { get; set; } =   60;   // Max New    Connection Threads  | Boot Boost
        public int      BoostTime           { get; set; } =   30;   // Boot Boost Time (Seconds)

        
        public int      SleepModeLimit      { get; set; } =    0;   // Activates Sleep Mode (Low Resources) at the specify DownRate | DHT Stop, Re-Fills Stop (DHT/Trackers) & MinThreads Drop to MinThreads / 2
                                                                    // -1: Auto | 0: Disabled | Auto will figure out SleepModeLimit from MaxRate

        //public int      DownloadLimit       { get; set; } = -1;
        //public int      UploadLimit         { get; set; }

        public int      ConnectionTimeout   { get; set; } =  600;
        public int      HandshakeTimeout    { get; set; } =  800;
        public int      MetadataTimeout     { get; set; } = 1600;
        public int      PieceTimeout        { get; set; } = 1500;   // Large timeouts without resets will cause more working pieces (more memory/more lost bytes on force stop)
        public int      PieceRetries        { get; set; } =    3;

        public bool     EnablePEX           { get; set; } = true;
        public bool     EnableDHT           { get; set; } = true;
        public bool     EnableTrackers      { get; set; } = true;
        public int      PeersFromTracker    { get; set; } = -1;

        public int      BlockRequests       { get; set; } =  9;     // Blocks that we request at once for each peer (should be small on streaming to avoid delayed resets on timeouts)

        public int      Verbosity           { get; set; } =  0;     // [0 - 4]
        public bool     LogTracker          { get; set; } = false;  // Verbosity 1
        public bool     LogPeer             { get; set; } = false;  // Verbosity 1 - 4
        public bool     LogDHT              { get; set; } = false;  // Verbosity 1
        public bool     LogStats            { get; set; } = false;  // Verbosity 1

        public string   TrackersPath        { get; set; } = "";


        public static string ConfigFile     { get; private set; } = "bitswarm.config.xml";

        public static void CreateConfig(Options opt, string path = null)
        {
            if (path == null) path = Directory.GetCurrentDirectory();

            using (FileStream fs = new FileStream(Path.Combine(path, ConfigFile), FileMode.Create))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Options));
                xmlSerializer.Serialize(fs, opt);
            }
        }
        public static Options LoadConfig(string customFilePath = null)
        {
            string foundPath;

            if (customFilePath != null)
            {
                if (!File.Exists(customFilePath)) return null;
                foundPath = customFilePath;
            } 
            else
            {
                foundPath = SearchConfig();
                if (foundPath == null) return null;
            }

            using (FileStream fs = new FileStream(foundPath, FileMode.Open))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Options));
                return (Options) xmlSerializer.Deserialize(fs);
            }
        }
        private static string SearchConfig()
        {
            string  foundPath;

            foundPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigFile);
            if (File.Exists(foundPath)) return foundPath;

            if (Utils.IsWindows)
            {
                foundPath = Path.Combine(Environment.ExpandEnvironmentVariables("%APPDATA%") , "BitSwarm", ConfigFile);
                if (File.Exists(foundPath)) return foundPath;
            }
            else
            {
                foundPath = Path.Combine(Environment.ExpandEnvironmentVariables("%HOME%") , ".bitswarm", ConfigFile);
                if (File.Exists(foundPath)) return foundPath;

                foundPath = $"/etc/bitswarm/{ConfigFile}";
                if (File.Exists(foundPath)) return foundPath;
            }

            return null;
        }
    }
}
