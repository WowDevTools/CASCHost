using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using CASCEdit.IO;

namespace CASCEdit.Handlers
{
	using CEKeyPageTable = SortedList<MD5Hash, EncodingCEKeyPageTable>;
	using EKeyPageTable = SortedList<MD5Hash, EncodingEKeyPageTable>;

	public class EncodingHandler : IDisposable
	{
		private const int CHUNK_SIZE = 4096;
		private EncodingMap[] EncodingMap;

		public EncodingHeader Header;
		public List<string> ESpecStringTable = new List<string>();
		public CEKeyPageTable CEKeys = new CEKeyPageTable(new HashComparer());
		public EKeyPageTable EKeys = new EKeyPageTable(new HashComparer());

		public EncodingHandler()
		{
			Header = new EncodingHeader();
			EncodingMap = new[]
			{
				new EncodingMap(EncodingType.None, 6),
				new EncodingMap(EncodingType.ZLib, 9),
				new EncodingMap(EncodingType.None, 6),
				new EncodingMap(EncodingType.None, 6),
				new EncodingMap(EncodingType.None, 6),
				new EncodingMap(EncodingType.None, 6),
				new EncodingMap(EncodingType.ZLib, 9),
			};
		}

		public EncodingHandler(BLTEStream blte)
		{
			if (blte.Length != long.Parse(CASContainer.BuildConfig["encoding-size"][0]))
				CASContainer.Settings?.Logger.LogAndThrow(Logging.LogType.Critical, "Encoding File is corrupt.");

			BinaryReader stream = new BinaryReader(blte);

			Header = new EncodingHeader()
			{
				Magic = stream.ReadBytes(2),
				Version = stream.ReadByte(),
				ChecksumSizeC = stream.ReadByte(),
				ChecksumSizeE = stream.ReadByte(),
				PageSizeCEKey = stream.ReadUInt16(),
				PageSizeEKey = stream.ReadUInt16(),
				PageCountCEKey = stream.ReadUInt32BE(),
				PageCountEKey = stream.ReadUInt32BE(),
				Unknown_x11 = stream.ReadByte(),
				ESpecBlockSize = stream.ReadUInt32BE()
			};

			// ESpec string table
			ESpecStringTable.AddRange(Encoding.ASCII.GetString(stream.ReadBytes((int)Header.ESpecBlockSize)).Split('\0'));

			// skip CE page table lookup
			stream.ReadBytes((int)Header.PageCountCEKey * 32);

			// read CE page table data
			for (int i = 0; i < Header.PageCountCEKey; i++)
			{
				long start = stream.BaseStream.Position;

				ushort keysCount;
				while ((keysCount = stream.ReadUInt16()) != 0)
				{
					var entry = new EncodingCEKeyPageTable()
					{
						DecompressedSize = stream.ReadUInt32BE(),
						CKey = new MD5Hash(stream)
					};

					for (int ki = 0; ki < keysCount; ki++)
						entry.EKeys.Add(new MD5Hash(stream));

					CEKeys.Add(entry.CKey, entry);
				}

				if (stream.BaseStream.Position % CHUNK_SIZE != 0)
					stream.BaseStream.Position += CHUNK_SIZE - ((stream.BaseStream.Position - start) % CHUNK_SIZE);
			}

			// skip EKey page table lookup
			stream.ReadBytes((int)Header.PageCountEKey * 32);

			// read EKey page table data
			for (int i = 0; i < Header.PageCountEKey; i++)
			{
				long start = stream.BaseStream.Position;

				MD5Hash hash;
				while (!(hash = new MD5Hash(stream)).IsEmpty)
				{
					var entry = new EncodingEKeyPageTable()
					{
						EKey = hash,
						ESpecStringIndex = stream.ReadUInt32BE(),
						FileSize = stream.ReadUInt40BE()
					};

					EKeys.Add(entry.EKey, entry);
				}

				if (stream.BaseStream.Position % CHUNK_SIZE != 0)
					stream.BaseStream.Position += CHUNK_SIZE - ((stream.BaseStream.Position - start) % CHUNK_SIZE);
			}

			// Encoding file ESpecStringTable
			stream.ReadBytes((int)(stream.BaseStream.Length - stream.BaseStream.Position));

			EncodingMap = blte.EncodingMap.ToArray();

			blte?.Dispose();
			stream?.Dispose();
		}


