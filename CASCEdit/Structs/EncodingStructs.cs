using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CASCEdit.Helpers;

namespace CASCEdit.Structs
{
    public class EncodingHeader
    {
        public byte[] Magic = new byte[] { 69, 78 }; // EN
        public byte Version;
        public byte ChecksumSizeA;
        public byte ChecksumSizeB;
        public ushort FlagsA;
        public ushort FlagsB;
        public uint NumEntriesA;
        public uint NumEntriesB;
        public ulong StringBlockSize;
    }

    public class EncodingEntry
    {
        public MD5Hash Hash;
        public List<MD5Hash> Keys = new List<MD5Hash>();
        public uint DecompressedSize;

        public uint EntrySize => 2 + 4 + 16 + ((uint)Keys.Count * 16);
    }

    public class EncodingLayout
    {
        public MD5Hash Hash;
        public uint StringIndex;
        public ulong Size;

        public uint EntrySize => 16 + 4 + 1 + 4;
    }

}
