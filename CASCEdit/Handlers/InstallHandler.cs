using CASCEdit.IO;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace CASCEdit.Handlers
{
	public class InstallHandler
	{
		public List<InstallEntry> InstallData = new List<InstallEntry>();
		public List<InstallTag> Tags = new List<InstallTag>();

		private InstallHeader Header;
		private EncodingMap[] EncodingMap;

		public InstallHandler()
		{
			Header = new InstallHeader();
			EncodingMap = new[]
			{
				new EncodingMap(EncodingType.ZLib, 9),
				new EncodingMap(EncodingType.None, 6),
			};
		}

		public InstallHandler(BLTEStream blte)
		{
			if (blte.Length != long.Parse(CASCContainer.BuildConfig["install-size"][0]))
				CASCContainer.Settings?.Logger.LogAndThrow(Logging.LogType.Critical, "Install File is corrupt.");

			BinaryReader stream = new BinaryReader(blte);

			Header = new InstallHeader()
			{
				Magic = stream.ReadBytes(2),
				Version = stream.ReadByte(),
				HashSize = stream.ReadByte(),
				NumTags = stream.ReadUInt16BE(),
				NumEntries = stream.ReadUInt32BE()
			};

			// tags            
			int numMaskBytes = (int)(Header.NumEntries + 7) / 8;
			for (int i = 0; i < Header.NumTags; i++)
			{
				InstallTag tag = new InstallTag()
				{
					Name = stream.ReadCString(),
					Type = stream.ReadUInt16BE(),
					BitMask = new BoolArray(stream.ReadBytes(numMaskBytes))
				};

				Tags.Add(tag);
			}

			// entries
			for (int i = 0; i < Header.NumEntries; i++)
			{
				InstallEntry entry = new InstallEntry()
				{
					Name = stream.ReadCString(),
					MD5 = new MD5Hash(stream),
					Size = stream.ReadUInt32BE()
				};

				InstallData.Add(entry);
			}

			EncodingMap = blte.EncodingMap.ToArray();

			stream?.Dispose();
			blte?.Dispose();
		}

		public void Write(List<CASCResult> newentries)
		{
			if (!NeedsWrite(newentries))
				return;

			byte[][] entries = new byte[EncodingMap.Length][];
			CASCFile[] files = new CASCFile[EncodingMap.Length];

			// header
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				bw.Write(Header.Magic);
				bw.Write(Header.Version);
				bw.Write(Header.HashSize);
				bw.WriteUInt16BE((ushort)Tags.Count);
				bw.WriteUInt32BE((uint)InstallData.Count);

				foreach (var tag in Tags)
				{
					bw.Write(Encoding.UTF8.GetBytes(tag.Name));
					bw.Write((byte)0);
					bw.WriteUInt16BE(tag.Type);
					bw.Write(tag.BitMask.ToByteArray());
				}

				entries[0] = ms.ToArray();
				files[0] = new CASCFile(entries[0], EncodingMap[0].Type, EncodingMap[0].CompressionLevel);
			}

			// entries
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{

				foreach (var entry in InstallData)
				{
					bw.Write(Encoding.UTF8.GetBytes(entry.Name));
					bw.Write((byte)0);
					bw.Write(entry.MD5.Value);
					bw.WriteUInt32BE(entry.Size);
				}

				entries[1] = ms.ToArray();
				files[1] = new CASCFile(entries[1], EncodingMap[1].Type, EncodingMap[1].CompressionLevel);
			}

			// write
			CASCResult res = DataHandler.Write(WriteMode.CDN, files);
			using (var md5 = MD5.Create())
				res.DataHash = new MD5Hash(md5.ComputeHash(entries.SelectMany(x => x).ToArray()));

			Console.WriteLine($"Install: Hash: {res.Hash} Data: {res.DataHash}");

			CASCContainer.BuildConfig.Set("install-size", res.DecompressedSize.ToString());
			CASCContainer.BuildConfig.Set("install-size", (res.CompressedSize - 30).ToString(), 1); // BLTE size minus header
			CASCContainer.BuildConfig.Set("install", res.DataHash.ToString());
			CASCContainer.BuildConfig.Set("install", res.Hash.ToString(), 1);

			Array.Resize(ref entries, 0);
			Array.Resize(ref files, 0);
		}

		private bool NeedsWrite(List<CASCResult> entries)
		{
			// files that mean we need to edit the install file
			string[] files = new[] { "wow.exe", "wow-64.exe", @"world of warcraft.app\contents\macos\world of warcraft" };

			bool needswrite = false;
			foreach (var file in files)
			{
				var entry = entries.Where(x => !string.IsNullOrWhiteSpace(x?.Path)).FirstOrDefault(x => x.Path.ToLower().EndsWith(file));
				var existing = InstallData.FirstOrDefault(x => x.Name.ToLower() == file);

				if (entry != null && existing != null)
				{
					if (entry.DataHash != existing.MD5 || entry.DecompressedSize != existing.Size)
					{
						existing.MD5 = entry.DataHash;
						existing.Size = entry.DecompressedSize;
						needswrite = true;
					}
				}
			}

			return needswrite;
		}

		public InstallEntry GetEntry(string name)
		{
			return InstallData.FirstOrDefault(i => i.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
		}
	}
}
