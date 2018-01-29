using CASCEdit.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace CASCEdit
{
    public class CASCResult
    {
        public MD5Hash Hash;
        public MD5Hash DataHash;
        public uint CompressedSize;
        public uint DecompressedSize;

        public ulong Archive;
        public ulong Offset;

        public string Path;
        public string OutPath;
		public bool HighPriority;
    }
}
