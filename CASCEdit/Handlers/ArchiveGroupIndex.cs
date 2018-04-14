using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using System.ComponentModel;

namespace CASCEdit.Handlers
{
    /// <summary>
    /// DO NOT USE - Not thoroughly tested
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    class ArchiveGroupIndex
    {
        public string BaseFile; //MD5(last 20 bytes + Checksumsize) to hex(X2) lowercase
        public List<IndexBlock> Blocks = new List<IndexBlock>();
        public IndexFooter Footer;

        const int CHUNK_SIZE = 0x1000;

        public ArchiveGroupIndex(string path)
        {
            BaseFile = path;
            Read();
        }

        public void Read()
        {
            using (FileStream stream = new FileStream(BaseFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(stream))
            {
                br.BaseStream.Position = 0;

                //((Length - footer) / (chunk + toc + block_hash)) + 1
                int blockcount = (int)((br.BaseStream.Length - 36) / (CHUNK_SIZE + 16 + 8)) + 1;

                for (int i = 0; i < blockcount; i++)
                {
                    long start = br.BaseStream.Position;
                    var block = new IndexBlock();

                    while ((br.BaseStream.Position - start) + (16 + 4 + 2 + 4) <= CHUNK_SIZE)
                    {
                        IndexEntry entry = new IndexEntry()
                        {
                            Hash = new MD5Hash(br),
                            Size = br.ReadUInt32BE(),
                            ArchiveIndex = br.ReadUInt16BE(),
                            Offset = br.ReadUInt32BE()
                        };

                        if (!entry.Hash.IsEmpty)
                            block.Entries.Add(entry);
                    }

                    Blocks.Add(block);

                    if (br.BaseStream.Position % CHUNK_SIZE != 0)
                        br.BaseStream.Position += CHUNK_SIZE - ((br.BaseStream.Position - start) % CHUNK_SIZE);
                }

                //TOC last entry hashes
                br.BaseStream.Position += Blocks.Count * 16;

                //Block hashes - lower_md5 all blocks except last
                br.BaseStream.Position += (Blocks.Count - 1) * 8;

                Footer = new IndexFooter()
                {
                    IndexBlockHash = br.ReadBytes(8),
                    TOCHash = br.ReadBytes(8),
                    Version = br.ReadByte(),
                    _11 = br.ReadByte(),
                    _12 = br.ReadByte(),
                    _13 = br.ReadByte(),
                    Offset = br.ReadByte(),
                    Size = br.ReadByte(),
                    KeySize = br.ReadByte(),
                    ChecksumSize = br.ReadByte(),
                    EntryCount = br.ReadUInt32(),
                };
                Footer.FooterMD5 = br.ReadBytes(Footer.ChecksumSize);
            }
        }


        public string Write()
        {
            using (var md5 = MD5.Create())
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                IndexBlock lastBlock = Blocks[Blocks.Count - 1];
                List<byte[]> blockhashes = new List<byte[]>();

                //Blocks
                foreach (var block in Blocks)
                {
                    //Entries
                    long posBlockStart = bw.BaseStream.Position;

                    block.Entries.Sort(new HashComparer());
                    foreach (var entry in block.Entries)
                    {
                        bw.Write(entry.Hash.Value);
                        bw.WriteUInt32BE(entry.Size);
                        bw.WriteUInt16BE(entry.ArchiveIndex);
                        bw.WriteUInt32BE(entry.Offset);
                    }

                    //Pad to CHUNK_SIZE
                    if (bw.BaseStream.Position % CHUNK_SIZE != 0)
                        bw.Write(new byte[CHUNK_SIZE - ((bw.BaseStream.Position - posBlockStart) % CHUNK_SIZE)]);

                    //Calc block's md5
                    if (block != lastBlock)
                        blockhashes.Add(GetBlockHash(bw, md5, posBlockStart));
                }

                //TOC
                bw.Write(Blocks.SelectMany(x => x.Entries.Last().Hash.Value).ToArray());

                //Block hashes
                if (Blocks.Count > 1)
                {
                    bw.Write(blockhashes.SelectMany(x => x).ToArray());
                    blockhashes.Clear();
                }

                //Footer Start
                long posFooterStart = bw.BaseStream.Position;

                //Calculate IndexBlockHash
                //  BOTTOM 8 bytes = BLOCK DATA
                bw.BaseStream.Position = CHUNK_SIZE * (Blocks.Count - 1);
                byte[] indexBlockHashBytes = new byte[CHUNK_SIZE];
                bw.BaseStream.Read(indexBlockHashBytes, 0, indexBlockHashBytes.Length);
                bw.BaseStream.Position = posFooterStart;
                var lowerHash = md5.ComputeHash(indexBlockHashBytes).Take(8).ToArray();
                bw.Write(lowerHash);

                //  TOP 8 bytes = TOC + lowerHash
                bw.BaseStream.Position = CHUNK_SIZE * Blocks.Count;
                byte[] tocHashBytes = new byte[8 + posFooterStart - bw.BaseStream.Position];
                bw.BaseStream.Read(tocHashBytes, 0, tocHashBytes.Length);
                var upperHash = md5.ComputeHash(tocHashBytes).Take(8).ToArray();
                bw.Write(upperHash);

                //Footer
                bw.Write(Footer.Version);
                bw.Write(Footer._11);
                bw.Write(Footer._12);
                bw.Write(Footer._13);
                bw.Write(Footer.Offset);
                bw.Write(Footer.Size);
                bw.Write(Footer.KeySize);
                bw.Write(Footer.ChecksumSize);
                bw.Write((uint)Blocks.Sum(x => x.Entries.Count));
                bw.Write(new byte[Footer.ChecksumSize]); //Footer MD5 hash placeholder

                //Update Footer Md5 Hash
                bw.BaseStream.Position = posFooterStart + 16;
                byte[] footerBytes = new byte[bw.BaseStream.Length - bw.BaseStream.Position]; //From _10 to EOF
                bw.BaseStream.Read(footerBytes, 0, footerBytes.Length);
                bw.BaseStream.Position = bw.BaseStream.Length - Footer.ChecksumSize; //Update the MD5 hash
                var footerHash = md5.ComputeHash(footerBytes).Take(8).ToArray();
                bw.Write(footerHash);

                //Save file to output
                string filename = ComputeFilename(bw, md5, posFooterStart);
                var path = Path.Combine(CASContainer.Settings.OutputPath, filename + ".index");

                if (CASContainer.Settings.StaticMode)
                {
                    path = Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(filename + ".index", "data"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                }

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    ms.Position = 0;
                    ms.CopyTo(fs);
                    fs.Flush();
                }

                //Update CDN Config
                CASContainer.CDNConfig.Set("archive-group", filename);
                return filename;
            }
        }

        private byte[] GetBlockHash(BinaryWriter bw, MD5 md5, long posStart)
        {
            bw.BaseStream.Position = posStart;
            byte[] checksum = new byte[CHUNK_SIZE];
            bw.BaseStream.Read(checksum, 0, checksum.Length);
            return md5.ComputeHash(checksum).Take(8).ToArray();
        }

        private string ComputeFilename(BinaryWriter bw, MD5 md5, long posFooterStart)
        {
            bw.BaseStream.Position = posFooterStart + 8;
            byte[] filenameBytes = new byte[bw.BaseStream.Length - bw.BaseStream.Position]; //Last 8 bytes of IndexBlockHash to EOF
            bw.BaseStream.Read(filenameBytes, 0, filenameBytes.Length);
            return md5.ComputeHash(filenameBytes).ToMD5String();
        }

    }
}
