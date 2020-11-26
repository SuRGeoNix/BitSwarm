using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web;

using BencodeNET.Parsing;
using BencodeNET.Objects;

using SuRGeoNix.Partfiles;

namespace SuRGeoNix.BitSwarmLib.BEP
{
    [Serializable]
    public class Torrent : IDisposable
    {
        [NonSerialized]
        internal        BitSwarm        bitSwarm;
        [NonSerialized]
        private static  BencodeParser   bParser = new BencodeParser();
        [NonSerialized]
        private static  SHA1            sha1    = new SHA1Managed();

        public TorrentFile  file;
        public TorrentData  data;
        public MetaData     metadata;

        [Serializable]
        public struct TorrentFile
        {
            // SHA-1 of 'info'
            public string           infoHash        { get; set; }

            // 'announce' | 'announce-list'
            public List<Uri>        trackers        { get; set; }

            // 'info'

            // 'name' | 'length'
            public string           name            { get; set; }
            public long             length          { get; set; }

            // ['path' | 'length']
            public List<string>     paths           { get; set; }
            public List<long>       lengths         { get; set; }

            public int              pieceLength     { get; set; }

            public List<byte[]>     pieces;
        }

        [Serializable]
        public struct TorrentData
        {
            public bool             isDone          { get; set; }

            [NonSerialized]
            public Partfile[]       files;

            public List<string>     filesIncludes   { get; set; }
            public string           folder          { get; set; }
            public string           folderTemp      { get; set; }
            public long             totalSize       { get; set; }

            public int              pieces          { get; set; }
            public int              pieceSize       { get; set; }
            public int              pieceLastSize   { get; set; } // NOTE: it can be 0, it should be equals with pieceSize in case of totalSize % pieceSize = 0

            public int              blocks          { get; set; }
            public int              blockSize       { get; set; }
            public int              blockLastSize   { get; set; }
            public int              blocksLastPiece { get; set; }

            [NonSerialized]
            public Bitfield         progress;
            [NonSerialized]
            public Bitfield         requests;
            [NonSerialized]
            public Bitfield         progressPrev;

            [NonSerialized]
            internal Dictionary<int, PieceProgress>   pieceProgress;

            internal class PieceProgress
            {
                public PieceProgress(ref TorrentData data, int piece)
                {
                    bool isLastPiece= piece == data.pieces - 1 && data.totalSize % data.pieceSize != 0;

                    this.piece      = piece;
                    this.data       = !isLastPiece ? new byte[data.pieceSize] : new byte[data.pieceLastSize];
                    this.progress   = !isLastPiece ? new Bitfield(data.blocks): new Bitfield(data.blocksLastPiece);
                    this.requests   = !isLastPiece ? new Bitfield(data.blocks): new Bitfield(data.blocksLastPiece);
                }
                public int          piece;
                public byte[]       data;
                public Bitfield     progress;
                public Bitfield     requests;
            }
        }

        [Serializable]
        public struct MetaData
        {
            public bool             isDone          { get; set; }

            [NonSerialized]
            public Partfile         file;
            public int              pieces          { get; set; }
            public long             totalSize       { get; set; }

            [NonSerialized]
            public Bitfield         progress;

            public int              parallelRequests;
        }

        public Torrent (BitSwarm bitSwarm) 
        {
            this.bitSwarm       = bitSwarm;
            file                = new TorrentFile();
            data                = new TorrentData();
            metadata            = new MetaData();
            file.trackers       = new List<Uri>();
        }

