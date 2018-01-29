using CASCEdit.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit.Structs
{
    public class CacheEntry
	{
		public MD5Hash MD5;
		public MD5Hash BLTE;
		public ulong Hash;
		public uint FileDataId;
		public string Path;

		public CacheEntry() { }

		public CacheEntry(RootEntry rootEntry, MD5Hash blte)
		{
			MD5 = rootEntry.MD5;
			BLTE = blte;
			Hash = rootEntry.Hash;
			FileDataId = rootEntry.FileDataId;
			Path = rootEntry.Path;
		}
	}
}