		public void AddEntry(CASResult blte)
		{
			if (blte == null)
				return;

			// create the entry
			var entry = new EncodingCEKeyPageTable()
			{
				DecompressedSize = blte.DecompressedSize,
				CKey = blte.CEKey,
			};
			entry.EKeys.Add(blte.EKey);

			if (CEKeys.ContainsKey(blte.CEKey)) // check if it exists
			{
				var existing = CEKeys[blte.CEKey];
				if (EKeys.ContainsKey(existing.EKeys[0])) // remove old layout
					EKeys.Remove(existing.EKeys[0]);

				existing.EKeys[0] = blte.EKey; // update existing entry
			}
			else
			{
				CEKeys.Add(entry.CKey, entry); // new entry
			}

			AddLayoutEntry(blte);
		}

		private void AddLayoutEntry(CASResult blte)
		{
			if (EKeys.ContainsKey(blte.EKey))
				EKeys.Remove(blte.EKey);

			// generate ESpecString
			string ESpecString;
			uint size = blte.CompressedSize - 30;

			// the below suffices and is technically correct
			// however this could be more compliant https://wowdev.wiki/CASC#Encoding_Specification_.28ESpec.29
			if (blte.CEKey == CASContainer.BuildConfig.GetKey("root")) // root is always z
				ESpecString = "z";
			else if (size >= 1024 * 256) // 256K* seems to be the max
				ESpecString = "b:{256K*=z}";
			else if (size > 1024)
				ESpecString = "b:{" + (int)Math.Floor(size / 1024d) + "K*=z}"; // closest floored KB
			else
				ESpecString = "b:{" + size + "*=z}"; // actual B size

			// string index
			int stridx = ESpecStringTable.IndexOf(ESpecString);
			if (stridx == -1)
			{
				stridx = ESpecStringTable.Count - 2; // ignore the 0 byte
				ESpecStringTable.Insert(stridx, ESpecString);
			}

			// create the entry
			var entry = new EncodingEKeyPageTable()
			{
				FileSize = size,
				EKey = blte.EKey,
				ESpecStringIndex = (uint)stridx
			};
			EKeys.Add(entry.EKey, entry);
		}


		public CASResult Write()
		{
			byte[][] entries = new byte[EncodingMap.Length][];
			CASFile[] files = new CASFile[EncodingMap.Length];

			// ESpecStringTable 1
			entries[1] = Encoding.UTF8.GetBytes(string.Join("\0", ESpecStringTable));
			files[1] = new CASFile(entries[1], EncodingMap[1].Type, EncodingMap[1].CompressionLevel);

			// CEKeysPageTable Data 3
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms))
			{
				long pos = 0;
				foreach (var entry in CEKeys.Values)
				{
					if (pos + entry.EntrySize > CHUNK_SIZE)
					{
						bw.Write(new byte[CHUNK_SIZE - pos]); // pad to chunk size
						pos = 0;
					}

					bw.Write((ushort)entry.EKeys.Count);
					bw.WriteUInt32BE(entry.DecompressedSize);
					bw.Write(entry.CKey.Value);
					for (int i = 0; i < entry.EKeys.Count; i++)
						bw.Write(entry.EKeys[i].Value);

					pos += entry.EntrySize;
				}

				bw.Write(new byte[CHUNK_SIZE - pos]); // final padding

				entries[3] = ms.ToArray();
				files[3] = new CASFile(entries[3], EncodingMap[3].Type, EncodingMap[3].CompressionLevel);
			}

