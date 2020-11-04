using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web;

using BencodeNET.Parsing;
using BencodeNET.Objects;

namespace SuRGeoNix.BEP
{
    public class Torrent : IDisposable
    {
        public TorrentFile          file;
        public TorrentData          data;
        public MetaData             metadata;

        public string               DownloadPath    { get; set; }
        public bool                 isMultiFile     { get; private set; }

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
            public List<byte[]>     pieces          { get; set; }
        }
        public struct TorrentData
        {
            public bool             isDone          { get; set; }

            public List<PartFile>   files           { get; set; }
            public List<string>     filesIncludes   { get; set; }
            public string           folder          { get; set; }
            public long             totalSize       { get; set; }

            public int              pieces          { get; set; }
            public int              pieceSize       { get; set; }
            public int              pieceLastSize   { get; set; }

            public int              blocks          { get; set; }
            public int              blockSize       { get; set; }
            public int              blockLastSize   { get; set; }
            public int              blocksLastPiece { get; set; }

            public BitField         progress        { get; set; }
            public BitField         requests        { get; set; }
            public BitField         progressPrev    { get; set; }
            public BitField         requestsPrev    { get; set; }


            public Dictionary<int, PieceProgress>   pieceProgress;
            public List<PieceRequest>               pieceRequests;

            public class PieceProgress
            {
                public PieceProgress(ref TorrentData data, int piece)
                {
                    bool isLastPiece= piece == data.pieces - 1 && data.totalSize % data.pieceSize != 0;

                    this.piece      = piece;
                    this.data       = !isLastPiece ? new byte[data.pieceSize] : new byte[data.pieceLastSize];
                    this.progress   = !isLastPiece ? new BitField(data.blocks): new BitField(data.blocksLastPiece);
                    this.requests   = !isLastPiece ? new BitField(data.blocks): new BitField(data.blocksLastPiece);
                }
                public int          piece;
                public byte[]       data;
                public BitField     progress;
                public BitField     requests;
            }
            public class PieceRequest
            {
                public PieceRequest(long timestamp, Peer peer, int piece, int block, int size, bool aggressive = false)
                {
                    this.timestamp  = timestamp;
                    this.peer       = peer;
                    this.piece      = piece;
                    this.block      = block;
                    this.size       = size;
                    this.aggressive = aggressive;
                }
                public long         timestamp;
                public Peer         peer;
                public int          piece;
                public int          block;
                public int          size;
                public bool         aggressive;
            }
        }
        public struct MetaData
        {
            public bool             isDone          { get; set; }

            public PartFile         file            { get; set; }
            public int              pieces          { get; set; }
            public long             totalSize       { get; set; }

            public BitField         progress        { get; set; }

            public int              parallelRequests { get; set; }
        }

        private static BencodeParser    bParser = new BencodeParser();
        public  static SHA1             sha1    = new SHA1Managed();

        public Torrent (string downloadPath) 
        {
            DownloadPath        = downloadPath;

            file                = new TorrentFile();
            data                = new TorrentData();
            metadata            = new MetaData();

            file.trackers       = new List<Uri>();
            data.pieceRequests  = new List<TorrentData.PieceRequest>();
        }

