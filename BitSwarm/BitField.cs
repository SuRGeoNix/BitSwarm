using System;
using System.Collections.Generic;

namespace SuRGeoNix
{
    public class BitField
    {
        public byte[]   bitfield    { get; private set; }
        public int      size        { get; private set; }
        public int      setsCounter { get; private set; }

        readonly object locker = new object();

        public BitField(int size)
        {
            bitfield        = new byte[((size-1)/8) + 1];
            this.size       = size;
            setsCounter     = 0;
        }
        public BitField(byte[] bitfield, int size)
        {
            if ( size > (bitfield.Length * 8) || size <= ((bitfield.Length-1) * 8) ) throw new Exception("Out of range");

            this.size       = size;
            this.bitfield   = bitfield;
            setsCounter     = 0;
        }

        public void SetBit(int input)
        {
            if ( input >= size || input < 0 ) throw new Exception($"Out of range input: {input} size: {size}");

            int bytePos = input / 8;
            int bitPos  = input % 8;

            lock ( locker )
            {
                if ( !GetBit(input) ) setsCounter++;
                bitfield[bytePos] |= (byte) (1 << (7-bitPos));
            }
        }
        public void UnSetBit(int input)
        {
            if ( input >= size || input < 0 ) throw new Exception($"Out of range input: {input} size: {size}");

            int bytePos = input / 8;
            int bitPos  = input % 8;

            lock ( locker )
            {
                if ( GetBit(input) ) setsCounter--;
                bitfield[bytePos] &= (byte) ~(1 << (7-bitPos));
            }
        }
        public bool GetBit(int input)
        {
            if ( input >= size || input < 0 ) throw new Exception($"Out of range input: {input} size: {size}");

            int bytePos = input / 8;
            int bitPos  = input % 8;

            lock ( locker ) 
                if ( (bitfield[bytePos] & (1 << (7-bitPos))) > 0 ) return true;

            return false;
        }

        // TODO: Review locking
        public int GetFirst0()
        {
            int bytePos = 0;

            lock ( locker )
            {
                for (;bytePos<bitfield.Length; bytePos++)
                    if ( bitfield[bytePos] != 0xff ) break;

                for (int i=bytePos*8; i<(bytePos*8) + 8; i++)
                    if ( i<size && !GetBit(i) ) return i;
            }

            return -1;
        }
        public int GetFirst0(int from, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if ( from >= size || to >= size || from < 0 || to < 0 ) return -2;

            int bytePos = from / 8;
            
            for (int i=(bytePos*8)+(from % 8); i<(bytePos*8) + 8; i++)
                    if ( i<=to && !GetBit(i) ) return i;

            bytePos++;

            for (;bytePos<to/8; bytePos++)
                    if ( bitfield[bytePos] != 0xff ) break;

            for (int i=(bytePos*8); i<(bytePos*8) + 8; i++)
                if ( i<=to && !GetBit(i) ) return i;

            return -1;
        }
        public int GetFirst01(BitField bitfield)
        {
            if ( bitfield == null ) return -2;

            int bytePos = 0;

            for (;bytePos<this.bitfield.Length;bytePos++)
                if ( this.bitfield[bytePos] != 0xff && bitfield.bitfield[bytePos] != 0x00 && ((this.bitfield[bytePos] ^ bitfield.bitfield[bytePos]) & bitfield.bitfield[bytePos] ) != 0 ) break;

            for (int i=(bytePos*8); i<(bytePos*8) + 8; i++)
                if ( i < size && !GetBit(i) && bitfield.GetBit(i) ) return i;
            
            return -1;
        }
        public int GetFirst01(BitField bitfield, int from, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if ( from >= size || to >= size || from < 0 || to < 0 || bitfield == null ) return -2;

            int bytePos = from / 8;

            if ( this.bitfield[bytePos] != 0xff && bitfield.bitfield[bytePos] != 0x00 && ((this.bitfield[bytePos] ^ bitfield.bitfield[bytePos]) & bitfield.bitfield[bytePos] ) != 0 )
                for (int i=(bytePos*8)+(from % 8); i<(bytePos*8) + 8; i++)
                    if ( i<=to && !GetBit(i) && bitfield.GetBit(i) ) return i;

            bytePos++;

            for (;bytePos<to/8;bytePos++)
                if ( this.bitfield[bytePos] != 0xff && bitfield.bitfield[bytePos] != 0x00 && ((this.bitfield[bytePos] ^ bitfield.bitfield[bytePos]) & bitfield.bitfield[bytePos] ) != 0 ) 
                    break;

            for (int i=(bytePos*8); i<(bytePos*8) + 8; i++)
                if ( i<=to && !GetBit(i) && bitfield.GetBit(i) ) return i;
            
            return -1;
        }

