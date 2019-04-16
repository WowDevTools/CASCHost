using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Structs;
using CASCEdit.Helpers;

namespace CASCEdit.Structs
{
    public class IndexBlock
    {
        public List<IndexEntry> Entries = new List<IndexEntry>();
        public uint Padding;
    }

    public class IndexEntry
    {
        public MD5Hash EKey;
        public ushort ArchiveIndex;
        public uint Offset;
        public uint Size;
        
        public int EntrySize => 16 + 4 + 4;
    }

    public class IndexFooter
    {
        public byte[] TOCHash; // ChecksumSize
        public byte Version = 1;
        public byte _11 = 0;
        public byte _12 = 0;
        public byte BlockSizeKb = 4;
        public byte Offset = 4;
        public byte Size = 4;
        public byte KeySize = 16;
        public byte ChecksumSize = 8; // always <= 0x10
        public uint EntryCount = 0;
        public byte[] FooterMD5; // MD5[ChecksumSize]
    }
}
