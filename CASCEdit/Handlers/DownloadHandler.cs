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
		private DownloadHeader Header;
		private List<DownloadEntry> Entries = new List<DownloadEntry>();
		private List<DownloadTag> Tags = new List<DownloadTag>();

		private int[] lastIndex;
		private EncodingMap[] EncodingMap;

		public DownloadHandler(BLTEStream blte)
		{
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

				//Entries
				for (int i = 0; i < Header.NumEntries; i++)
				{
					var entry = new DownloadEntry()
					{
						Unknown = Header.Version > 1 ? br.ReadByte() : (byte)0, // new V2 field
						Hash = new MD5Hash(br),
						FileSize = br.ReadUInt40BE(),
						Stage = br.ReadByte(),
						UnknownData = br.ReadBytes(4)
					};

					Entries.Add(entry);
				}

				//Tags
				int numMaskBytes = ~~((int)Header.NumEntries + 7) / 8;
				for (int i = 0; i < Header.NumTags; i++)
				{
					var tag = new DownloadTag()
					{
						Name = br.ReadCString(),
						Type = br.ReadUInt16BE(),
						BitMask = new BoolArray(br.ReadBytes(numMaskBytes))
					};

					Tags.Add(tag);
				}

				EncodingMap = blte.EncodingMap.ToArray();

				lastIndex = new int[]
				{
					Entries.FindLastIndex(x => x.Stage == 0),
					Entries.FindLastIndex(x => x.Stage == 1)
				};
			}

			blte?.Dispose();
		}

		public void AddEntry(CASCResult blte)
		{
			if (CASCContainer.EncodingHandler.Layout.ContainsKey(blte.Hash)) // skip existing
				return;

			var entry = new DownloadEntry()
			{
				Hash = blte.Hash,
				FileSize = blte.CompressedSize - 30,
				UnknownData = new byte[4],
				Stage = (byte)(blte.HighPriority ? 0 : 1)
			};

			int index = lastIndex[entry.Stage];
			if (index >= 0)
			{
				if (entry.Stage == 0) lastIndex[0]++;
				lastIndex[1]++;

				Entries.Insert(index, entry);

				foreach (var tag in Tags)
					if (tag.Name != "Alternate")
						tag.BitMask.Insert(index, true);
			}
			else
			{
				Entries.Add(entry);

				foreach (var tag in Tags)
					if (tag.Name != "Alternate")
						tag.BitMask.Add(true);
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

		public CASCResult Write()
		{
			byte[][] entries = new byte[EncodingMap.Length][];
			CASCFile[] files = new CASCFile[EncodingMap.Length];

			//Header
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				bw.Write(Header.Header);
				bw.Write(Header.Version);
				bw.Write(Header.ChecksumSize);
				bw.Write(Header.Unknown);
				bw.WriteUInt32BE(Header.NumEntries);
				bw.WriteUInt16BE(Header.NumTags);

				entries[0] = ms.ToArray();
				files[0] = new CASCFile(entries[0], EncodingMap[0].Type, EncodingMap[0].CompressionLevel);
			}

			//Files
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				foreach (var entry in Entries)
				{
					bw.Write(entry.Hash.Value);
					bw.WriteUInt40BE(entry.FileSize);
					bw.Write(entry.Stage);
					bw.Write(entry.UnknownData);
				}

				entries[1] = ms.ToArray();
				files[1] = new CASCFile(entries[1], EncodingMap[1].Type, EncodingMap[1].CompressionLevel);
			}

			//Tags
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
				files[2] = new CASCFile(entries[2], EncodingMap[2].Type, EncodingMap[2].CompressionLevel);
			}

			//Write
			CASCResult res = DataHandler.Write(WriteMode.CDN, files);
			using (var md5 = MD5.Create())
				res.DataHash = new MD5Hash(md5.ComputeHash(entries.SelectMany(x => x).ToArray()));

			File.Delete(Path.Combine(CASCContainer.Settings.OutputPath, CASCContainer.BuildConfig["download"][0]));

			CASCContainer.Logger.LogInformation($"Download: Hash: {res.Hash} Data: {res.DataHash}");
			CASCContainer.BuildConfig.Set("download-size", res.DecompressedSize.ToString());
			CASCContainer.BuildConfig.Set("download-size", (res.CompressedSize - 30).ToString(), 1);
			CASCContainer.BuildConfig.Set("download", res.DataHash.ToString());
			CASCContainer.BuildConfig.Set("download", res.Hash.ToString(), 1);

			entries = new byte[0][];
			files = new CASCFile[0];
			entries = null;
			files = null;
			return res;
		}
	}
}
