using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;

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
        private static readonly object fileCreating = new object();

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
                if ( chunksCounter == -1 && chunkId == 0) { WriteFirst(chunk, offset, len); return; } // WriteLast as WriteFirst

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

        public byte[] Read(long pos, long size)
        {
            lock (fileCreating)
            {
                if (firstPos == -1) return null; // Possible allow it for firstChunkSize == chunkSize

                byte[] retData = null;

                if (FileCreated)
                {
                    retData = new byte[size];
                    fileStream.Seek(pos, SeekOrigin.Begin);
                    fileStream.Read(retData, 0, (int)size);
                    return retData;
                }
                
                long writeSize;
                long startByte;
                byte[] curChunk;
                long sizeLeft = size;

                int chunkId     = (int)((pos - firstChunkSize) / ChunkSize) + 1;
                int lastChunkId = (int)((Size - firstChunkSize - 1) / ChunkSize) + 1;
                
                if (pos < firstChunkSize)
                {
                    chunkId     = 0;
                    startByte   = pos;
                    writeSize   = Math.Min(sizeLeft, firstChunkSize - startByte);
                }
                else if (chunkId == lastChunkId && lastPos != -1)
                {
                    startByte   = ((pos - firstChunkSize) % ChunkSize);
                    writeSize   = Math.Min(sizeLeft, lastChunkSize - startByte);
                }
                else
                {
                    startByte   = ((pos - firstChunkSize) % ChunkSize);
                    writeSize   = Math.Min(sizeLeft, ChunkSize - startByte);
                }

                // For progress.GetFirst0() == -1 but didnt have enough time to write it in the file | Also for ThreadAborts
                try
                {
                    curChunk = ReadChunk(chunkId);
                    retData = Utils.ArraySub(ref curChunk, (uint)startByte, (uint)writeSize);
                    sizeLeft -= writeSize;
                }
                catch (ThreadAbortException t)
                {
                    throw t;
                }
                catch (Exception) { }

                while (sizeLeft > 0)
                {
                    try
                    {
                        chunkId++;

                        curChunk = ReadChunk(chunkId);
                        if (chunkId == lastChunkId && lastPos != -1)
                            writeSize = (uint)Math.Min(sizeLeft, lastChunkSize);
                        else
                            writeSize = (uint)Math.Min(sizeLeft, ChunkSize);
                        retData = Utils.ArrayMerge(retData, Utils.ArraySub(ref curChunk, 0, (uint)writeSize));
                        sizeLeft -= writeSize;
                    }
                    catch (ThreadAbortException t)
                    {
                        throw t;
                    }
                    catch (Exception) { }
                }

                return retData;
            }
        }
        public byte[] ReadChunk(int chunkId)
        {
            lock (locker)
            {
                if (FileCreated) return null;

                int len = ChunkSize;
                if (chunkId == 0 && firstChunkSize != 0)
                    len = firstChunkSize;
                else if (chunkId == chunksCounter && lastChunkSize != 0)
                    len = lastChunkSize;

                int chunkPos    = mapIdToChunkId[chunkId];
                int chunkPos2   = chunkPos;
                long pos        = 0;
                if (firstChunkSize != 0 && chunkPos > firstPos) { pos += firstChunkSize; chunkPos2--; }
                if (lastChunkSize  != 0 && chunkPos > lastPos ) { pos += lastChunkSize;  chunkPos2--; }
                pos += (long)ChunkSize * chunkPos2;

                byte[] data = new byte[len];
                long savePos = fileStream.Position;
                fileStream.Seek(pos, SeekOrigin.Begin);
                fileStream.Read(data, 0, len);
                fileStream.Seek(savePos, SeekOrigin.Begin);

                return data;
            }
        }

        public void CreateFile()
        {
            // MR Testing (Disabled it to work only with part file)
            //if ( !force ) return;
            lock (locker)
            {
                lock (fileCreating)
                {
                    if (FileCreated) return;

                    using (FileStream fs = File.Open(FileName, FileMode.CreateNew))
                    {
                        if (Size > 0)
                        {
                            for (int i = 0; i <= chunksCounter; i++)
                            {
                                byte[] data = ReadChunk(i);
                                fs.Write(data, 0, data.Length);
                            }
                        }

                        FileCreated = true;
                    }
                    fileStream.Close();
                    fileStream = File.Open(FileName, FileMode.Open, FileAccess.Read);
                    File.Delete(FileName + ".part");
                    
                }
            }
        }
        public void CloseFile()
        {
            if (fileStream != null)  fileStream.Close(); 
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