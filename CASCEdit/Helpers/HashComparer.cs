using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Structs;
using CASCEdit.IO;

namespace CASCEdit.Helpers
{
    class HashComparer : IComparer<byte[]>, IComparer<CASResult>, IComparer<IndexEntry>, IComparer<LocalIndexEntry>, IComparer<MD5Hash>, IComparer<string>
    {
        public int Compare(MD5Hash x, MD5Hash y) => Compare(x.Value, y.Value);
        public int Compare(LocalIndexEntry x, LocalIndexEntry y) => Compare(x.Key, y.Key);
        public int Compare(IndexEntry x, IndexEntry y) => Compare(x.EKey.Value, y.EKey.Value);
        public int Compare(CASResult x, CASResult y) => Compare(x.EKey.Value, y.EKey.Value);
        public int Compare(string x, string y) => Compare(x.ToByteArray(), y.ToByteArray());

        public int Compare(byte[] x, byte[] y)
        {
            if (x == y)
                return 0;

            int length = Math.Min(x.Length, y.Length);
            for (int i = 0; i < length; i++)
            {
                int c = x[i].CompareTo(y[i]);
                if (c != 0)
                    return c;
            }

            return 0;
        }
    }
}
