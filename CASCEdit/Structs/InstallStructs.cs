using CASCEdit.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CASCEdit.Structs
{
    public class InstallHeader
    {
        public byte[] Magic = new byte[] { 73, 78 }; // IN
        public byte Version = 1;
        public byte HashSize = 16;
        public ushort NumTags;
        public uint NumEntries;
    }

    public class InstallEntry
    {
        public string Name;
        public MD5Hash MD5;
        public uint Size;
    }

    public class InstallTag
    {
        public string Name;
        public ushort Type;
        public byte[] BitMask;
    }
}
