using System;
using System.Collections;
using System.Collections.Generic;
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
	public class DownloadHandler
	{
		public List<DownloadEntry> Entries = new List<DownloadEntry>();
		public List<DownloadTag> Tags = new List<DownloadTag>();

		private DownloadHeader Header;
		private int[] endofStageIndex;
		private EncodingMap[] EncodingMap;

		public DownloadHandler()
		{
			Header = new DownloadHeader();
			EncodingMap = new[]
			{
				new EncodingMap(EncodingType.None, 6),
				new EncodingMap(EncodingType.None, 6),
				new EncodingMap(EncodingType.ZLib, 9)
			};
		}

		public DownloadHandler(BLTEStream blte)
		{

			if (CASContainer.BuildConfig["download-size"][0] != null && blte.Length != long.Parse(CASContainer.BuildConfig["download-size"][0]))
				CASContainer.Settings?.Logger.LogAndThrow(Logging.LogType.Critical, "Download File is corrupt.");

			using (var br = new BinaryReader(blte))
			{
				Header = new DownloadHeader()
				{
					Header = br.ReadBytes(2),
					Version = br.ReadByte(),
					ChecksumSize = br.ReadByte(),
					Unknown = br.ReadByte(),
					NumEntries = br.ReadUInt32BE(),
					NumTags = br.ReadUInt16BE(),
				};

				if (Header.Version >= 3) {
					throw new NotImplementedException("Download file versions newer than 2 are not supported.");
				}

				if (Header.Version >= 2) {
					Header.NumFlags = br.ReadByte();
				}

				// entries
				for (int i = 0; i < Header.NumEntries; i++)
				{
					var entry = new DownloadEntry()
					{
						Hash = new MD5Hash(br),
						FileSize = br.ReadUInt40BE(),
						Stage = br.ReadByte()
					};

					if (Header.Unknown != 0) {
						entry.Unknown = br.ReadUInt32BE();
					}

					if (Header.Version >= 2) {
						entry.Flags = br.ReadBytes(Header.NumFlags);
					}

					Entries.Add(entry);
				}

				// tags
				int numMaskBytes = ((int)Header.NumEntries + 7) / 8;
				for (int i = 0; i < Header.NumTags; i++)
				{
					var tag = new DownloadTag()
					{
						Name = br.ReadCString(),
						Type = br.ReadUInt16BE(),
						BitMask = new BoolArray(br.ReadBytes(numMaskBytes))
					};

					// We need to remove trailing bits from the padded byte array.
					while (tag.BitMask.Count != Entries.Count) {
						tag.BitMask.RemoveAt(tag.BitMask.Count - 1);
					}

					Tags.Add(tag);
				}

				EncodingMap = blte.EncodingMap.ToArray();

				endofStageIndex = new int[] // store last indice of each stage
				{
					Entries.FindLastIndex(x => x.Stage == 0),
					Entries.FindLastIndex(x => x.Stage == 1)
				};
			}

			blte?.Dispose();
		}

		public void AddEntry(CASResult blte)
		{
			if (CASContainer.EncodingHandler.EKeys.ContainsKey(blte.Hash)) // skip existing
				return;

			var entry = new DownloadEntry()
			{
				Hash = blte.Hash,
				FileSize = blte.CompressedSize - 30,
				Flags = new byte[Header.NumFlags],
				Stage = (byte)(blte.HighPriority ? 0 : 1)
			};

			int index = endofStageIndex[entry.Stage];
			if (index >= 0)
			{
				endofStageIndex[entry.Stage]++;

				Entries.Insert(index, entry);

				foreach (var tag in Tags) {
					tag.BitMask.Insert(index, tag.Name != "Alternate");
				}
			}
			else
			{
				Entries.Add(entry);

				foreach (var tag in Tags) {
					tag.BitMask.Add(tag.Name != "Alternate");
				}
			}
		}

		public void RemoveEntry(MD5Hash hash)
		{
			int index = Entries.FindIndex(x => x.Hash == hash);
			if (index > -1)
			{
				Entries.RemoveAt(index);
				Tags.ForEach(x => x.BitMask.RemoveAt(index));
			}
		}

		public CASResult Write()
		{
			byte[][] entries = new byte[EncodingMap.Length][];
			CASFile[] files = new CASFile[EncodingMap.Length];

			// header
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				bw.Write(Header.Header);
				bw.Write(Header.Version);
				bw.Write(Header.ChecksumSize);
				bw.Write(Header.Unknown);
				bw.WriteUInt32BE((uint)Entries.Count);
				bw.WriteUInt16BE((ushort)Tags.Count);

				if (Header.Version >= 2) {
					bw.Write(Header.NumFlags);
				}

				entries[0] = ms.ToArray();
				files[0] = new CASFile(entries[0], EncodingMap[0].Type, EncodingMap[0].CompressionLevel);
			}

			// files
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				foreach (var entry in Entries)
				{
					bw.Write(entry.Hash.Value);
					bw.WriteUInt40BE(entry.FileSize);
					bw.Write(entry.Stage);

					if (Header.Unknown != 0) {
						bw.WriteUInt32BE(entry.Unknown);
					}

					if (Header.Version >= 2) {
						bw.Write(entry.Flags);
					}
				}

				entries[1] = ms.ToArray();
				files[1] = new CASFile(entries[1], EncodingMap[1].Type, EncodingMap[1].CompressionLevel);
			}

			// tags
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				foreach (var tag in Tags)
				{
					bw.Write(Encoding.UTF8.GetBytes(tag.Name));
					bw.Write((byte)0);
					bw.WriteUInt16BE(tag.Type);
					bw.Write(tag.BitMask.ToByteArray());
					tag.BitMask.Clear();
				}

				entries[2] = ms.ToArray();
				files[2] = new CASFile(entries[2], EncodingMap[2].Type, EncodingMap[2].CompressionLevel);
			}

			// write
			CASResult res = DataHandler.Write(WriteMode.CDN, files);
			using (var md5 = MD5.Create())
				res.DataHash = new MD5Hash(md5.ComputeHash(entries.SelectMany(x => x).ToArray()));

			File.Delete(Path.Combine(CASContainer.Settings.OutputPath, CASContainer.BuildConfig["download"][0]));

			CASContainer.Logger.LogInformation($"Download: Hash: {res.Hash} Data: {res.DataHash}");
			CASContainer.BuildConfig.Set("download-size", res.DecompressedSize.ToString());
			CASContainer.BuildConfig.Set("download-size", (res.CompressedSize - 30).ToString(), 1);
			CASContainer.BuildConfig.Set("download", res.DataHash.ToString());
			CASContainer.BuildConfig.Set("download", res.Hash.ToString(), 1);

			Array.Resize(ref entries, 0);
			Array.Resize(ref files, 0);
			entries = null;
			files = null;
			return res;
		}
	}
}
