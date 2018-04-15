using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit.Helpers
{
    public class MD5Hash
    {
        public byte[] Value;

        public MD5Hash(BinaryReader br)
        {
            Value = br.ReadBytes(16);
        }

        public MD5Hash(byte[] hash)
        {
            Value = hash;
        }

        public bool IsEmpty => Value.All(x => x == 0);

        public override string ToString() => Value.ToMD5String();

		#region Operators

		public static bool operator ==(MD5Hash hash1, MD5Hash hash2)
        {
            return hash1.Equals(hash2);
        }

        public static bool operator !=(MD5Hash hash1, MD5Hash hash2)
        {
            return !hash1.Equals(hash2);
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != typeof(MD5Hash))
                return false;

            return new HashComparer().Compare(this, (MD5Hash)obj) == 0;
        }

        public override int GetHashCode() => Value.GetHashCode();
        
        #endregion

    }
}