        public int GetFirst0Reversed(int from = 0, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if ( from >= size || from < 0 || to >=size || to < 0 || to < from ) return -2;

            int bytePos = to / 8;

            for (int i=(bytePos*8) + to%8; i>=bytePos*8; i--)
                if ( i>=from && !GetBit(i) ) return i;

            bytePos--;

            for (;bytePos>=from/8; bytePos--)
                if ( bitfield[bytePos] != 0xff ) break;

            for (int i=(bytePos*8) + 7; i>=bytePos*8; i--)
                if ( i>=from && !GetBit(i) ) return i;

            return -1;
        }
        public int GetFirst01Reversed(BitField bitfield, int from = 0, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if ( from >= size || from < 0 || to >=size || to < 0 || to < from || bitfield == null ) return -2;

            int bytePos = to / 8;

            if ( this.bitfield[bytePos] != 0xff && bitfield.bitfield[bytePos] != 0x00 && ((this.bitfield[bytePos] ^ bitfield.bitfield[bytePos]) & bitfield.bitfield[bytePos] ) != 0 )
                for (int i=(bytePos*8) + to%8; i>=bytePos*8; i--)
                    if ( i>=from && !GetBit(i) && bitfield.GetBit(i) ) return i;

            bytePos--;

            for (;bytePos>=from/8; bytePos--)
                if ( this.bitfield[bytePos] != 0xff && bitfield.bitfield[bytePos] != 0x00 && ((this.bitfield[bytePos] ^ bitfield.bitfield[bytePos]) & bitfield.bitfield[bytePos] ) != 0 ) 
                    break;

            for (int i=(bytePos*8) + 7; i>=bytePos*8; i--)
                if ( i>=from && !GetBit(i) && bitfield.GetBit(i) ) return i;
            
            return -1;
        }

        public List<int> GetAll0(int from = 0, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if (from >= size || to >= size || from < 0 || to < 0 ) return new List<int>();

            List<int> ret = new List<int>();
            int cur = -1;
            //int from = 0;

            while ( from <= to && (cur = GetFirst0(from, to)) >= 0 )
            {
                ret.Add(cur);
                from = cur + 1;
            }

            return ret;
        }
        public List<int> GetAll0(BitField bitfield, int from = 0, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if (from >= size || to >= size || from < 0 || to < 0) return new List<int>();

            if ( bitfield == null ) return new List<int>();

            List<int> ret = new List<int>();
            int cur = -1;

            while ( from <= to && (cur = GetFirst01(bitfield, from, to)) >= 0 )
            {
                ret.Add(cur);
                from = cur + 1;
            }

            return ret;
        }

        public bool SetBits(int from, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if ( from >= size || from < 0 || to >=size || to < 0 || to < from ) return false;

            int bytePos = from / 8;
            
            for (int i=(bytePos*8)+(from % 8); i<(bytePos*8) + 8; i++)
                if ( i<=to ) SetBit(i);

            bytePos++;
            int endBytePos = (to/8);
            lock ( locker )
            {
                for (;bytePos<=endBytePos; bytePos++)
                {
                    if ( bitfield[bytePos] == 0x00 && bytePos != endBytePos)
                    {
                        bitfield[bytePos] = 0xff;
                        setsCounter += 8;
                    }
                    else
                    {
                        for (int i=(bytePos*8); i<(bytePos*8) + 8; i++)
                            if ( i<=to ) SetBit(i);
                    }
                }
            }
            
            return true;
        }
        public bool UnSetBits(int from, int to = -1)
        {
            to = to == -1 ? size - 1 : to;

            if ( from >= size || from < 0 || to >=size || to < 0 || to < from ) return false;

            int bytePos = from / 8;
            
            for (int i=(bytePos*8)+(from % 8); i<(bytePos*8) + 8; i++)
                if ( i<=to ) UnSetBit(i);

            bytePos++;
            int endBytePos = (to/8);
            lock ( locker )
            {
                for (;bytePos<=endBytePos; bytePos++)
                {
                    if ( bitfield[bytePos] == 0xff && bytePos != endBytePos)
                    {
                        bitfield[bytePos] = 0x00;
                        setsCounter -= 8;
                    }
                    else
                    {
                        for (int i=(bytePos*8); i<(bytePos*8) + 8; i++)
                            if ( i<=to ) UnSetBit(i);
                    }
                }
            }
            
            return true;
        }
        public void UnSetAll()
        {
            lock ( locker )
            {
                for (int i=0; i<bitfield.Length; i++)
                    bitfield[i] = 0x00;

                setsCounter = 0;
            }
        }
        public void SetAll()
        {
            lock ( locker )
            {
                for (int i=0; i<bitfield.Length; i++)
                    bitfield[i] = 0xff;

                setsCounter = size;
            }
        }

        public bool CopyFrom(BitField bitfield)
        {
            lock ( locker )
            {
                if ( bitfield.size != size ) return false;

                Buffer.BlockCopy(bitfield.bitfield, 0, this.bitfield, 0, this.bitfield.Length);
                size = bitfield.size;
                setsCounter = bitfield.setsCounter;
            }

            return true;
        }
        public bool CopyFrom(BitField bitfield, int from, int to =-1)
        {
            to = to == -1 ? size - 1 : to;

            if ( from >= size || from < 0 || to >=size || to < 0 || to < from ) return false;

            for (int i=from; i<=to; i++)
                if ( bitfield.GetBit(i) ) SetBit(i); else UnSetBit(i);

            return true;
        }

        public void PrintBitField()
        {
            Console.WriteLine($"====== BitField ({size}) ======");
            for (int i=0; i<bitfield.Length-1; i++)
            {
                Console.Write(Convert.ToString(bitfield[i], 2).PadLeft(8, '0'));
            }
            Console.Write(Convert.ToString(bitfield[bitfield.Length-1], 2).PadLeft(8, '0').Substring(0,((size-1)%8)+1));
            Console.WriteLine("\n==============================");
        }
        public void SetSize(int size) { this.size = size; }
        public int GetSize() { return size; }
        public void SetBitfield(byte[] bitfield) { this.bitfield = bitfield; }
        public byte[] GetBitfield() { return bitfield; }
    }
}