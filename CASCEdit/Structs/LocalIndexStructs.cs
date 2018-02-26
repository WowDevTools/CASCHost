using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Helpers;

namespace CASCEdit.Structs
{
    public class LocalIndexHeader
    {
        public string BaseFile;
        public bool Changed = false;

        public uint HeaderHashSize; // usually 0x10
        public uint HeaderHash;
        public ushort _2 = 7;
        public byte BucketIndex; // always first byte of hex filename
        public byte _4 = 0;
        public byte EntrySizeBytes = 4;
        public byte EntryOffsetBytes = 5;
        public byte EntryKeyBytes = 9;
        public byte ArchiveFileHeaderBytes = 30;
        public ulong ArchiveTotalSizeMaximum = 0x4000000000;
        public byte[] Padding = new byte[8];
        public uint EntriesSize; // total entry length in bytes
        public uint EntriesHash; // jenkins hash of all entries

        public List<LocalIndexEntry> Entries = new List<LocalIndexEntry>();
    }

    public class LocalIndexEntry
    {
        public byte[] Key; // first 9 bytes of hash
        public uint Size; // byte length of file
        public ulong Archive;
        public ulong Offset;


        public ulong ArchiveOffset // UInt40 (5 bytes)
        {
            get => Offset | (Archive << 30);
            set
            {
                Archive = value >> 30;  // top 10 bits = data.***
                Offset = value & 0x3FFFFFFF; // bottom 30 bits = offset
            }
        }
    }

    public class LocalIndexSpace
    {
        public ulong Archive;
        public ulong Offset;
        public ulong Size;
    }
}