        public void FillFromMagnetLink(Uri magnetLink)
        {
            // TODO: Check v2 Magnet Link
            // http://www.bittorrent.org/beps/bep_0009.html

            NameValueCollection nvc = HttpUtility.ParseQueryString(magnetLink.Query);
            string[] xt     = nvc.Get("xt") == null ? null  : nvc.GetValues("xt")[0].Split(Char.Parse(":"));
            if (xt == null || xt.Length != 3 || xt[1].ToLower() != "btih" || xt[2].Length < 20) throw new Exception("[Magnet][xt] No hash found " + magnetLink);

            file.name       = nvc.Get("dn") == null ? null  : nvc.GetValues("dn")[0] ;
            file.length     = nvc.Get("xl") == null ? 0     : (int) UInt32.Parse(nvc.GetValues("xl")[0]);
            file.infoHash   = xt[2];

            if (Regex.IsMatch(file.infoHash,@"^[2-7a-z]+=*$", RegexOptions.IgnoreCase)) file.infoHash = Utils.ArrayToStringHex(Utils.FromBase32String(file.infoHash));
            if (file.infoHash.Length != 40 || !Regex.IsMatch(file.infoHash, @"^[0-9a-f]+$", RegexOptions.IgnoreCase)) throw new Exception("[Magnet][xt] No valid hash found " + magnetLink);

            string[] tr = nvc.Get("tr") == null ? null : nvc.GetValues("tr");
            if (tr == null) return;

            for (int i=0; i<tr.Length; i++)
                file.trackers.Add(new Uri(tr[i]));
        }
        public BDictionary FillFromTorrentFile(string fileName)
        {
            BDictionary bdicTorrent = bParser.Parse<BDictionary>(fileName);
            BDictionary bInfo;

            if (bdicTorrent["info"] != null)
            {
                bInfo = (BDictionary) bdicTorrent["info"];
                FillTrackersFromInfo(bdicTorrent);
            } 
            else if (bdicTorrent["name"] != null)
                bInfo = bdicTorrent;
            else
                throw new Exception("Invalid torrent file");

            file.infoHash = Utils.ArrayToStringHex(sha1.ComputeHash(bInfo.EncodeAsBytes()));
            file.name     = ((BString) bInfo["name"]).ToString();

            return bInfo;
        }
        public void FillFromMetadata()
        {
            try
            {
                if (metadata.file == null) bitSwarm.StopWithError("No metadata found");

                string curFilePath  = Path.Combine(metadata.file.Options.Folder, metadata.file.Filename);
                string curPath      = (new FileInfo(curFilePath)).DirectoryName;

                metadata.file.Dispose();
                BDictionary bInfo   = (BDictionary) bParser.Parse(curFilePath);

                if (file.infoHash != Utils.ArrayToStringHex(sha1.ComputeHash(bInfo.EncodeAsBytes())))
                    bitSwarm.StopWithError("[CRITICAL] Metadata SHA1 validation failed");

                file.name = ((BString) bInfo["name"]).ToString();

                string torrentName  = Utils.GetValidFileName(file.name) + ".torrent";

                if (!File.Exists(Path.Combine(curPath, torrentName))) File.Move(curFilePath, Path.Combine(bitSwarm.Options.FolderTorrents, torrentName));

                FillFromInfo(bInfo);
            } catch (Exception e) { bitSwarm.StopWithError($"FillFromMetadata(): {e.Message} - {e.StackTrace}"); }
            
        }
        public void FillTrackersFromTrackersPath(string fileName)
        {
            try
            {
                if (fileName == null || fileName.Trim() == "" || !File.Exists(fileName)) return;

                string[] trackers = File.ReadAllLines(fileName);

                foreach (var tracker in trackers)
                    try { file.trackers.Add(new Uri(tracker)); } catch (Exception) { }
            } catch (Exception) { }
        }
        public void FillTrackersFromInfo(BDictionary torrent)
        {
            string tracker = null;
            BList trackersBList = null;

            if (torrent["announce"] != null)
                tracker = ((BString) torrent["announce"]).ToString();

            if (torrent["announce-list"] != null)
                trackersBList = (BList) torrent["announce-list"];

            if (trackersBList != null)
                for (int i=0; i<trackersBList.Count; i++)
                    file.trackers.Add(new Uri(((BString)((BList)trackersBList[i])[0]).ToString()));

            if (tracker != null)
                file.trackers.Add(new Uri(tracker));
        }
        public void FillFromInfo(BDictionary bInfo)
        {
            if (bitSwarm.Options.FolderComplete == null) bitSwarm.StopWithError("[CRITICAL] Folder Complete cannot be empty");

            bool isMultiFile    = (BList) bInfo["files"] == null ? false : true;

            file.pieces         = GetHashesFromInfo(bInfo);
            file.pieceLength    = (BNumber) bInfo["piece length"];

            data.filesIncludes  = new List<string>();

            Partfiles.Options opt = new Partfiles.Options();
            opt.AutoCreate      = true;

            if (isMultiFile)
            {
                file.paths      = GetPathsFromInfo(bInfo);
                data.files      = new Partfile[file.paths.Count];
                file.lengths    = GetFileLengthsFromInfo(bInfo, out long tmpTotalSize);
                data.totalSize  = tmpTotalSize;

                data.folder     = Path.Combine(bitSwarm.Options.FolderComplete  , Utils.GetValidPathName(file.name));
                data.folderTemp = Path.Combine(bitSwarm.Options.FolderIncomplete, Utils.GetValidPathName(file.name));

                if (Directory.Exists(data.folder))      bitSwarm.StopWithError($"Torrent folder already exists! {data.folder}");
                if (Directory.Exists(data.folderTemp))  Directory.Delete(data.folderTemp, true);

                opt.Folder      = data.folder;
                opt.PartFolder  = data.folderTemp;

                for (int i=0; i<file.paths.Count; i++)
                {
                    data.files[i] = new Partfile(file.paths[i], file.pieceLength, file.lengths[i], opt);
                    data.filesIncludes.Add(file.paths[i]);
                }
            }
            else
            {
                file.length     = (BNumber) bInfo["length"];  
                data.totalSize  = file.length;
                data.files      = new Partfile[1];

                string filePath = Path.Combine(bitSwarm.Options.FolderComplete  , Utils.GetValidFileName(file.name));
                if (File.Exists(filePath)) bitSwarm.StopWithError($"Torrent file already exists! {filePath}");

                opt.Folder          = bitSwarm.Options.FolderComplete;
                opt.PartFolder      = bitSwarm.Options.FolderIncomplete;
                opt.PartOverwrite   = true;

                data.files[0]       = new Partfile(Utils.GetValidFileName(file.name), file.pieceLength, file.length, opt);

                file.paths          = new List<string>()    { file.name     };
                file.lengths        = new List<long>()      { file.length   };

                data.filesIncludes.Add(file.name);
            }

            data.pieces         = file.pieces.Count;
            data.pieceSize      = file.pieceLength;
            data.pieceLastSize  = (int) (data.totalSize % data.pieceSize); // NOTE: it can be 0, it should be equals with pieceSize in case of totalSize % pieceSize = 0

            data.blockSize      = Math.Min(Peer.MAX_DATA_SIZE, data.pieceSize);
            data.blocks         = ((data.pieceSize -1)      / data.blockSize) + 1;
            data.blockLastSize  = data.pieceLastSize % data.blockSize == 0 ? data.blockSize : data.pieceLastSize % data.blockSize;
            data.blocksLastPiece= ((data.pieceLastSize -1)  / data.blockSize) + 1;

            data.progress       = new Bitfield(data.pieces);
            data.requests       = new Bitfield(data.pieces);
            data.progressPrev   = new Bitfield(data.pieces);
            data.pieceProgress  = new Dictionary<int, TorrentData.PieceProgress>();

            SaveSession();
        }
        
