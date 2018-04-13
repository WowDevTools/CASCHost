using CASCEdit.Helpers;
using CASCEdit.IO;
using CASCEdit.ZLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CASCEdit
{
    public class CASFile
    {
        public uint CompressedSize;
        public uint DecompressedSize;
        public byte[] Data;
        public MD5Hash DataHash;

        public CASFile(byte[] data, EncodingType encoding, byte compression = 9)
        {
            DecompressedSize = (uint)data.Length;

            using (var md5 = MD5.Create())
                DataHash = new MD5Hash(md5.ComputeHash(data)); // get raw hash

            switch (encoding)
            {
                case EncodingType.None:
                    Data = data;
                    break;
                case EncodingType.ZLib:
                    Data = ZLibCompress(data, compression);
                    break;
                default:
                    throw new NotImplementedException();
            }

            CompressedSize = (uint)Data.Length + 1;
            SetEncoding(encoding);
        }

        private void SetEncoding(EncodingType encoding)
        {
            byte[] data = new byte[Data.Length + 1];
            data[0] = (byte)encoding;
            Buffer.BlockCopy(Data, 0, data, 1, Data.Length);
            Data = data;
        }

        private byte[] ZLibCompress(byte[] data, int level = 9)
        {
            using (var ms = new MemoryStream())
            using (var t = new ZStreamWriter(ms, level))
            {
                t.Write(data, 0, data.Length);
                t.WriteToEnd();
                return ms.ToArray();
            }
        }

    }
}
