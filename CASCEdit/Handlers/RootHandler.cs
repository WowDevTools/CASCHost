using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using System.Threading.Tasks;
using System.Diagnostics;
using CASCEdit.IO;

namespace CASCEdit.Handlers
{
	public class RootHandler : IDisposable
	{
		public RootChunk GlobalRoot { get; private set; }
		public SortedDictionary<string, CASFile> NewFiles { get; private set; } = new SortedDictionary<string, CASFile>();
		public List<RootChunk> Chunks { get; private set; } = new List<RootChunk>();

		private LocaleFlags locale;
		private uint maxId = 0;
		private readonly uint minimumId;
		private readonly EncodingMap encodingMap;

		public RootHandler()
		{
			GlobalRoot = new RootChunk() { ContentFlags = ContentFlags.None, LocaleFlags = LocaleFlags.All_WoW };
			encodingMap = new EncodingMap(EncodingType.ZLib, 9);
		}

		public RootHandler(Stream data, LocaleFlags locale, uint minimumid = 0)
		{
			this.minimumId = minimumid;
			this.locale = locale;

			BinaryReader stream = new BinaryReader(data);

			long length = stream.BaseStream.Length;
			while (stream.BaseStream.Position < length)
			{
				RootChunk chunk = new RootChunk()
				{
					Count = stream.ReadUInt32(),
					ContentFlags = (ContentFlags)stream.ReadUInt32(),
					LocaleFlags = (LocaleFlags)stream.ReadUInt32(),
				};

				// set the global root
				if (chunk.LocaleFlags == LocaleFlags.All_WoW && chunk.ContentFlags == ContentFlags.None)
					GlobalRoot = chunk;

				uint fileDataIndex = 0;
				for (int i = 0; i < chunk.Count; i++)
				{
					uint offset = stream.ReadUInt32();

					RootEntry entry = new RootEntry()
					{
						FileDataIdOffset = offset,
						FileDataId = fileDataIndex + offset
					};

					fileDataIndex = entry.FileDataId + 1;
					chunk.Entries.Add(entry);
				}

				foreach (var entry in chunk.Entries)
				{
					entry.MD5 = new MD5Hash(stream);
					entry.Hash = stream.ReadUInt64();
					maxId = Math.Max(maxId, entry.FileDataId);
				}

				Chunks.Add(chunk);
			}

			if (GlobalRoot == null)
			{
				CASContainer.Logger.LogCritical($"No Global root found. Root file is corrupt.");
				return;
			}

			// set maxid from cache
			maxId = Math.Max(Math.Max(maxId, minimumid), CASContainer.Settings.Cache?.MaxId ?? 0);

			// store encoding map
			encodingMap = (data as BLTEStream)?.EncodingMap.FirstOrDefault() ?? new EncodingMap(EncodingType.ZLib, 9);

			stream?.Dispose();
			data?.Dispose();
		}

		public void RemoveDeleted()
		{
			if (CASContainer.Settings?.Cache == null)
				return;

			var entries = GlobalRoot.Entries.Where(x => x.FileDataId >= minimumId).ToList(); // avoid collection change errors
			foreach (var e in entries)
			{
				if (!CASContainer.Settings.Cache.HasId(e.FileDataId))
				{
					GlobalRoot.Entries.Remove(e);
					File.Delete(Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(e.MD5.ToString(), "data")));
				}
			}

		}

		public void AddEntry(string path, CASResult file)
		{
			ulong hash = new Jenkins96().ComputeHash(path);
			bool found = false;

			// check to see if we're overwriting an existing entry
			foreach (var root in Chunks)
			{
				if (!root.LocaleFlags.HasFlag(locale) && root != GlobalRoot) // skip incorrect locales and non-global roots
					continue;

				var index = root.Entries.FindIndex(x => x.Hash == hash);
				if (index >= 0)
				{
					RootEntry entry = new RootEntry()
					{
						MD5 = file.DataHash,
						Hash = hash,
						Path = path,
						FileDataId = root.Entries[index].FileDataId,
						FileDataIdOffset = root.Entries[index].FileDataIdOffset
					};

					root.Entries[index] = entry; // update
					found = true;

					// max id check just to be safe
					if (root == GlobalRoot)
						maxId = Math.Max(entry.FileDataId, maxId); // update max id				

					CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry(entry, file.Hash));
				}
			}

