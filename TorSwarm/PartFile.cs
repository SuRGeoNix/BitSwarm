using System;
using System.IO;
using System.Collections.Generic;

namespace SuRGeoNix.TorSwarm
{
    public class PartFile : IDisposable
    {
        public string       FileName        { get; private set; }
        public long         Size            { get; private set; }
        public int          ChunkSize       { get; private set; }
        public int          chunksCounter   { get; private set; }
        public bool         FileCreated     { get; private set; }
        public bool         AutoCreate      { get { return autoCreate; } set { autoCreate = value; if ( value && fileStream.Length == Size ) CreateFile(); } }
        private bool        autoCreate;

        private FileStream  fileStream;
        private Dictionary<int, int> mapIdToChunkId;

        private int         firstPos;
        private int         firstChunkSize;
        private int         lastPos;
        private int         lastChunkSize;

        private static readonly object locker = new object();

        public PartFile(string fileName, int chunkSize, long size = -1, bool autoCreate = true)
        {
            if ( File.Exists(fileName) || File.Exists(fileName + ".part") ) throw new IOException("File " + fileName + " already exists");
            if ( chunkSize < 1 ) throw new Exception("Chunk size must be > 0");

            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            fileStream = File.Open(fileName + ".part", FileMode.CreateNew);

            FileName   = fileName;
            Size       = size;
            ChunkSize  = chunkSize;
            AutoCreate = autoCreate;

            mapIdToChunkId  = new Dictionary<int, int>();
            chunksCounter   = -1;
            firstChunkSize  =  0;
            lastChunkSize   =  0;
            firstPos        = -1;
            lastPos         = -1;
        }
        public PartFile(string fileName, Dictionary<int, int> mapIdToChunkId, int chunkSize, int firstPos, int firstChunkSize, int lastPos, int lastChunkSize, int chunksCounter)
        {
            if ( !File.Exists(fileName + ".part") ) throw new IOException("File " + fileName + " not exists");
            if ( chunkSize < 1 ) throw new Exception("Chunk size must be > 0");

            fileStream = File.Open(fileName + ".part", FileMode.Open, FileAccess.Read);

            this.mapIdToChunkId  = mapIdToChunkId;
            this.FileName        = fileName;
            this.ChunkSize       = chunkSize;
            this.chunksCounter   = chunksCounter;
            this.firstChunkSize  = firstChunkSize;
            this.lastChunkSize   = lastChunkSize;
            this.firstPos        = firstPos;
            this.lastPos         = lastPos;
        }

        public void Write(int chunkId, byte[] chunk, int offset = 0)
        {
            lock (locker)
            {
                if ( FileCreated ) return;

                fileStream.Write(chunk, offset, (int) ChunkSize);
                fileStream.Flush();
                chunksCounter++;
                mapIdToChunkId.Add(chunkId, chunksCounter);

                if ( AutoCreate && fileStream.Length == Size ) CreateFile();
            }
        }
        public void WriteFirst(byte[] chunk, int offset, int len)
        {
            lock (locker)
            {
                if ( FileCreated ) return;

                if ( firstChunkSize != 0 ) throw new Exception("First chunk already exists");

                fileStream.Write(chunk, offset, (int) len);
                fileStream.Flush();
                chunksCounter++;
                mapIdToChunkId.Add(0, chunksCounter);
                firstChunkSize = len;
                firstPos = chunksCounter;

                if ( AutoCreate && fileStream.Length == Size ) CreateFile();
            }
        }
        public void WriteLast(int chunkId, byte[] chunk, int offset, int len)
        {
            lock (locker)
            {
                if ( FileCreated ) return;

                if ( lastChunkSize != 0 ) throw new Exception("Last chunk already exists");
                
                fileStream.Write(chunk, offset, (int) len);
                fileStream.Flush();
                chunksCounter++;
                mapIdToChunkId.Add(chunkId, chunksCounter);
                lastChunkSize = len;
                lastPos = chunksCounter;

                if ( AutoCreate && fileStream.Length == Size ) CreateFile();
            }
        }

        public byte[] Read(int chunkId)
        {
            lock (locker)
            {
                int len = ChunkSize;
                if ( chunkId == 0 && firstChunkSize != 0 ) 
                    len = firstChunkSize;
                else if ( chunkId == chunksCounter && lastChunkSize != 0 )
                    len = lastChunkSize;

                int chunkPos    = mapIdToChunkId[chunkId];
                int chunkPos2   = chunkPos;
                long pos         = 0;
                if ( firstChunkSize != 0 && chunkPos > firstPos)  { pos += firstChunkSize;    chunkPos2--; }
                if ( lastChunkSize  != 0 && chunkPos > lastPos )  { pos += lastChunkSize;     chunkPos2--; }
                pos += (long)ChunkSize * chunkPos2;

                byte[] data = new byte[len];
                fileStream.Seek(pos, SeekOrigin.Begin);
                fileStream.Read(data, 0, len);

                return data;
            }
        }

        public void CreateFile()
        {
            lock (locker)
            {
                if ( FileCreated ) return;

                using (FileStream fs = File.Open(FileName, FileMode.CreateNew) )
                {
                    for (int i=0; i<=chunksCounter; i++)
                    {
                        byte[] data = Read(i);
                        fs.Write(data,0,data.Length);
                    }

                    FileCreated = true;
                }

                fileStream.Close();
                fileStream = null;
                File.Delete(FileName + ".part");
            }
        }

        public void Dispose()
        {
            bool deleteFile = false;
            if ( fileStream != null && fileStream.Length == 0 ) deleteFile = true;
            if ( fileStream != null ) ((IDisposable)fileStream).Dispose();
            if ( deleteFile ) File.Delete(FileName + ".part");
        }
    }
}