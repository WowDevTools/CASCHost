using System;
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
	public class CDNIndexHandler
	{
		public List<ArchiveIndexHandler> Archives = new List<ArchiveIndexHandler>();

		public CDNIndexHandler(bool load)
		{
			if (load)
			{
				CASContainer.Logger.LogInformation("Loading CDN Indices...");

				for (int i = 0; i < CASContainer.CDNConfig["archives"].Count; i++)
				{
					string archive = CASContainer.CDNConfig["archives"][i];
					string path = Path.Combine(CASContainer.BasePath, "Data", "indices", archive + ".index");

					Archives.Add(new ArchiveIndexHandler(path));
				}
			}
			else
			{
				Archives.Add(OpenOrCreate());
			}
		}


		public void CreateArchive(List<CASResult> entries)
		{
			ArchiveIndexHandler archive = Archives[0];

			//Remove missing existing entries
			archive.Entries.RemoveAll(x => !File.Exists(Path.Combine(CASContainer.Settings.OutputPath, x.Hash.ToString())));

			//Add entries
			foreach (var blte in entries)
			{
				if (!File.Exists(Path.Combine(CASContainer.Settings.OutputPath, blte.Hash.ToString())))
					continue;

				var entry = new IndexEntry()
				{
					Hash = blte.Hash,
					Size = blte.CompressedSize - 30, //BLTE minus header
				};

				archive.Entries.RemoveAll(x => x.Hash == entry.Hash);
				archive.Entries.Add(entry);
			}

			//Archive is now empty - better remove it
			if (archive.Entries.Count == 0)
			{
				if (File.Exists(archive.BaseFile))
					File.Delete(archive.BaseFile);
				CASContainer.CDNConfig["archives"].RemoveAll(x => x == Path.GetFileNameWithoutExtension(archive.BaseFile));
				return;
			}

			//Save
			string path = archive.Write();

			//Create data file
			CASContainer.Logger.LogInformation("Saving CDN Index data... This may take a while.");
			string datapath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
			using (var fs = new FileStream(datapath, FileMode.Create, FileAccess.Write, FileShare.Read))
			{
				fs.Position = 0;

				foreach (var entry in archive.Entries)
				{
					string entrypath = Path.Combine(CASContainer.Settings.OutputPath, entry.Hash.ToString());
					new FileInfo(entrypath).OpenRead().CopyTo(fs);
					fs.Flush();
				}
			}

			//Remove old
			if (!string.IsNullOrWhiteSpace(archive.BaseFile))
			{
				string oldata = Path.Combine(Path.GetDirectoryName(archive.BaseFile), Path.GetFileNameWithoutExtension(archive.BaseFile));
				File.Delete(archive.BaseFile);
				File.Delete(oldata);
			}

			CASContainer.Logger.LogInformation("CDN Index: " + Path.GetFileName(path));
		}

		public void RemoveEntry(MD5Hash hash)
		{
			Archives.ForEach(x => x.Entries.RemoveAll(y => y.Hash == hash));
		}


		private ArchiveIndexHandler OpenOrCreate()
		{
			var files = Directory.EnumerateFiles(CASContainer.Settings.OutputPath, "*.index");
			if (!files.Any())
				return new ArchiveIndexHandler();
			else
				return new ArchiveIndexHandler(files.First());
		}

	}
}