			// must be a new file, add it to the global root
			if (!found)
			{
				RootEntry entry = new RootEntry()
				{
					MD5 = file.DataHash,
					FileDataIdOffset = 0,
					Hash = hash,
					Path = path
				};

				var cache = CASContainer.Settings.Cache.Entries.FirstOrDefault(x => x.Path == path);
				if (cache?.Path != path) // get cache id
					entry.FileDataId = Math.Max(maxId + 1, minimumId); // calculate the Id 

				GlobalRoot.Entries.Add(entry); // add new

				maxId = Math.Max(entry.FileDataId, maxId); // Update max id
				CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry(entry, file.Hash));
			}
		}

		private void FixOffsets()
		{
			foreach (var root in Chunks)
			{
				root.Entries.Sort((x, y) => x.FileDataId.CompareTo(y.FileDataId));

				for (int i = 1; i < root.Entries.Count; i++)
				{
					var prevId = root.Entries[i - 1].FileDataId;
					var current = root.Entries[i];

					if (prevId + current.FileDataIdOffset + 1 != current.FileDataId)
						current.FileDataIdOffset = current.FileDataId - prevId - 1;
				}
			}
		}

		public CASResult Write()
		{
			FixOffsets();

			using (var md5 = MD5.Create())
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms))
			{
				// write each chunk
				foreach (var c in Chunks)
				{
					bw.Write((uint)c.Entries.Count);
					bw.Write((uint)c.ContentFlags);
					bw.Write((uint)c.LocaleFlags);

					foreach (var e in c.Entries)
						bw.Write(e.FileDataIdOffset);

					foreach (var e in c.Entries)
					{
						bw.Write(e.MD5.Value);
						bw.Write(e.Hash);
					}
				}

				// create CASCFile
				CASFile entry = new CASFile(ms.ToArray(), encodingMap.Type, encodingMap.CompressionLevel);

				// save and update Build Config
				CASResult res = DataHandler.Write(WriteMode.CDN, entry);
				res.DataHash = new MD5Hash(md5.ComputeHash(ms.ToArray()));
				res.HighPriority = true;
				CASContainer.BuildConfig.Set("root", res.DataHash.ToString());

				CASContainer.Logger.LogInformation($"Root: Hash: {res.Hash} Data: {res.DataHash}");

				// cache Root Hash
				CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry() { MD5 = res.DataHash, BLTE = res.Hash, Path = "__ROOT__" });

				return res;
			}
		}


		#region File Methods

		public BLTEStream OpenFile(string cascpath)
		{
			var entry = GetEntry(cascpath);
			if (entry != null && CASContainer.EncodingHandler.CEKeys.TryGetValue(entry.MD5, out EncodingEntry enc))
			{
				LocalIndexEntry idxInfo = CASContainer.LocalIndexHandler.GetIndexInfo(enc.Keys[0]);
				if (idxInfo != null)
				{
					var path = Path.Combine(CASContainer.BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
					return DataHandler.Read(path, idxInfo);
				}
				else
				{
					return DataHandler.ReadDirect(Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(enc.Keys[0].ToString(), "data")));
				}
			}

			return null;
		}

		public void AddFile(string filepath, string cascpath, EncodingType encoding = EncodingType.ZLib, byte compression = 9)
		{
			if (File.Exists(filepath))
				NewFiles.Add(cascpath, new CASFile(File.ReadAllBytes(filepath), encoding, compression));
		}

		public void RenameFile(string path, string newpath)
		{
			ulong hash = new Jenkins96().ComputeHash(path);
			ulong newhash = new Jenkins96().ComputeHash(newpath);

			foreach (var root in Chunks)
			{
				if (!root.LocaleFlags.HasFlag(locale) && root != GlobalRoot) // ignore incorrect locale and not global
					continue;

				var entries = root.Entries.Where(x => x.Hash == hash);
				foreach (var entry in entries)
				{
					var blte = CASContainer.EncodingHandler.CEKeys[entry.MD5].EKeys[0];
					entry.Hash = newhash;
					entry.Path = path;

					CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry(entry, blte));
				}
			}
		}

		public void RemoveFile(string path)
		{
			ulong hash = new Jenkins96().ComputeHash(path);

			foreach (var root in Chunks)
			{
				var entries = root.Entries.Where(x => x.Hash == hash).ToArray(); // should only ever be one but just incase
				foreach(var entry in entries)
				{
					if (CASContainer.EncodingHandler.CEKeys.TryGetValue(entry.MD5, out EncodingEntry enc))
					{
						CASContainer.DownloadHandler?.RemoveEntry(enc.Keys[0]); // remove from download
						CASContainer.CDNIndexHandler?.RemoveEntry(enc.Keys[0]); // remove from cdn index
					}

					root.Entries.Remove(entry);
					CASContainer.Settings.Cache?.Remove(path);
				}
			}
		}

		#endregion


		#region Entry Methods

		public RootEntry GetEntry(string cascpath) => GetEntry(new Jenkins96().ComputeHash(cascpath));

		public RootEntry GetEntry(uint fileid) => GlobalRoot.Entries.Where(x => x.FileDataId == fileid).OrderByDescending(x => x.FileDataId).FirstOrDefault();

		public RootEntry GetEntry(ulong hash) => GlobalRoot.Entries.Where(x => x.Hash == hash).OrderByDescending(x => x.FileDataId).FirstOrDefault();

		#endregion


		public void Dispose()
		{
			NewFiles.Clear();
			NewFiles = null;
			Chunks.Clear();
			Chunks.TrimExcess();
			Chunks = null;
		}
	}
}