			// EKeysPageTable Data 5
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms))
			{
				long pos = 0;
				foreach (var entry in EKeys.Values)
				{
					if (pos + entry.EntrySize > CHUNK_SIZE)
					{
						bw.Write(new byte[CHUNK_SIZE - pos]); // pad to chunk size
						pos = 0;
					}

					bw.Write(entry.EKey.Value);
					bw.WriteUInt32BE(entry.ESpecStringIndex);
					bw.WriteUInt40BE(entry.FileSize);

					pos += entry.EntrySize;
				}

				// EOF flag
				bw.Write(new byte[16]); // empty hash
				bw.Write(0xFFFFFFFF); // flag
				pos += 16 + 4;

				bw.Write(new byte[CHUNK_SIZE - pos]); // final padding

				entries[5] = ms.ToArray();
				files[5] = new CASFile(entries[5], EncodingMap[5].Type, EncodingMap[5].CompressionLevel);
			}

			// CEKeysPageTable lookup 2
			using (var md5 = MD5.Create())
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms))
			{
				int chunks = entries[3].Length / CHUNK_SIZE;

				for (int i = 0; i < chunks; i++)
				{
					byte[] chunk = new byte[CHUNK_SIZE];
					Buffer.BlockCopy(entries[3], (i * CHUNK_SIZE), chunk, 0, CHUNK_SIZE);

					bw.Write(chunk, 6, 16); // first entry hash
					bw.Write(md5.ComputeHash(chunk)); // md5 of chunk
				}

				entries[2] = ms.ToArray();
				files[2] = new CASFile(entries[2], EncodingMap[2].Type, EncodingMap[2].CompressionLevel);
			}

			// EKeysPageTable lookup 4
			using (var md5 = MD5.Create())
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms))
			{
				int chunks = entries[5].Length / CHUNK_SIZE;

				for (int i = 0; i < chunks; i++)
				{
					byte[] chunk = new byte[CHUNK_SIZE];
					Buffer.BlockCopy(entries[5], (i * CHUNK_SIZE), chunk, 0, CHUNK_SIZE);

					bw.Write(chunk, 0, 16); // first entry hash
					bw.Write(md5.ComputeHash(chunk)); // md5 of chunk
				}

				entries[4] = ms.ToArray();
				files[4] = new CASFile(entries[4], EncodingMap[4].Type, EncodingMap[4].CompressionLevel);
			}

			// Encoding Header 0
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms))
			{
				bw.Write(Header.Magic);
				bw.Write(Header.Version);
				bw.Write(Header.ChecksumSizeC);
				bw.Write(Header.ChecksumSizeE);
				bw.Write(Header.PageSizeCEKey);
				bw.Write(Header.PageSizeEKey);
				bw.WriteUInt32BE((uint)entries[2].Length / 32);
				bw.WriteUInt32BE((uint)entries[4].Length / 32);
				bw.Write(Header.Unknown_x11);
				bw.WriteUInt32BE((uint)Encoding.UTF8.GetByteCount(string.Join("\0", ESpecStringTable)));

				entries[0] = ms.ToArray();
				files[0] = new CASFile(entries[0], EncodingMap[0].Type, EncodingMap[0].CompressionLevel);
			}

			// Encoding's own ESpecStringTable 6
			entries[6] = GetStringTable(entries.Select(x => x.Length));
			files[6] = new CASFile(entries[6], EncodingMap[6].Type, EncodingMap[6].CompressionLevel);

			//Write
			CASResult res = DataHandler.Write(WriteMode.CDN, files);
			using (var md5 = MD5.Create())
				res.CEKey = new MD5Hash(md5.ComputeHash(entries.SelectMany(x => x).ToArray()));

			CASContainer.Logger.LogInformation($"Encoding: EKey: {res.EKey} CEKey: {res.CEKey}");

			CASContainer.BuildConfig.Set("encoding-size", res.DecompressedSize.ToString());
			CASContainer.BuildConfig.Set("encoding-size", (res.CompressedSize - 30).ToString(), 1); // BLTE size minus header
			CASContainer.BuildConfig.Set("encoding", res.CEKey.ToString());
			CASContainer.BuildConfig.Set("encoding", res.EKey.ToString(), 1);

			Array.Resize(ref entries, 0);
			Array.Resize(ref files, 0);
			entries = null;
			files = null;

			// cache Encoding Hash
			CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry() { CEKey = res.CEKey, EKey = res.EKey, Path = "__ENCODING__" });

			return res;
		}

		private byte[] GetStringTable(IEnumerable<int> lengths)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("b:{");

			for (int i = 0; i < EncodingMap.Length; i++)
			{
				char encoding = (char)EncodingMap[i].Type;

				if (i == EncodingMap.Length - 1)
					sb.Append($"*={encoding}");
				else
					sb.Append($"{lengths.ElementAt(i)}={encoding},");
			}

			sb.Append("}");

			return Encoding.UTF8.GetBytes(sb.ToString().ToLowerInvariant());
		}

		public void Dispose()
		{
			Header = null;
			ESpecStringTable.Clear();
			ESpecStringTable.TrimExcess();
			ESpecStringTable = null;
			CEKeys.Clear();
			CEKeys = null;
			EKeys.Clear();
			EKeys = null;
		}
	}
}
