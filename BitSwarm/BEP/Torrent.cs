using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

using BencodeNET.Parsing;
using BencodeNET.Objects;

using SuRGeoNix.Partfiles;

namespace SuRGeoNix.BitSwarmLib.BEP
{
    /// <summary>
    /// BitSwarm's Torrent
    /// </summary>
    public class Torrent : IDisposable
    {
        internal        BitSwarm        bitSwarm;
        private static  BencodeParser   bParser = new BencodeParser();
        private static  SHA1            sha1    = new SHA1Managed();
        
        /// <summary>
        /// Fields of .torrent file (extracted from bencoded data)
        /// </summary>
        public TorrentFile  file;

        /// <summary>
        /// Torrent data
        /// </summary>
        public TorrentData  data;

        /// <summary>
        /// Metadata
        /// </summary>
        public MetaData     metadata;

        /// <summary>
        /// Fields of .torrent file (extracted from bencoded data)
        /// </summary>
        public class TorrentFile
        {
            /// <summary>
            /// SHA1 Hash computation of 'info' part
            /// </summary>
            public string           infoHash        { get; set; }

            /// <summary>
            /// List of trackers extracted from 'announce' | 'announce-list'
            /// </summary>
            public List<Uri>        trackers        { get; set; }

            /// <summary>
            /// Torrent name and file name in case of single file
            /// </summary>
            public string           name            { get; set; }

            /// <summary>
            /// Torrent size (bytes) and file size in case of single file
            /// </summary>
            public long             length          { get; set; }

            // ['path' | 'length']

            /// <summary>
            /// List of relative paths (in case of multi-file)
            /// </summary>
            public List<string>     paths           { get; set; }

            /// <summary>
            /// List of sizes (bytes) for paths with the same array index (in case of multi-file)
            /// </summary>
            public List<long>       lengths         { get; set; }

            /// <summary>
            /// Piece size (bytes)
            /// </summary>
            public int              pieceLength     { get; set; }

            /// <summary>
            /// List of SHA1 Hashes for all torrent pieces
            /// </summary>
            public List<byte[]>     pieces;

            /// <summary>
            /// Gets the array index of the specified file
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            public int GetFileIndex(string filename)
            {
                for (int i=0; i<paths.Count; i++)
                    if (paths[i].ToLower() == filename.ToLower()) return i;

                return -1;
            }

            /// <summary>
            /// Gets the size of the specified file
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            public long GetLength(string filename)
            {
                int ret = GetFileIndex(filename);
                if (ret == -1) return -1;

                return lengths[ret];
            }

            /// <summary>
            /// Gets file position in the torrent
            /// </summary>
            /// <param name="filename"></param>
            /// <returns></returns>
            public long GetStartPosition(string filename)
            {
                long startPos = 0;

                for (int i=0; i<paths.Count; i++)
                {
                    if (paths[i].ToLower() == filename.ToLower()) return startPos;
                    startPos += lengths[i];
                }

                return -1;
            }

            public List<int> GetFilesFromPiece(int piece)
            {
                List<int> files = new List<int>();

                long piecePos = (long)piece * pieceLength;

                long filePos = 0;
                for (int i=0; i<lengths.Count; i++)
                {
                    if ((filePos >= piecePos && filePos < piecePos + pieceLength) ||
                        (filePos + lengths[i] >= piecePos && filePos + lengths[i] < piecePos + pieceLength))
                        files.Add(i);
                    else if (files.Count > 0)
                        break;

                    filePos += lengths[i];
                }

                return files;
            }
        }

        /// <summary>
        /// Torrent data
        /// </summary>
        public class TorrentData
        {
            /// <summary>
            /// Whether the torrent data have been completed successfully
            /// </summary>
            public bool             isDone          { get; set; }

            /// <summary>
            /// List of APF incomplete / part files that required to create the completed files
            /// </summary>
            public Partfile[]       files;

            /// <summary>
            /// List of curerent included files
            /// </summary>
            public List<string>     filesIncludes   { get; set; }

            /// <summary>
            /// Folder where the completed files will be saved (Same as Options.FolderComplete in case of single file, otherwise Options.FolderComplete + Torrent.Name)
            /// </summary>
            public string           folder          { get; set; }

            /// <summary>
            /// Folder where the incomplete / part files will be saved (Same as Options.FolderIncomplete in case of single file, otherwise Options.FolderIncomplete + Torrent.Name)
            /// </summary>
            public string           folderTemp      { get; set; }

            /// <summary>
            /// Total torrent size (bytes)
            /// </summary>
            public long             totalSize       { get; set; }

            /// <summary>
            /// Total pieces
            /// </summary>
            public int              pieces          { get; set; }

            /// <summary>
            /// Piece size (bytes)
            /// </summary>
            public int              pieceSize       { get; set; }

