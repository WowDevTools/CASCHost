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
using System.Net;
using System.Collections.Concurrent;
using CASCEdit.Patch;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;

namespace CASCEdit.IO
{
	[Flags]
	public enum WriteMode
	{
		Data = 1,
		CDN = 2,
		AllFiles = Data | CDN
	}

	public class DataHandler
	{

		#region Read BLTE
		public static BLTEStream Read(string file, LocalIndexEntry index)
		{
			using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (BinaryReader br = new BinaryReader(fs, Encoding.ASCII))
			{
				br.BaseStream.Position = (long)index.Offset;

				byte[] hash = br.ReadBytes(16);
				uint size = br.ReadUInt32();
				byte[] unknown = br.ReadBytes(0xA);
				byte[] data = br.ReadBytes((int)index.Size - 30);

				return new BLTEStream(new MemoryStream(data));
			}
		}

		public static BLTEStream ReadDirect(string file)
		{
			return new BLTEStream(new MemoryStream(File.ReadAllBytes(file)));
		}

		public static void Extract(string file, string savepath, LocalIndexEntry index)
		{
			using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (FileStream fout = new FileStream(savepath, FileMode.Create, FileAccess.Write))
			using (BinaryReader br = new BinaryReader(fs, Encoding.ASCII))
			{
				br.BaseStream.Position = (long)index.Offset;

				var hash = br.ReadBytes(16);
				uint size = br.ReadUInt32();
				byte[] unknown = br.ReadBytes(0xA);
				byte[] data = br.ReadBytes((int)index.Size - 30);

				fout.Write(data, 0, data.Length);
				fout.Flush();
			}
		}
		#endregion

		#region Download
		public static bool Download(string url, string savepath)
		{
			CASContainer.Logger.LogInformation($"Downloading {Path.GetFileName(url)}...");

			if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
			{
				return DoDownload(url, savepath);
			}
			else
			{
				foreach (var host in CASContainer.Settings.DownloadLocations)
					if (DoDownload(host + url, savepath))
						return true;
			}

			return false;
		}

		public static MemoryStream Stream(string url)
		{
			try
			{
				HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
				using (WebResponse resp = req.GetResponse())
				{
					var ms = new MemoryStream();
					resp.GetResponseStream().CopyTo(ms);
					return ms;
				}
			}
			catch
			{
				return null;
			}
		}

		private static bool DoDownload(string url, string savepath)
		{
			try
			{
				HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
				using (WebResponse resp = req.GetResponse())
				using (FileStream fs = new FileStream(savepath, FileMode.Create, FileAccess.Write, FileShare.Read))
				{
					resp.GetResponseStream().CopyTo(fs);
					return true;
				}
			}
			catch
			{
				return false;
			}
		}
		#endregion

		#region Write BLTE/CASC
		public static CASResult Write(WriteMode mode, params CASFile[] entries)
		{
			CASContainer.Logger.LogInformation("Writing data...");

			var path = Path.Combine(CASContainer.Settings.OutputPath, "Data", "data");
			string filename = "dummy.000";

			// calculate local data file
			if (mode.HasFlag(WriteMode.Data))
			{
				Directory.CreateDirectory(path);

				long requiredBytes = entries.Sum(x => x.Data.Length) + 38 + (entries.Count() > 1 ? 5 : 0) + (entries.Count() * 24);
				if (mode.HasFlag(WriteMode.Data))
					filename = GetDataFile(requiredBytes);
			}

			using (var md5 = MD5.Create())
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms, Encoding.ASCII))
			{
				bw.Seek(0, SeekOrigin.End);
				long posStart = bw.BaseStream.Position;

				uint headersize = entries.Length == 1 ? 0 : 24 * (uint)entries.Length + 12;

				// Archive Header
				bw.Write(new byte[16]); // MD5 hash
				bw.Write((uint)0); // Size
				bw.Write(new byte[0xA]); // Unknown

				// Header
				bw.Write(BLTEStream.BLTE_MAGIC);
				bw.WriteUInt32BE(headersize);

				// Chunkinfo
				if (headersize > 0)
				{
					bw.Write((byte)15); // Flags
					bw.WriteUInt24BE((uint)entries.Count());

					// Entries
					foreach (var entry in entries)
					{
						bw.WriteUInt32BE(entry.CompressedSize);
						bw.WriteUInt32BE(entry.DecompressedSize);
						bw.Write(md5.ComputeHash(entry.Data)); // Checksum
					}
				}

				// Write data
				foreach (var entry in entries)
				{
					bw.Write(entry.Data);
					Array.Resize(ref entry.Data, 0);
				}

				// Compute header hash
				bw.BaseStream.Position = posStart + 30;
				byte[] buffer = new byte[(headersize == 0 ? bw.BaseStream.Length - bw.BaseStream.Position : headersize)];
				bw.BaseStream.Read(buffer, 0, buffer.Length);
				var hash = md5.ComputeHash(buffer);

				CASResult blte = new CASResult()
				{
					DecompressedSize = (uint)entries.Sum(x => x.DecompressedSize),
					CompressedSize = (uint)(bw.BaseStream.Length - posStart),
					Hash = new MD5Hash(hash)
				};

				bw.BaseStream.Position = posStart;
				bw.Write(hash.Reverse().ToArray()); // Write Hash
				bw.Write(blte.CompressedSize); // Write Length

				// Update .data file
				if (mode.HasFlag(WriteMode.Data))
				{
					Directory.CreateDirectory(path);

					using (FileStream fs = new FileStream(Path.Combine(path, filename), FileMode.OpenOrCreate, FileAccess.ReadWrite))
					{
						fs.Seek(0, SeekOrigin.End);
						blte.Offset = (ulong)fs.Position;

						ms.Position = 0;
						ms.CopyTo(fs);
						fs.Flush();
					}
				}

				// Output raw
				if (mode.HasFlag(WriteMode.CDN))
				{
					blte.OutPath = Path.Combine(CASContainer.Settings.OutputPath, blte.Hash.ToString());
					using (FileStream fs = new FileStream(blte.OutPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
					{
						ms.Position = 30; // Skip header
						ms.CopyTo(fs);
						fs.Flush();
					}
				}

				return blte;
			}
		}
		#endregion


		private static string GetDataFile(long bytes)
		{
			string path = Path.Combine(CASContainer.BasePath, "Data", "data");
			string prevDataFile = Directory.EnumerateFiles(path, "data.*").OrderByDescending(x => x).First();
			long remaining = (0x40000000L - new FileInfo(prevDataFile).Length);

			if (remaining > bytes) // < 1GB space check
			{
				return prevDataFile;
			}
			else
			{
				int ext = int.Parse(Path.GetExtension(prevDataFile).TrimStart('.')) + 1;
				return Path.Combine(path, "data." + ext.ToString("D3")); // make a new .data file
			}
		}
	}
}
