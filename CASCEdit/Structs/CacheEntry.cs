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
		public MD5Hash CEKey;
		public MD5Hash EKey;
		public ulong NameHash;
		public uint FileDataId;
		public string Path;

		public CacheEntry() { }

		public CacheEntry(RootEntry rootEntry, MD5Hash ekey)
		{
			CEKey = rootEntry.CEKey;
			EKey = ekey;
			NameHash = rootEntry.NameHash;
			FileDataId = rootEntry.FileDataId;
			Path = rootEntry.Path;
		}
	}
}
