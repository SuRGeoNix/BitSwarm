using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SuRGeoNix
{
    public class Logger
    {
        public string       FileName        { get; private set; }
        
        private FileStream  fileStream;
        private Stopwatch   sw;

        private bool disposed = false;
        private readonly object locker = new object();

        public Logger(string fileName, bool start = false) 
        {
            FileName = fileName;
            fileStream = new FileStream(fileName, FileMode.Append, FileAccess.Write);
            sw = new Stopwatch();
            if ( start ) sw.Start();
        }

        public void RestartTime()
        {
            sw.Restart();
        }
        public long GetTime()
        {
            return sw.ElapsedMilliseconds;
        }

        public void Write(string txt)
        {
            if ( sw.IsRunning )
                Write1(txt);
            else
                Write2(txt);
        }
        public void Write1(string txt)
        {
            byte[] data = Encoding.UTF8.GetBytes($"[{sw.Elapsed.ToString(@"hh\:mm\:ss\:fff")}] {txt}\r\n");
            lock ( locker ) { if ( disposed ) return; fileStream.Write(data, 0, data.Length); }
        }
        public void Write2(string txt)
        {
            if ( disposed ) return;

            byte[] data = Encoding.UTF8.GetBytes($"[{DateTime.Now.ToString("H.mm.ss.ffff")}] {txt}\r\n");
            lock ( locker ) { if ( disposed ) return; fileStream.Write(data, 0, data.Length); }
        }

        public void Dispose()
        {
            if ( disposed ) return;

            lock ( locker )
            {
                disposed = true;
                fileStream?.Flush();
                fileStream?.Close();
                sw?.Stop();
            }
        }
    }
}