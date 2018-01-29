using CASCEdit.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit.Helpers
{
    public interface ICache
    {
        string Version { get; }
		HashSet<string> ToPurge { get; }
		IReadOnlyCollection<CacheEntry> Entries { get; }
		uint MaxId { get; }

        bool HasFiles { get; }

        void AddOrUpdate(CacheEntry item);
        bool HasId(uint fileid);
        void Remove(string file);

        void Save();
        void Load();

        void Clean();
    }

}
