using CASCEdit.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace CASCEdit
{
    public class CASResult
    {
        public MD5Hash EKey;
        public MD5Hash CEKey;
        public uint CompressedSize;
        public uint DecompressedSize;

        public ulong Archive;
        public ulong Offset;

        public string Path;
        public string OutPath;
		public bool HighPriority;
    }
}
