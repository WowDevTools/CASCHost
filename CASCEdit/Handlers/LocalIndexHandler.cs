using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Handlers;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using CASCEdit.IO;

namespace CASCEdit.Handlers
{
    public class LocalIndexHandler
    {
        public List<LocalIndexHeader> LocalIndices = new List<LocalIndexHeader>();

        private List<string> Files = new List<string>();
        private const int CHUNK_SIZE = 0xC0000;

        public LocalIndexHandler()
        {
            CASCContainer.Logger.LogInformation("Loading Local Indices...");
            GetFiles();
            Read();
        }

        public void Read()
        {
            foreach (var file in Files)
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs, Encoding.ASCII))
                {
                    // header
                    LocalIndexHeader index = new LocalIndexHeader()
                    {
                        BaseFile = file,

                        HeaderHashSize = br.ReadUInt32(),
                        HeaderHash = br.ReadUInt32(),
                        _2 = br.ReadUInt16(),
                        BucketIndex = br.ReadByte(),
                        _4 = br.ReadByte(),
                        EntrySizeBytes = br.ReadByte(),
                        EntryOffsetBytes = br.ReadByte(),
                        EntryKeyBytes = br.ReadByte(),
                        ArchiveFileHeaderBytes = br.ReadByte(),
                        ArchiveTotalSizeMaximum = br.ReadUInt64(),
                        Padding = br.ReadBytes(8),
                        EntriesSize = br.ReadUInt32(),
                        EntriesHash = br.ReadUInt32()
                    };

                    // entries
                    br.BaseStream.Position = 0x28;
                    for (int i = 0; i < index.EntriesSize / 18; i++)
                    {
                        var s = br.BaseStream.Position;

                        LocalIndexEntry entry = new LocalIndexEntry()
                        {
                            Key = br.ReadBytes(9),
                            ArchiveOffset = br.ReadUInt40BE(),
                            Size = br.ReadUInt32(),
                        };

                        index.Entries.Add(entry);
                    }

                    LocalIndices.Add(index);
                }
            }
        }

        public void AddEntry(CASCResult blte)
        {
            var entry = new LocalIndexEntry()
            {
                Archive = blte.Archive,
                Key = blte.Hash.Value.Take(9).ToArray(),
                Offset = blte.Offset,
                Size = blte.CompressedSize
            };

            var idx = LocalIndices.First(x => x.BucketIndex == GetBucket(entry.Key));
            var existing = idx.Entries.FirstOrDefault(x => x.Key.SequenceEqual(entry.Key)); // check for existing

            if (existing != null)
                existing = entry;
            else
                idx.Entries.Add(entry);


            idx.Changed = true;
        }

        public void Write()
        {
            foreach (var index in LocalIndices)
            {
                if (!index.Changed)
                    continue;

                uint pC = 0;
                Jenkins96 hasher = new Jenkins96();

                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(index.HeaderHashSize);
                    bw.Write((uint)0); // HeaderHash
                    bw.Write(index._2);
                    bw.Write(index.BucketIndex);
                    bw.Write(index._4);
                    bw.Write(index.EntrySizeBytes);
                    bw.Write(index.EntryOffsetBytes);
                    bw.Write(index.EntryKeyBytes);
                    bw.Write(index.ArchiveFileHeaderBytes);
                    bw.Write(index.ArchiveTotalSizeMaximum);
                    bw.Write(new byte[8]);
                    bw.Write((uint)index.Entries.Count * 18);
                    bw.Write((uint)0); // EntriesHash

                    // entries
                    index.Entries.Sort(new HashComparer());
                    foreach (var entry in index.Entries)
                    {
                        bw.Write(entry.Key);
                        bw.WriteUInt40BE(entry.ArchiveOffset);
                        bw.Write(entry.Size);
                    }

                    // update EntriesHash
                    bw.BaseStream.Position = 0x28;
                    for (int i = 0; i < index.Entries.Count; i++)
                    {
                        byte[] entryhash = new byte[18];
                        bw.BaseStream.Read(entryhash, 0, entryhash.Length);
                        hasher.ComputeHash(entryhash, out pC);
                    }
                    bw.BaseStream.Position = 0x24;
                    bw.Write(pC);

                    // update HeaderHash
                    bw.BaseStream.Position = 8;
                    byte[] headerhash = new byte[index.HeaderHashSize];
                    bw.BaseStream.Read(headerhash, 0, headerhash.Length);
                    hasher.Reset();
                    hasher.ComputeHash(headerhash, out pC);
                    bw.BaseStream.Position = 4;
                    bw.Write(pC);

                    // file length constraint
                    if (bw.BaseStream.Length < CHUNK_SIZE)
                        bw.BaseStream.SetLength(CHUNK_SIZE);

                    // save file to output
                    var bucket = index.BucketIndex.ToString("X2");
                    var version = long.Parse(Path.GetFileNameWithoutExtension(index.BaseFile).Substring(2), NumberStyles.HexNumber);
                    string filename = bucket + version.ToString("X8") + ".idx";

                    var path = Path.Combine(CASCContainer.Settings.OutputPath, filename.ToLowerInvariant());

                    using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                    {
                        fs.Seek(0, SeekOrigin.End);
                        ms.Position = 0;
                        ms.CopyTo(fs);
                        fs.Flush();
                    }

                    index.Changed = false;
                }
            }
        }


        public LocalIndexEntry GetIndexInfo(MD5Hash md5)
        {
            byte[] hash = md5.Value.Take(9).ToArray();
            var idx = LocalIndices.FirstOrDefault(x => x.BucketIndex == GetBucket(hash));
            return idx?.Entries.FirstOrDefault(x => x.Key.SequenceEqual(hash));
        }

        public List<LocalIndexSpace> GetEmptySpace(ulong minsize = 0x100)
        {
            List<LocalIndexSpace> space = new List<LocalIndexSpace>();

            var datagroups = LocalIndices.SelectMany(x => x.Entries).GroupBy(x => x.Archive).Select(x => x.OrderBy(y => y.Offset).ToList());
            foreach (var group in datagroups)
            {
                for (int i = 0; i < group.Count - 1; i++)
                {
                    var current = group[i];
                    ulong nextOffset = group[i + 1].Offset;

                    if (current.Offset + current.Size + minsize < nextOffset)
                    {
                        space.Add(new LocalIndexSpace()
                        {
                            Archive = current.Archive,
                            Offset = current.Offset,
                            Size = nextOffset - (current.Offset - current.Size)
                        });
                    }
                }
            }

            return space;
        }

        private void GetFiles()
        {
            string dataPath = Path.Combine(CASCContainer.BasePath, "Data", "data");
            for (int i = 0; i < 0x10; i++)
            {
                var files = Directory.EnumerateFiles(dataPath, string.Format("{0:X2}*.idx", i));
                if (files.Any())
                    Files.Add(files.Last());
            }
        }

        private byte GetBucket(byte[] key)
        {
            byte a = key.Aggregate((x, y) => (byte)(x ^ y));
            return (byte)((a & 0xf) ^ (a >> 4));
        }
    }
}