        public void FillFromSession()
        {
            data.files          = new Partfile[file.paths == null ? 1 : file.paths.Count];
            data.filesIncludes  = new List<string>();
            
            Partfiles.Options opt = new Partfiles.Options();
            opt.AutoCreate      = true;

            if (data.folder != null)
            {
                data.folder     = Path.Combine(bitSwarm.Options.FolderComplete  , Utils.GetValidPathName(file.name));
                data.folderTemp = Path.Combine(bitSwarm.Options.FolderIncomplete, Utils.GetValidPathName(file.name));

                opt.Folder      = data.folder;

                for (int i=0; i<file.paths.Count; i++)
                {
                    if (!File.Exists(Path.Combine(data.folder, file.paths[i])) && File.Exists(Path.Combine(data.folderTemp, file.paths[i] + opt.PartExtension)))
                        data.files[i] = new Partfile(Path.Combine(data.folderTemp, file.paths[i] + opt.PartExtension), true, opt);

                    data.filesIncludes.Add(file.paths[i]);
                }
            }
            else
            {
                opt.Folder = bitSwarm.Options.FolderComplete;

                string validFilename = Utils.GetValidFileName(file.name);

                if (!File.Exists(Path.Combine(bitSwarm.Options.FolderComplete, validFilename)) && File.Exists(Path.Combine(bitSwarm.Options.FolderIncomplete, validFilename + opt.PartExtension)))
                    data.files[0] = new Partfile(Path.Combine(bitSwarm.Options.FolderIncomplete, validFilename + opt.PartExtension), true, opt);
                data.filesIncludes.Add(file.name);
            }

            bool allNull = true;
            for(int i=0; i<data.files.Length; i++)
                if (data.files[i] != null) { allNull = false; break; }

            if (allNull) throw new Exception($"The loaded session either is already completed or is invalid (Session File: {bitSwarm.LoadedSessionFile})");

            data.progress       = new Bitfield(data.pieces);
            data.progressPrev   = new Bitfield(data.pieces);
            data.pieceProgress  = new Dictionary<int, TorrentData.PieceProgress>();

            long curSize        = 0;
            int  curFile        = 0;
            int  prevFile       =-1;
            long firstByte;

            for (int piece =0; piece<data.pieces; piece++)
            {
                firstByte  = (long)piece * file.pieceLength;

                for (int i=curFile; i<file.lengths.Count; i++)
                {
                    curFile = i;

                    if (prevFile != curFile) curSize += file.lengths[i];

                    if (firstByte < curSize)
                    {
                        int chunkId = (int) (((firstByte + file.pieceLength - 1) - (curSize - file.lengths[i]))/file.pieceLength);

                        if (data.files[i] == null || data.files[i].MapChunkIdToChunkPos.ContainsKey(chunkId))
                        {
                            if (piece == data.pieces -1)
                                bitSwarm.Stats.BytesDownloadedPrevSession += data.pieceLastSize;
                            else
                                bitSwarm.Stats.BytesDownloadedPrevSession += data.pieceSize;

                            data.progress.SetBit(piece);
                        }

                        prevFile = curFile;
                        break;
                    }
                    prevFile = curFile;
                }
            }

            data.requests = data.progress.Clone();
        }
        public void SaveSession()
        {
            FileStream fs = null;
            try
            {
                BinaryFormatter formatter = new BinaryFormatter();
                string sessionFilePath = Path.Combine(bitSwarm.Options.FolderSessions, file.infoHash.ToUpper() + ".bsf");
                fs = new FileStream(sessionFilePath, FileMode.Create);
                formatter.Serialize(fs, this);
            }
            catch (Exception e) {  bitSwarm.StopWithError($"SaveSession(): {e.Message} - {e.StackTrace}"); }

            fs?.Close();
        }
        public static Torrent LoadSession(string sessionFile)
        {
            FileStream fs = null;
            try
            {
                BinaryFormatter formatter = new BinaryFormatter();
                fs = new FileStream(sessionFile, FileMode.Open);

                Torrent tr = (Torrent) formatter.Deserialize(fs);
                fs?.Close();
                return tr;
            }
            catch (Exception e) { Console.WriteLine(e.Message); }

            return null;
        }
        public static List<string> GetPathsFromInfo(BDictionary info)
        {
            BList files = (BList) info["files"];
            if (files == null) return null;

            List<string> fileNames = new List<string>();

            for (int i=0; i<files.Count; i++)
            {
                BDictionary bdic = (BDictionary) files[i];
                BList path = (BList) bdic["path"];
                string fileName = "";
                for (int l=0; l<path.Count; l++)
                    fileName +=  path[l] + "\\";
                fileNames.Add(fileName.Substring(0, fileName.Length-1));
            }

            return fileNames;
        }
        public static List<long> GetFileLengthsFromInfo(BDictionary info, out long totalSize)
        {
            totalSize = 0;

            BList files = (BList) info["files"];
            if (files == null) return null;
            List<long> lens = new List<long>();
            
            for (int i=0; i<files.Count; i++)
            {
                BDictionary bdic = (BDictionary) files[i];
                long len = (BNumber) bdic["length"];
                totalSize += len;
                lens.Add(len);
            }

            return lens;
        }
        public static List<byte[]> GetHashesFromInfo(BDictionary info)
        {
            byte[] hashBytes = ((BString) info["pieces"]).Value.ToArray();
            List<byte[]> hashes = new List<byte[]>();

            for (int i=0; i<hashBytes.Length; i+=20)
                hashes.Add(Utils.ArraySub(ref hashBytes, (uint) i, 20));
                
            return hashes;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        // Clean Files (Partfiles will be deleted based on options)
                        if (data.files != null)
                            foreach (Partfile file in data.files)
                                file?.Dispose();

                        // Delete Completed Folder (If Empty)
                        if (data.folder != null && Directory.Exists(data.folder) && Directory.GetFiles(data.folder, "*", SearchOption.AllDirectories).Length == 0)
                            Directory.Delete(data.folder, true);

                        // Delete Temp Folder (If Empty)
                        if (data.folderTemp != null && Directory.Exists(data.folderTemp) && Directory.GetFiles(data.folderTemp, "*", SearchOption.AllDirectories).Length == 0)
                            Directory.Delete(data.folderTemp, true);
                    } catch (Exception) { }
                    
                }

                disposedValue = true;
            }
        }
        public void Dispose() { Dispose(true); }
        #endregion
    }
}
