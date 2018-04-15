using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit.Helpers
{
    class Jenkins96 : HashAlgorithm
    {
		// Original Implementation by TOM_RUS in CASCExplorer
		// Original Source : https://github.com/WoW-Tools/CASCExplorer/blob/master/CascLib/Jenkins96.cs

		private ulong hashValue;

        public byte[] ResultValue => BitConverter.GetBytes(hashValue);
        public ulong Result => hashValue;
        public uint pB;
        public uint pC;

        public ulong ComputeHash(string str, bool normalized = true)
        {
            if (normalized)
                str = str.Replace('/', '\\').ToUpperInvariant();

            byte[] data = Encoding.ASCII.GetBytes(str);
            ComputeHash(data);
            return hashValue;
        }

        public ulong ComputeHash(byte[] data, out uint pC, bool reset = false)
        {
			if (reset)
				pC = pB = 0;

			ComputeHash(data);
            pC = this.pC;
            return hashValue;
        }

        public override void Initialize() { }

        protected override unsafe void HashCore(byte[] array, int ibStart, int cbSize)
        {
			uint rot(uint x, int k) => (x << k) | (x >> (32 - k));

			uint length = (uint)array.Length;
            uint a, b, c;
            a = b = c = 0xdeadbeef + (length) + pC;
            c += pB;

            if (length == 0)
            {
                hashValue = ((ulong)c << 32) | b;
                return;
            }

            var newLen = (length + (12 - length % 12) % 12);
            if (length != newLen)
            {
                Array.Resize(ref array, (int)newLen);
                length = newLen;
            }

            fixed (byte* bb = array)
            {
                uint* u = (uint*)bb;

                for (var j = 0; j < length - 12; j += 12)
                {
                    a += *(u + j / 4);
                    b += *(u + j / 4 + 1);
                    c += *(u + j / 4 + 2);

                    a -= c; a ^= rot(c, 4); c += b;
                    b -= a; b ^= rot(a, 6); a += c;
                    c -= b; c ^= rot(b, 8); b += a;
                    a -= c; a ^= rot(c, 16); c += b;
                    b -= a; b ^= rot(a, 19); a += c;
                    c -= b; c ^= rot(b, 4); b += a;
                }

                var i = length - 12;
                a += *(u + i / 4);
                b += *(u + i / 4 + 1);
                c += *(u + i / 4 + 2);

                c ^= b; c -= rot(b, 14);
                a ^= c; a -= rot(c, 11);
                b ^= a; b -= rot(a, 25);
                c ^= b; c -= rot(b, 16);
                a ^= c; a -= rot(c, 4);
                b ^= a; b -= rot(a, 14);
                c ^= b; c -= rot(b, 24);

                pB = b;
                pC = c;

                hashValue = ((ulong)c << 32) | b;
            }
        }

        protected override byte[] HashFinal()
        {
            return ResultValue;
        }
    }
}
