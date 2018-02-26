using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Helpers;

namespace CASCEdit.Structs
{
    public class DownloadHeader
    {
        public byte[] Header = new byte[] { 68, 76 }; // DL
        public byte Version = 1;
        public byte ChecksumSize = 16;
        public byte Unknown = 1;
        public uint NumEntries;
        public ushort NumTags;
    }

    public class DownloadEntry
    {
		public byte Unknown; // v2 only
        public MD5Hash Hash;
        public ulong FileSize;
        public byte Stage;
        public byte[] UnknownData;
    }

    public class DownloadTag
    {
        public string Name;
        public ushort Type;
        public BoolArray BitMask;
    }
}
