using CASCEdit.ZLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit
{
    public static class Extensions
    {
        #region Read Extensions
        public static string ReadCString(this BinaryReader reader)
        {
            List<byte> temp = new List<byte>();

            byte b;
            while ((b = reader.ReadByte()) != 0)
                temp.Add(b);

            return Encoding.UTF8.GetString(temp.ToArray());
        }

        public static ulong ReadUInt40BE(this BinaryReader reader)
        {
            byte[] array = new byte[8];
            for (int i = 0; i < 5; i++)
                array[4 - i] = reader.ReadByte();

            return BitConverter.ToUInt64(array, 0);
        }

        public static uint ReadUInt32BE(this BinaryReader reader)
        {
            byte[] val = reader.ReadBytes(4);
            Array.Reverse(val);
            return BitConverter.ToUInt32(val, 0);
        }

        public static uint ReadUInt24BE(this BinaryReader reader)
        {
            byte[] array = new byte[8];
            for (int i = 0; i < 3; i++)
                array[2 - i] = reader.ReadByte();

            return BitConverter.ToUInt32(array, 0);
        }

        public static ushort ReadUInt16BE(this BinaryReader reader)
        {
            byte[] val = reader.ReadBytes(2);
            Array.Reverse(val);
            return BitConverter.ToUInt16(val, 0);
        }

        #endregion

        #region Write Extensions

        public static void WriteUInt40BE(this BinaryWriter writer, ulong v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            for (int i = 3; i < bytes.Length; i++)
                writer.Write(bytes[bytes.Length - i - 1]);
        }

        public static void WriteUInt32BE(this BinaryWriter writer, uint value)
        {
            byte[] val = BitConverter.GetBytes(value);
            Array.Reverse(val);
            writer.Write(val);
        }

        public static void WriteUInt24BE(this BinaryWriter writer, uint v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            for (int i = 1; i < bytes.Length; i++)
                writer.Write(bytes[bytes.Length - i - 1]);
        }

        public static void WriteUInt16BE(this BinaryWriter writer, ushort value)
        {
            byte[] val = BitConverter.GetBytes(value);
            Array.Reverse(val);
            writer.Write(val);
        }

        #endregion

        public static byte[] ToByteArray(this string hex, int count = 32)
        {
            Func<char, int> CharToHex = (h) => h - (h < 0x3A ? 0x30 : 0x57);

            count = Math.Min(hex.Length / 2, count);

            var arr = new byte[count];
            for (var i = 0; i < count; i++)
                arr[i] = (byte)((CharToHex(hex[i << 1]) << 4) + CharToHex(hex[(i << 1) + 1]));

            return arr;
        }

        public static string ToMD5String(this byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
