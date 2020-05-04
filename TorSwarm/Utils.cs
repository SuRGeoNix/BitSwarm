using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Security;

using BencodeNET.Parsing;
using BencodeNET.Objects;

namespace SuRGeoNix
{
public class Utils
    {
        // Dir / File Next Available
        public static string FindNextAvailablePartFile(string fileName)
        {
            if ( !File.Exists(fileName) && !File.Exists(fileName + ".part") ) return fileName;

            string tmp = Path.Combine(Path.GetDirectoryName(fileName),Regex.Replace(Path.GetFileNameWithoutExtension(fileName), @"(.*) (\([0-9]+)\)$", "$1"));
            string newName;

            for (int i=1; i<1000; i++)
            {
                newName = tmp  + " (" + i + ")" + Path.GetExtension(fileName);
                if ( !File.Exists(newName) && !File.Exists(newName + ".part") ) return newName;
            }

            return null;
        }
        public static string FindNextAvailableDir(string dir)
        {
            if ( !Directory.Exists(dir) ) return dir;

            string tmp = Regex.Replace(dir, @"(.*)\\+$", "$1");
            tmp = Path.Combine(Path.GetDirectoryName(tmp),Regex.Replace(Path.GetFileName(tmp), @"(.*) (\([0-9]+)\)$", "$1"));
            string newName;

            for (int i=1; i<101; i++)
            {
                newName = tmp  + " (" + i + ")";
                if ( !Directory.Exists(newName) ) return newName;
            }

            return null;
        }
        public static string FindNextAvailableFile(string fileName)
        {
            if ( !File.Exists(fileName) ) return fileName;

            string tmp = Path.Combine(Path.GetDirectoryName(fileName),Regex.Replace(Path.GetFileNameWithoutExtension(fileName), @"(.*) (\([0-9]+)\)$", "$1"));
            string newName;

            for (int i=1; i<101; i++)
            {
                newName = tmp  + " (" + i + ")" + Path.GetExtension(fileName);
                if ( !File.Exists(newName) ) return newName;
            }

            return null;
        }

        // BEncode
        public static object GetFromBDic(BDictionary dic, string[] path)
        {

            return GetFromBDicRec(dic, path, 0);
        }
        private static object GetFromBDicRec(BDictionary dic, string[] path, int level)
        {
            IBObject ibo = dic[path[level]];
            if (ibo == null) return null;

            if (level == path.Length - 1)
            {
                if (ibo.GetType() == typeof(BString))
                    return ibo.ToString();
                else if (ibo.GetType() == typeof(BNumber))
                    return (int)((BNumber)ibo).Value;
                else if (ibo.GetType() == typeof(BDictionary))
                    return ((BDictionary)ibo).Value;
            }
            else
                if (ibo.GetType() == typeof(BDictionary))
                return GetFromBDicRec((BDictionary)ibo, path, level + 1);

            return null;
        }
        public static void printBEnc(string benc)
        {
            BencodeParser parser = new BencodeParser();
            BDictionary bdic = parser.ParseString<BDictionary>(benc.Trim());
            printDicRec(bdic, 0);
        }
        public static void printDicRec(BDictionary dic, int level = 0)
        {
            foreach (KeyValuePair<BString, IBObject> a in dic) {
                if ( a.Value.GetType() == typeof(BDictionary) )
                {
                    Console.WriteLine(String.Concat(Enumerable.Repeat("\t", level)) + a.Key);
                    level++;
                    printDicRec((BDictionary) a.Value, level);
                    level--;
                } else if ( a.Value.GetType() == typeof(BList) ) {

                } else
                {
                    Console.WriteLine(String.Concat(Enumerable.Repeat("\t", level)) + a.Key + "-> " + a.Value);
                }
            }
        }

        // To Big Endian
        public static int ToBigEndian(byte[] input)
        {
            if ( BitConverter.IsLittleEndian ) Array.Reverse(input);
            return BitConverter.ToInt32(input, 0);
        }
        public static byte[] ToBigEndian(byte input)
        {
            return new byte[1] {input};
        }
        public static byte[] ToBigEndian(Int16 input)
        {
            byte[] output = BitConverter.GetBytes(input);
            if ( !BitConverter.IsLittleEndian ) return output;

            Array.Reverse(output);
            return output;
        }
        public static byte[] ToBigEndian(Int32 input)
        {
            byte[] output = BitConverter.GetBytes(input);
            if ( !BitConverter.IsLittleEndian ) return output;

            Array.Reverse(output);
            return output;
        }
        public static byte[] ToBigEndian(Int64 input)
        {
            byte[] output = BitConverter.GetBytes(input);
            if ( !BitConverter.IsLittleEndian ) return output;

            Array.Reverse(output);
            return output;
        }

        // Arrays
        public static T[] ArraySub<T>(ref T[] data, uint index, uint length, bool reverse = false)
        {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            if ( reverse ) Array.Reverse(result);
            return result;
        }
        public static unsafe bool ArrayComp(byte[] a1, byte[] a2)
        {
          if(a1==a2) return true;
          if(a1==null || a2==null || a1.Length!=a2.Length)
            return false;
          fixed (byte* p1=a1, p2=a2) {
            byte* x1=p1, x2=p2;
            int l = a1.Length;
            for (int i=0; i < l/8; i++, x1+=8, x2+=8)
              if (*((long*)x1) != *((long*)x2)) return false;
            if ((l & 4)!=0) { if (*((int*)x1)!=*((int*)x2)) return false; x1+=4; x2+=4; }
            if ((l & 2)!=0) { if (*((short*)x1)!=*((short*)x2)) return false; x1+=2; x2+=2; }
            if ((l & 1)!=0) if (*((byte*)x1) != *((byte*)x2)) return false;
            return true;
          }
        }
        public static byte[] ArrayMerge(params byte[][] arrays)
        {
            byte[] rv = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (byte[] array in arrays) {
                System.Buffer.BlockCopy(array, 0, rv, offset, array.Length);
                offset += array.Length;
            }
            return rv;
        }
        public static byte[] StringHexToArray(string hex) {
            return Enumerable.Range(0, hex.Length)
                     .Where(x => x % 2 == 0)
                     .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                     .ToArray();
        }
        public static string StringHexToUrlEncode(string hex)
        {
            string hex2 = "";

            for (int i=0; i<hex.Length; i++)
            {
                if ( i % 2 == 0) hex2 += "%";
                hex2 += hex[i];
            }

            return hex2;
        }

        // Misc
        public static string BytesToReadableString(long bytes)
        {
            string bd = "";

            if (        bytes < 1024 )
                bd =    bytes.ToString();
            else if (   bytes > 1024    && bytes < 1024 * 1024 )
                bd =    bytes / 1024        + " KB";
            else if (   bytes > 1024 * 1024 && bytes < 1024 * 1024 * 1024 )
                bd =    bytes / (1024 * 1024)     + " MB";
            else if (   bytes > 1024 * 1024 * 1024 )
                bd =    bytes / (1024 * 1024 * 1024)  + " GB";

            return bd;
        }
        
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        public static extern uint TimeEndPeriod(uint uMilliseconds);

        [DllImport("Kernel32.dll")]
        public static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

        [DllImport("Kernel32.dll")]
        public static extern bool QueryPerformanceFrequency(out long lpFrequency);
    }
}