            /// <summary>
            /// Last piece size (bytes)
            /// </summary>
            public int              pieceLastSize   { get; set; } // NOTE: it can be 0, it should be equals with pieceSize in case of totalSize % pieceSize = 0

            /// <summary>
            /// Total blocks
            /// </summary>
            public int              blocks          { get; set; }

            /// <summary>
            /// Block size (bytes)
            /// </summary>
            public int              blockSize       { get; set; }

            /// <summary>
            /// Last block size (bytes)
            /// </summary>
            public int              blockLastSize   { get; set; }

            /// <summary>
            /// Last block size (bytes)
            /// </summary>
            public int              blockLastSize2   { get; set; }

            /// <summary>
            /// Blocks of last piece
            /// </summary>
            public int              blocksLastPiece { get; set; }

            /// <summary>
            /// Progress bitfield (received pieces)
            /// </summary>
            public Bitfield         progress;

            /// <summary>
            /// Requests bitfield (requested pieces)
            /// </summary>
            public Bitfield         requests;

            /// <summary>
            /// Previous progress bitfield (received pieces).
            /// Required for include / exclude files cases
            /// </summary>
            public Bitfield         progressPrev;

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

        /// <summary>
        /// Metadata
        /// </summary>
        public class MetaData
        {
            /// <summary>
            /// Whether the metadata have been received successfully
            /// </summary>
            public bool             isDone          { get; set; }

            /// <summary>
            /// Incomplete / part file for .torrent
            /// </summary>
            public Partfile         file;

            /// <summary>
            /// Total pieces
            /// </summary>
            public int              pieces          { get; set; }

            /// <summary>
            /// Total size (bytes)
            /// </summary>
            public long             totalSize       { get; set; }

            /// <summary>
            /// Progress bitfield (received pieces)
            /// </summary>
            public Bitfield         progress;
        }
        public Torrent (BitSwarm bitSwarm) 
        {
            this.bitSwarm       = bitSwarm;
            file                = new TorrentFile();
            data                = new TorrentData();
            metadata            = new MetaData();
            file.trackers       = new List<Uri>();
        }

        public TorrentStream GetTorrentStream(string filename)
        {
            int fileIndex = file.GetFileIndex(filename);
            if (fileIndex == -1 || data.files[file.GetFileIndex(filename)] == null) return null;
            

            return new TorrentStream(this, data.files[file.GetFileIndex(filename)], file.GetStartPosition(filename));
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

            string torrentFile =  Path.Combine(bitSwarm.OptionsClone.FolderTorrents, Utils.GetValidPathName(file.name) + ".torrent");

            if (File.Exists(torrentFile))
                FillFromTorrentFile(torrentFile);

        }
        public void FillFromTorrentFile(string fileName)
        {
            try
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

                PrepareFiles(bInfo);
            } catch (Exception e) { bitSwarm.StopWithError($"FillFromTorrentFile(): {e.Message} - {e.StackTrace}"); }
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

                if (file.infoHash.ToLowerInvariant() != Utils.ArrayToStringHex(sha1.ComputeHash(bInfo.EncodeAsBytes())).ToLowerInvariant())
                    bitSwarm.StopWithError("[CRITICAL] Metadata SHA1 validation failed");

                file.name = ((BString) bInfo["name"]).ToString();

                string torrentName  = Utils.GetValidFileName(file.name) + ".torrent";

                if (!File.Exists(Path.Combine(curPath, torrentName)))
                    File.Move(curFilePath, Path.Combine(bitSwarm.OptionsClone.FolderTorrents, torrentName));

                PrepareFiles(bInfo);
            } catch (Exception e) { bitSwarm.StopWithError($"FillFromMetadata(): {e.Message} - {e.StackTrace}"); }
            
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