        public void FillTrackersFromTrackersPath(string fileName)
        {
            try
            {
                if (fileName == null || fileName.Trim() == "" || !File.Exists(fileName)) return;

                string[] trackers = File.ReadAllLines(fileName);

                foreach (var tracker in trackers)
                    if (!file.trackers.Contains(new Uri(tracker))) file.trackers.Add(new Uri(tracker));

            } catch (Exception) { }
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

            // Base32 Hash
            if (file.infoHash.Length != 40)
            {
                if (Regex.IsMatch(file.infoHash,@"[QAZ2WSX3EDC4RFV5TGB6YHN7UJM8K9LP]+"))
                {
                    try
                    {
                        file.infoHash = Utils.ArrayToStringHex(Utils.FromBase32String(file.infoHash));
                        if (file.infoHash.Length != 40) throw new Exception("[Magnet][xt] No valid hash found " + magnetLink);
                    } catch (Exception) { throw new Exception("[Magnet][xt] No valid hash found " + magnetLink); }   
                } else { throw new Exception("[Magnet][xt] No valid hash found " + magnetLink); }
            }

            string[] tr = nvc.Get("tr") == null ? null  : nvc.GetValues("tr");
            if (tr == null) return;

            for (int i=0; i<tr.Length; i++)
                if (!file.trackers.Contains(new Uri(tr[i]))) file.trackers.Add(new Uri(tr[i]));
        }
        public void FillFromTorrentFile(string fileName)
        {
            BDictionary bdicTorrent = bParser.Parse<BDictionary>(fileName);
            BDictionary bInfo;

            if (bdicTorrent["info"] != null)
            {
                bInfo = (BDictionary) bdicTorrent["info"];
                file.trackers = GetTrackersFromTorrent(bdicTorrent);
            } 
            else if (bdicTorrent["name"] != null)
                bInfo = bdicTorrent;
            else
                throw new Exception("Invalid torrent file");

            file.infoHash = Utils.ArrayToStringHex(sha1.ComputeHash(bInfo.EncodeAsBytes()));
            FillFromInfo(bInfo);
        }
        public void FillFromMetadata()
        {
            if (metadata.file == null) throw new Exception("No metadata found");

            BDictionary bInfo = (BDictionary) bParser.Parse(metadata.file.FileName);
            FillFromInfo(bInfo);
        }
        public void FillFromInfo(BDictionary bInfo)
        {
            if (DownloadPath == null) throw new Exception("DownloadPath cannot be empty");

            isMultiFile         = (BList) bInfo["files"] == null ? false : true;

            file.name           = ((BString) bInfo["name"]).ToString();
            file.pieces         = GetHashesFromInfo(bInfo);
            file.pieceLength    = (BNumber) bInfo["piece length"];

            data.files          = new List<PartFile>();
            data.filesIncludes  = new List<string>();

            if (isMultiFile)
            {
                file.paths      = GetPathsFromInfo(bInfo);      long tmpTotalSize;
                file.lengths    = GetFileLengthsFromInfo(bInfo, out  tmpTotalSize);
                data.totalSize  = tmpTotalSize;

                data.folder = Utils.FindNextAvailableDir(Path.Combine(DownloadPath, file.name)).Replace("..","_");
                for (int i=0; i<file.paths.Count; i++)
                {
                    data.files.Add(new PartFile(Utils.FindNextAvailablePartFile(Path.Combine(data.folder, file.paths[i])), file.pieceLength, file.lengths[i]));
                    data.filesIncludes.Add(file.paths[i]);
                }
            }
            else
            {
                file.length     = (BNumber) bInfo["length"];  
                data.totalSize  = file.length;
                data.files.Add(new PartFile(Utils.FindNextAvailablePartFile(Path.Combine(DownloadPath, file.name)), file.pieceLength, file.length));

                file.paths      = new List<string>()    { file.name     };
                file.lengths    = new List<long>()      { file.length   };

                data.filesIncludes.Add(file.name);
            }

            data.pieces         = file.pieces.Count;
            data.pieceSize      = file.pieceLength;
            data.pieceLastSize  = (int) (data.totalSize % data.pieceSize);
            
            data.progress       = new BitField(data.pieces);
            data.requests       = new BitField(data.pieces);
            data.progressPrev   = new BitField(data.pieces);
            data.requestsPrev   = new BitField(data.pieces);

            data.blockSize      = Math.Min(Peer.MAX_DATA_SIZE, data.pieceSize);
            data.blocks         = ((data.pieceSize -1)      / data.blockSize) + 1;
            data.blockLastSize  = data.pieceLastSize % data.blockSize == 0 ? data.blockSize : data.pieceLastSize % data.blockSize;
            data.blocksLastPiece= ((data.pieceLastSize -1)  / data.blockSize) + 1;

            data.pieceProgress  = new Dictionary<int, TorrentData.PieceProgress>();
            data.pieceRequests  = new List<TorrentData.PieceRequest>();
        }

        public static List<Uri> GetTrackersFromTorrent(BDictionary torrent)
        {
            string tracker = null;
            BList trackersBList = null;

            if (torrent["announce"] != null)
                tracker = ((BString) torrent["announce"]).ToString();

            if (torrent["announce-list"] != null)
                trackersBList = (BList) torrent["announce-list"];

            if (trackersBList == null && tracker == null) return new List<Uri>();
            if (trackersBList == null) return new List<Uri>() { new Uri(tracker) };

            List<Uri> trackers = new List<Uri>();

            for (int i=0; i<trackersBList.Count; i++)
                trackers.Add(new Uri(((BString)((BList)trackersBList[i])[0]).ToString()));

            if (tracker != null && !trackers.Contains(new Uri(tracker))) trackers.Add(new Uri(tracker));

            return trackers;
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
                    //sha1.Dispose();

                    if (data.files != null)
                        foreach (PartFile file in data.files)
                            file.Dispose();

                    if (isMultiFile && data.folder != null
                        &&  Directory.Exists(data.folder)
                        &&  Directory.GetFileSystemEntries(data.folder).Length == 0)
                        Directory.Delete(data.folder);
                }

                disposedValue = true;
            }
        }
        public void Dispose() { Dispose(true); }
        #endregion
    }
}
