using CASCEdit.ZLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CASCEdit.Structs;
using CASCEdit.Helpers;

namespace CASCEdit.IO
{
    public class BLTEStream : Stream
    {
        public const int BLTE_MAGIC = 0x45544C42;

        public IReadOnlyCollection<EncodingMap> EncodingMap => Entries.Select(x => new EncodingMap(x.Encoding, x.CompressionLevel)).ToArray();

        private List<BLTEEntry> Entries = new List<BLTEEntry>();
        private BinaryReader reader;
        private MemoryStream memStream;

        private Stream stream;
        private int blocksIndex;
        private long length;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => memStream.Position;
            set
            {
                while (value > memStream.Length)
                    if (!ProcessNextBlock())
                        break;

                memStream.Position = value;
            }
        }


        public BLTEStream(Stream src)
        {
            stream = src;
            reader = new BinaryReader(src);

            Parse();
        }


        private void Parse()
        {
            uint size = (uint)reader.BaseStream.Length;
            if (size < 8)
            {
                CASCContainer.Logger.LogCritical($"Not enough data");
                return;
            }

            int magic = reader.ReadInt32();

            if (magic != BLTE_MAGIC)
            {
                CASCContainer.Logger.LogCritical("Incorrect magic");
                return;
            }

            uint headerSize = reader.ReadUInt32BE();
            uint chunkCount = 1;

            if (headerSize > 0)
            {
                if (size < 12)
                {
                    CASCContainer.Logger.LogCritical($"Not enough data: {12}");
                    return;
                }

                byte flags = reader.ReadByte();
                chunkCount = reader.ReadUInt24BE();

                if (flags != 0x0F || chunkCount == 0)
                {
                    CASCContainer.Logger.LogCritical($"Bad table format 0x{flags.ToString("X2")}, numBlocks {chunkCount}");
                    return;
                }


                uint frameHeaderSize = 24 * chunkCount + 12;
                if (headerSize != frameHeaderSize)
                {
                    CASCContainer.Logger.LogCritical("Header size mismatch");
                    return;
                }

                if (size < frameHeaderSize)
                {
                    CASCContainer.Logger.LogCritical($"Not enough data: {frameHeaderSize}");
                    return;
                }
            }

            for (int i = 0; i < chunkCount; i++)
            {
                BLTEEntry block = new BLTEEntry();

                if (headerSize != 0)
                {
                    block.CompressedSize = reader.ReadUInt32BE();
                    block.DecompressedSize = reader.ReadUInt32BE();
                    block.Checksum = new MD5Hash(reader);
                }
                else
                {
                    block.CompressedSize = size - 8;
                    block.DecompressedSize = size - 8 - 1;
                    block.Checksum = default(MD5Hash);
                }

                Entries.Add(block);
            }

            memStream = new MemoryStream((int)Entries.Sum(b => b.DecompressedSize));

            ProcessNextBlock();

            length = headerSize == 0 ? memStream.Length : memStream.Capacity;
        }

        private bool ProcessNextBlock()
        {
            if (blocksIndex == Entries.Count)
                return false;

            long startPos = memStream.Position;
            memStream.Position = memStream.Length;

            var block = Entries[blocksIndex];
            byte[] data = reader.ReadBytes((int)block.CompressedSize);
            block.Encoding = (EncodingType)data[0];

            switch (block.Encoding)
            {
                case EncodingType.ZLib:
                    Decompress(block, data, memStream);
                    break;
                case EncodingType.None:
                    memStream.Write(data, 1, data.Length - 1);
                    break;
                default:
                    CASCContainer.Logger.LogCritical($"BLTE block type {(char)block.Encoding} (0x{block.Encoding.ToString("X2")})");
                    return false;
            }

            blocksIndex++;
            memStream.Position = startPos;

            return true;
        }

        private void Decompress(BLTEEntry block, byte[] data, MemoryStream outStream)
        {
            //ZLib compression level
            block.CompressionLevel = (byte)(data[2] >> 6); //FLEVEL bits
            if (block.CompressionLevel > 1)
                block.CompressionLevel *= 3;

            //Ignore encoding byte
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var stream = new ZStreamReader(ms))
            {
                stream.CopyTo(outStream);
            }
        }


        public override int Read(byte[] buffer, int offset, int count)
        {
            if (memStream.Position + count > memStream.Length && blocksIndex < Entries.Count)
                return ProcessNextBlock() ? Read(buffer, offset, count) : 0;
            else
                return memStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
            }

            return Position;
        }


        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }



        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stream?.Dispose();
                reader?.Dispose();
                memStream?.Dispose();
            }

            base.Dispose(disposing);
        }

        internal class BLTEEntry
        {
            public uint CompressedSize;
            public uint DecompressedSize;

            public MD5Hash Checksum;
            public EncodingType Encoding;
            public byte CompressionLevel = 6;
        }
    }

    public enum EncodingType
    {
        ZLib = 0x5A,
        None = 0x4E
    }

    public class EncodingMap
    {
        public EncodingType Type;
        public byte CompressionLevel;

        public EncodingMap(EncodingType type, byte compression)
        {
            this.Type = type;
            this.CompressionLevel = compression;
        }
    }
}
