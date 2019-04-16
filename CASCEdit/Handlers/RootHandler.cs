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
					entry.CEKey = new MD5Hash(stream);
					entry.NameHash = stream.ReadUInt64();
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
					File.Delete(Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(e.CEKey.ToString(), "data")));
				}
			}

		}

		public void AddEntry(string path, CASResult file)
		{
			var cache = CASContainer.Settings.Cache;

			ulong namehash = new Jenkins96().ComputeHash(path);

			var entries = Chunks
						.FindAll(chunk => chunk.LocaleFlags.HasFlag(locale)) // Select locales that match selected locale
						.SelectMany(chunk => chunk.Entries) // Flatten the array to get all entries within all matching chunks
						.Where(e => e.NameHash == namehash);
						
			if (entries.Count() == 0)
            { // New file, we need to create an entry for it
				var cached = cache.Entries.FirstOrDefault(x => x.Path == path);
				var fileDataId = Math.Max(maxId + 1, minimumId);

				if (cached != null) {
					fileDataId = cached.FileDataId;
				}

				var entry = new RootEntry() {
					CEKey = file.CEKey,
					FileDataId = fileDataId,
					FileDataIdOffset = 0,
					NameHash = namehash,
					Path = path
				};

				GlobalRoot.Entries.Add(entry); // Insert into the Global Root
				maxId = Math.Max(entry.FileDataId, maxId); // Update the max id
			}
            else
            { // Existing file, we just have to update the data hash
				foreach (var entry in entries)
                {
					entry.CEKey = file.CEKey;
					entry.Path = path;

					cache?.AddOrUpdate(new CacheEntry(entry, file.EKey));
				}
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
						bw.Write(e.CEKey.Value);
						bw.Write(e.NameHash);
					}
				}

				// create CASCFile
				CASFile entry = new CASFile(ms.ToArray(), encodingMap.Type, encodingMap.CompressionLevel);

				// save and update Build Config
				CASResult res = DataHandler.Write(WriteMode.CDN, entry);
				res.CEKey = new MD5Hash(md5.ComputeHash(ms.ToArray()));
				res.HighPriority = true;
				CASContainer.BuildConfig.Set("root", res.CEKey.ToString());

				CASContainer.Logger.LogInformation($"Root: EKey: {res.EKey} CEKey: {res.CEKey}");

				// cache Root Hash
				CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry() { CEKey = res.CEKey, EKey = res.EKey, Path = "__ROOT__" });

				return res;
			}
		}


		#region File Methods

		public BLTEStream OpenFile(string cascpath)
		{
			var entry = GetEntry(cascpath);
			if (entry != null && CASContainer.EncodingHandler.CEKeys.TryGetValue(entry.CEKey, out EncodingCEKeyPageTable enc))
			{
				LocalIndexEntry idxInfo = CASContainer.LocalIndexHandler.GetIndexInfo(enc.EKeys[0]);
				if (idxInfo != null)
				{
					var path = Path.Combine(CASContainer.BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
					return DataHandler.Read(path, idxInfo);
				}
				else
				{
					return DataHandler.ReadDirect(Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(enc.EKeys[0].ToString(), "data")));
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

				var entries = root.Entries.Where(x => x.NameHash == hash);
				foreach (var entry in entries)
				{
					var blte = CASContainer.EncodingHandler.CEKeys[entry.CEKey].EKeys[0];
					entry.NameHash = newhash;
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
				var entries = root.Entries.Where(x => x.NameHash == hash).ToArray(); // should only ever be one but just incase
				foreach(var entry in entries)
				{
					if (CASContainer.EncodingHandler.CEKeys.TryGetValue(entry.CEKey, out EncodingCEKeyPageTable enc))
					{
						CASContainer.DownloadHandler?.RemoveEntry(enc.EKeys[0]); // remove from download
						CASContainer.CDNIndexHandler?.RemoveEntry(enc.EKeys[0]); // remove from cdn index
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

		public RootEntry GetEntry(ulong hash) => GlobalRoot.Entries.Where(x => x.NameHash == hash).OrderByDescending(x => x.FileDataId).FirstOrDefault();

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