        public void PrepareFiles(BDictionary bInfo)
        {
            bool newDownload = true;
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

                data.folder     = Path.Combine(bitSwarm.OptionsClone.FolderComplete  , Utils.GetValidPathName(file.name));
                data.folderTemp = Path.Combine(bitSwarm.OptionsClone.FolderIncomplete, Utils.GetValidPathName(file.name));

                opt.Folder      = data.folder;
                opt.PartFolder  = data.folderTemp;

                for (int i=0; i<file.paths.Count; i++)
                {
                    if (!File.Exists(Path.Combine(opt.Folder, file.paths[i])))
                    {
                        if (File.Exists(Path.Combine(opt.PartFolder, file.paths[i]) + opt.PartExtension))
                        {
                            data.files[i] = new Partfile(Path.Combine(opt.PartFolder, file.paths[i] + opt.PartExtension), true, opt);
                            newDownload = false;
                        }
                        else
                            data.files[i] = new Partfile(file.paths[i], file.pieceLength, file.lengths[i], opt);

                        data.filesIncludes.Add(file.paths[i]);
                    }
                    else
                        newDownload = false;
                }
            }
            else
            {
                file.length     = (BNumber) bInfo["length"];  
                file.lengths    = new List<long>()      { file.length   };
                file.paths      = new List<string>()    { file.name     };

                data.totalSize  = file.length;
                data.files      = new Partfile[1];

                string validFileName= Utils.GetValidFileName(file.name);

                opt.Folder          = bitSwarm.OptionsClone.FolderComplete;
                opt.PartFolder      = bitSwarm.OptionsClone.FolderIncomplete;
                opt.PartOverwrite   = true;

                if (!File.Exists(Path.Combine(opt.Folder, validFileName)))
                {
                    if (File.Exists(Path.Combine(opt.PartFolder, validFileName) + opt.PartExtension))
                    {
                        data.files[0] = new Partfile(Path.Combine(opt.PartFolder, validFileName + opt.PartExtension), true, opt);
                        newDownload = false;
                    }
                    else
                        data.files[0] = new Partfile(validFileName, file.pieceLength, file.length, opt);

                    data.filesIncludes.Add(file.name);
                }
                else
                    newDownload = false;
            }

            data.pieces         = file.pieces.Count;
            data.pieceSize      = file.pieceLength;
            data.pieceLastSize  = (int) (data.totalSize % data.pieceSize); // NOTE: it can be 0, it should be equals with pieceSize in case of totalSize % pieceSize = 0

            data.blockSize      = Math.Min(Peer.MAX_DATA_SIZE, data.pieceSize);
            data.blocks         = ((data.pieceSize -1)      / data.blockSize) + 1;
            data.blockLastSize  = data.pieceLastSize % data.blockSize == 0 ? data.blockSize : data.pieceLastSize % data.blockSize;
            data.blockLastSize2 = data.pieceSize % data.blockSize == 0 ? data.blockSize : data.pieceSize % data.blockSize;
            data.blocksLastPiece= ((data.pieceLastSize -1)  / data.blockSize) + 1;

            data.progress       = new Bitfield(data.pieces);
            data.requests       = new Bitfield(data.pieces);
            data.progressPrev   = new Bitfield(data.pieces);
            data.pieceProgress  = new Dictionary<int, TorrentData.PieceProgress>();

            if (!newDownload)
            {
                LoadProgress();

                data.requests = data.progress.Clone();

                if (data.progress.GetFirst0() == -1)
                    data.isDone = true;
            }
            
        }
        private void LoadProgress()
        {
            if (data.filesIncludes.Count == 0)
            {
                data.progress.SetAll();
                return;
            }

            int piece = 0;
            int modulo = 0;
            int chunkId;
            bool pieceMissing = false;

            for (int fileId=0; fileId<file.lengths.Count; fileId++) // total files
            {
                for (chunkId=0; chunkId<(file.lengths[fileId]+modulo-1)/data.pieceSize; chunkId++) // total file's chunks (-1)
                {
                    if (!pieceMissing && (data.files[fileId] == null || data.files[fileId].MapChunkIdToChunkPos.ContainsKey(chunkId)))
                        data.progress.SetBit(piece);

                    pieceMissing = false;
                    piece++;
                }

                modulo += (int) (file.lengths[fileId] % data.pieceSize);

                if (modulo == 0)
                {
                    if (!pieceMissing && (data.files[fileId] == null || data.files[fileId].MapChunkIdToChunkPos.ContainsKey(chunkId)))
                        data.progress.SetBit(piece);

                    pieceMissing = false;
                    piece++;
                }
                else
                {
                    if (data.files[fileId] != null && !data.files[fileId].MapChunkIdToChunkPos.ContainsKey(chunkId))
                        pieceMissing = true;

                    if (modulo > data.pieceSize)
                        modulo %= data.pieceSize;
                }
            }

            if (modulo != 0 && !pieceMissing)
                data.progress.SetBit(piece);
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
        public bool Disposed { get; private set; }
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    try
                    {
                        // Clean Files (Partfiles will be deleted based on options)
                        if (data.files != null)
                            foreach (Partfile file in data.files)
                                file?.Dispose();

                        if (data.pieceProgress != null) data.pieceProgress.Clear();

                        // Delete Completed Folder (If Empty)
                        if (data.folder != null && Directory.Exists(data.folder) && Directory.GetFiles(data.folder, "*", SearchOption.AllDirectories).Length == 0)
                            Directory.Delete(data.folder, true);

                        // Delete Temp Folder (If Empty)
                        if (data.folderTemp != null && Directory.Exists(data.folderTemp) && Directory.GetFiles(data.folderTemp, "*", SearchOption.AllDirectories).Length == 0)
                            Directory.Delete(data.folderTemp, true);

                        data = new TorrentData();
                        file = new TorrentFile();
                        metadata = new MetaData();
                        bitSwarm = null;
                        GC.Collect();
                    } catch (Exception)  { }
                }

