using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Helpers;
using CASCEdit.Structs;

namespace CASCEdit.Handlers
{
	public class ArchiveIndexHandler
	{
		public List<IndexEntry> Entries = new List<IndexEntry>();
		public IndexFooter Footer = new IndexFooter();

		public readonly string BaseFile;
		const int CHUNK_SIZE = 0x1000;

		public ArchiveIndexHandler(string path = "")
		{
			BaseFile = path;

			if (!string.IsNullOrWhiteSpace(path))
				Read();
		}


		private void Read()
		{
			using (FileStream stream = new FileStream(BaseFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (var br = new BinaryReader(stream))
			{
				//((Length - footer) / (chunk + toc + block_hash)) + 1
				int blockcount = (int)((br.BaseStream.Length - 36) / (CHUNK_SIZE + 16 + 8)) + 1;

				for (int i = 0; i < blockcount; i++)
				{
					long start = br.BaseStream.Position;

					while ((br.BaseStream.Position - start) + (16 + 4 + 4) <= CHUNK_SIZE)
					{
						IndexEntry entry = new IndexEntry()
						{
							EKey = new MD5Hash(br),
							Size = br.ReadUInt32BE(),
							Offset = br.ReadUInt32BE()
						};

						if (!entry.EKey.IsEmpty)
							Entries.Add(entry);
					}

					if (br.BaseStream.Position % CHUNK_SIZE != 0)
						br.BaseStream.Position += CHUNK_SIZE - ((br.BaseStream.Position - start) % CHUNK_SIZE);
				}

				//TOC last entry hashes
				br.BaseStream.Position += blockcount * 16;

				//Block hashes - lower_md5 all blocks except last
				br.BaseStream.Position += (blockcount - 1) * 8;

				//Footer
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
					FooterMD5 = br.ReadBytes(8)
				};
			}
		}

		public string Write()
		{
			using (var md5 = MD5.Create())
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				List<byte[]> blockHashes = new List<byte[]>();
				List<byte[]> entryHashes = new List<byte[]>();
				int blockCount = 0;

				Entries.Sort(new HashComparer());

				long pos = 0;
				uint offset = 0;

				for (int i = 0; i < Entries.Count; i++)
				{
					var entry = Entries[i];

					if (pos + entry.EntrySize > CHUNK_SIZE)
					{
						entryHashes.Add(Entries[i - 1].EKey.Value); //Last entry hash

						bw.Write(new byte[CHUNK_SIZE - pos]);
						blockHashes.Add(GetBlockHash(bw, md5));
						pos = 0;
					}

					entry.Offset = offset;
					bw.Write(entry.EKey.Value);
					bw.WriteUInt32BE(entry.Size);
					bw.WriteUInt32BE(entry.Offset);

					offset += entry.Size;
					pos += entry.EntrySize;
				}

				//Update final block
				bw.Write(new byte[CHUNK_SIZE - pos]); //Final padding
				blockCount = blockHashes.Count + 1;
				entryHashes.Add(Entries.Last().EKey.Value); //Final entry hash

				//TOC
				bw.Write(entryHashes.SelectMany(x => x).ToArray());
				entryHashes.Clear();

				//Block hashes
				if (blockHashes.Count > 0)
				{
					bw.Write(blockHashes.SelectMany(x => x).ToArray());
					blockHashes.Clear();
				}

				//Footer Start
				long posFooterStart = bw.BaseStream.Position;

				//Calculate IndexBlockHash
				bw.BaseStream.Position = CHUNK_SIZE * (blockCount - 1);
				byte[] indexBlockHashBytes = new byte[CHUNK_SIZE];
				bw.BaseStream.Read(indexBlockHashBytes, 0, indexBlockHashBytes.Length);
				bw.BaseStream.Position = posFooterStart;
				var lowerHash = md5.ComputeHash(indexBlockHashBytes).Take(8).ToArray();
				bw.Write(lowerHash);

				//Calculate TOCHash
				bw.BaseStream.Position = CHUNK_SIZE * blockCount;
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
				bw.Write((uint)Entries.Count);
				bw.Write(new byte[Footer.ChecksumSize]); //Footer MD5 hash placeholder

				//Update Footer Md5 Hash
				bw.BaseStream.Position = posFooterStart + 16;
				byte[] footerBytes = new byte[bw.BaseStream.Length - bw.BaseStream.Position]; //From _10 to EOF
				bw.BaseStream.Read(footerBytes, 0, footerBytes.Length);
				bw.BaseStream.Position = bw.BaseStream.Length - Footer.ChecksumSize; //Update the MD5 hash
				bw.Write(md5.ComputeHash(footerBytes).Take(8).ToArray());

				//Save file to output
				string filename = ComputeFilename(bw, md5, posFooterStart);

				var path = Helper.FixOutputPath(Path.Combine(CASContainer.Settings.OutputPath, filename + ".index"));
				using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
				{
					ms.Position = 0;
					ms.CopyTo(fs);
					fs.Flush();
				}

				//Update CDN Config
				CASContainer.CDNConfig["archives"].RemoveAll(x => x == Path.GetFileNameWithoutExtension(BaseFile));
				CASContainer.CDNConfig["archives"].Add(filename);
				CASContainer.CDNConfig["archives"].Sort(new HashComparer());

				return path;
			}
		}


		private byte[] GetBlockHash(BinaryWriter bw, MD5 md5)
		{
			bw.BaseStream.Position -= CHUNK_SIZE;
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
