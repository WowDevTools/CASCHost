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
        public byte HasChecksum = 1;
        public uint NumEntries;
        public ushort NumTags;
        public byte NumFlags = 0; // V2 only
		public byte BasePriority = 0; // V3 only
		public byte[] Unknown_0D = new byte[3]; // V3 only
	}

    public class DownloadEntry
    {
        public MD5Hash EKey;
        public ulong FileSize;
        public byte Priority;
		public uint Checksum;
		public DownloadFlags[] Flags; // V2 only
	}

    public class DownloadTag
    {
        public string Name;
        public ushort Type;
        public BoolArray BitMask;
    }

	public enum DownloadFlags : byte
	{
		None = 0,
		Plugin = 1,
		PluginData = 2,
	}
}