                Disposed = true;
            }
        }
        public void Dispose() { lock (this) Dispose(true); }
        #endregion
    }

    public class TorrentStream : PartStream
    {
        public long     StartPos    { get; set; }
        public long     EndPos      { get; set; }
        public int      LastPiece   { get; set; }

        Torrent  torrent;
        bool cancel;

        public TorrentStream(Torrent torrent, Partfile pf, long distance) : base(pf)
        {
            this.torrent = torrent;
            StartPos = torrent.file.GetStartPosition(pf.Filename);
            EndPos = StartPos + Length;
            LastPiece = FilePosToPiece(Length);
        }

        public void Cancel() { cancel = true; }

#if NET5_0_OR_GREATER
        /// <summary>
        /// Reads the specified span bytes from the part file until they are available or cancel
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public override int Read(Span<byte> buffer)
        {
            int startPiece  = FilePosToPiece(Position);
            int endPiece    = FilePosToPiece(Position + buffer.Length);

            if (!torrent.bitSwarm.FocusAreInUse)
                torrent.bitSwarm.FocusArea = new Tuple<int, int>(startPiece, LastPiece); // Set every time to follow the decoder?

            if (torrent.data.progress.GetFirst0(startPiece, endPiece) != -1)
            {
                lock(torrent) // Only one FA can be active
                {
                    cancel = false;
                    torrent.bitSwarm.FocusAreInUse = true;
                    torrent.bitSwarm.Log($"[FOCUS {startPiece} - {endPiece}] Buffering Started");

                    while (torrent.data.progress.GetFirst0(startPiece, endPiece) != -1 && !cancel && torrent.bitSwarm.isRunning)
                    {
                        if (torrent.bitSwarm.focusArea != null && torrent.bitSwarm.focusArea.Item1 != startPiece && torrent.data.requests.GetFirst0(startPiece, endPiece) != -1)
                            torrent.bitSwarm.FocusArea = new Tuple<int, int>(startPiece, LastPiece);
                        Thread.Sleep(25);
                    }

                    torrent.bitSwarm.FocusAreInUse = false;

                    if (cancel || !torrent.bitSwarm.isRunning)
                    {
                        torrent.bitSwarm.Log($"[FOCUS {startPiece} - {endPiece}] Buffering Cancel");
                        return -1;
                    }
                    else
                        torrent.bitSwarm.Log($"[FOCUS {startPiece} - {endPiece}] Buffering Done");
                }
            }

            return base.Read(buffer);
        }
#endif

        /// <summary>
        /// Reads the specified actual bytes from the part file until they are available or cancel
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns>Bytes read size | -2 on Error | -1 on Cancel</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int startPiece  = FilePosToPiece(Position);
            int endPiece    = FilePosToPiece(Position + count);

            if (!torrent.bitSwarm.FocusAreInUse)
                torrent.bitSwarm.FocusArea = new Tuple<int, int>(startPiece, LastPiece); // Set every time to follow the decoder?

            if (torrent.data.progress.GetFirst0(startPiece, endPiece) != -1)
            {
                lock(torrent) // Only one FA can be active
                {
                    cancel = false;
                    torrent.bitSwarm.FocusAreInUse = true;
                    torrent.bitSwarm.Log($"[FOCUS {startPiece} - {endPiece}] Buffering Started");

                    while (torrent.data.progress.GetFirst0(startPiece, endPiece) != -1 && !cancel && torrent.bitSwarm.isRunning)
                    {
                        if (torrent.bitSwarm.focusArea != null && torrent.bitSwarm.focusArea.Item1 != startPiece && torrent.data.requests.GetFirst0(startPiece, endPiece) != -1)
                            torrent.bitSwarm.FocusArea = new Tuple<int, int>(startPiece, LastPiece);
                        Thread.Sleep(25);
                    }

                    torrent.bitSwarm.FocusAreInUse = false;

                    if (cancel || !torrent.bitSwarm.isRunning)
                    {
                        torrent.bitSwarm.Log($"[FOCUS {startPiece} - {endPiece}] Buffering Cancel");
                        return -1;
                    }
                    else
                        torrent.bitSwarm.Log($"[FOCUS {startPiece} - {endPiece}] Buffering Done");
                }
            }

            return base.Read(buffer, offset, count);
        }

        public int FilePosToPiece(long filePos)
        {
            int piece = (int)((StartPos + filePos) / torrent.file.pieceLength);
            if (piece >= torrent.data.pieces) piece = torrent.data.pieces - 1;

            return piece;
        }

    }
}
