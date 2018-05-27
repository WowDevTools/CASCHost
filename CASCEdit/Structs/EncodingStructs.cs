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
        public byte ChecksumSizeC;
        public byte ChecksumSizeE;
        public ushort PageSizeCEKey;
        public ushort PageSizeEKey;
        public uint PageCountCEKey;
        public uint PageCountEKey;
		public byte Unknown_x11 = 0;
        public ulong ESpecBlockSize;
    }

    public class EncodingCEKeyPageTable
	{
        public MD5Hash CKey;
        public List<MD5Hash> EKeys = new List<MD5Hash>();
        public uint DecompressedSize;

        public uint EntrySize => 2 + 4 + 16 + ((uint)EKeys.Count * 16);
    }

    public class EncodingEKeyPageTable
	{
        public MD5Hash EKey;
        public uint ESpecStringIndex;
        public ulong FileSize;

        public uint EntrySize => 16 + 4 + 1 + 4;
    }

}
