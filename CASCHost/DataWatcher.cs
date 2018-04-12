using CASCEdit;
using CASCEdit.Structs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CASCHost
{
	public class DataWatcher : IDisposable
	{
		public bool RebuildInProgress { get; private set; }

		private IHostingEnvironment _env;
		private FileSystemWatcher watcher;
		private Timer timer;
		private readonly string dataPath;
		private readonly string outputPath;
		private ConcurrentDictionary<string, FileSystemEventArgs> changes;
		private CASCSettings settings;
		private DateTime lastBuild;


        public DataWatcher(IHostingEnvironment env)
		{
			_env = env;
			dataPath = Path.Combine(env.WebRootPath, "Data");
			outputPath = Path.Combine(env.WebRootPath, "Output");
            changes = new ConcurrentDictionary<string, FileSystemEventArgs>();
            lastBuild = new DateTime();

			LoadSettings();
            if (settings.StaticMode)
            {
                ForceRebuild();
                return;
            }

            //Rebuild if files have changed since last run otherwise wait for a change to occur
            if (!IsRebuildRequired())
				timer = new Timer(UpdateCASCDirectory, null, Timeout.Infinite, Timeout.Infinite);

            watcher = new FileSystemWatcher()
			{
				Path = dataPath,
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
				EnableRaisingEvents = false /*Startup.Settings.RebuildOnChange*/,
				IncludeSubdirectories = true
			};

			watcher.Changed += LogChange;
			watcher.Created += LogChange;
			watcher.Deleted += LogChange;
			watcher.Renamed += LogChange;
        }


		#region Change Detection
		private void LogChange(object sender, FileSystemEventArgs e)
		{
			//Ignore folder changes - rename is handled below all files fire this event themselves
			if (IsDirectory(e.FullPath))
				return;

			//Assume anything extensionless is a folder
			if (string.IsNullOrWhiteSpace(Path.GetExtension(e.FullPath)))
				return;

            //Update or add change
            changes.AddOrUpdate(e.FullPath, e, (k, v) => e);

            //Add delay for user to finishing changing files
            timer.Change(30 * 1000, 0);
		}

		private void LogChange(object sender, RenamedEventArgs e)
		{
			if (IsDirectory(e.FullPath))
			{
				var files = Directory.EnumerateFiles(e.FullPath, "*.*", SearchOption.AllDirectories);
				if (!files.Any())
					return;

				//Update files in directory
				foreach (var f in files)
				{
					var arg = new RenamedEventArgs(e.ChangeType, Path.GetDirectoryName(f), f, f.Replace(e.FullPath, e.OldFullPath));
                    changes.AddOrUpdate(f, arg, (k, v) => arg); //Set as renamed file
                }
            }
			else
			{
                changes.AddOrUpdate(e.FullPath, e, (k, v) => e);
            }

            //Add delay for user to finishing changing files
            timer.Change((int)(30 * 1000), 0);
		}

		private bool IsDirectory(string dir) => Directory.Exists(dir) || (File.Exists(dir) && File.GetAttributes(dir).HasFlag(FileAttributes.Directory));
		#endregion


		#region CASC Update
		public void ForceRebuild()
		{
            //Rebuild all files
            var files = Directory.EnumerateFiles(dataPath, "*.*", SearchOption.AllDirectories).OrderBy(f => f);
            foreach (var f in files)
			{
				if(File.GetLastWriteTime(f) >= lastBuild || File.GetCreationTime(f) >= lastBuild)
				{
					var args = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(f), Path.GetFileName(f));
                    changes.AddOrUpdate(f, args, (k, v) => args);
                }
            }

            timer = new Timer(UpdateCASCDirectory, null, 0, Timeout.Infinite);
		}

		private void UpdateCASCDirectory(object obj)
		{
			if (RebuildInProgress) //Saving already wait for build to finish
			{
				timer.Change(30 * 1000, Timeout.Infinite); //30 second delay
				return;
			}

			RebuildInProgress = true;
			timer.Change(Timeout.Infinite, Timeout.Infinite);

			Startup.Logger.LogWarning($"CASC rebuild started [{DateTime.Now}] - {changes.Count} files to be amended.");
			Stopwatch sw = Stopwatch.StartNew();

			//Open the CASC Container
			CASCContainer.Open(settings);
			CASCContainer.OpenCdnIndices(false);
			CASCContainer.OpenEncoding();
			CASCContainer.OpenRoot(settings.Locale, Startup.Settings.MinimumFileDataId);

			if(Startup.Settings.BNetAppSupport) // these are only needed by the bnet app launcher
			{
				CASCContainer.OpenDownload();
				CASCContainer.OpenInstall();
				CASCContainer.DownloadInstallAssets();
            }

			//Remove Purged files
			foreach (var purge in Startup.Cache.ToPurge)
				CASCContainer.RootHandler.RemoveFile(purge);

			//Apply file changes
			while (changes.Count > 0)
			{
				var key = changes.Keys.First();
                if (changes.TryRemove(key, out FileSystemEventArgs change))
                {
                    string fullpath = change.FullPath;
					string cascpath = GetCASCPath(fullpath);
					string oldpath = GetCASCPath((change as RenamedEventArgs)?.OldFullPath + "");

					switch (change.ChangeType)
					{
						case WatcherChangeTypes.Renamed:
							if (CASCContainer.RootHandler.GetEntry(oldpath) == null)
								CASCContainer.RootHandler.AddFile(fullpath, cascpath);
							else
								CASCContainer.RootHandler.RenameFile(oldpath, cascpath);
							break;
						case WatcherChangeTypes.Deleted:
							CASCContainer.RootHandler.RemoveFile(cascpath);
							break;
						default:
							CASCContainer.RootHandler.AddFile(fullpath, cascpath);
							break;
					}
				}
			}

			//Save and Clean
			CASCContainer.Save();

			//Update directory hashes
			Startup.Settings.DirectoryHash = new[]
			{
				GetDirectoryHash(dataPath),
				GetDirectoryHash(outputPath)
			};
			Startup.Settings.Save(_env);

			sw.Stop();
			Startup.Logger.LogWarning($"CASC rebuild finished [{DateTime.Now}] - {Math.Round(sw.Elapsed.TotalSeconds, 3)}s");

            if (settings.StaticMode)
            {
                Environment.Exit(0);
            }

            lastBuild = DateTime.Now;
			RebuildInProgress = false;
		}

		private string GetCASCPath(string file)
		{
			string lookup = new DirectoryInfo(_env.WebRootPath).Name;
			string[] parts = file.Split(Path.DirectorySeparatorChar);
			return Path.Combine(parts.Skip(Array.IndexOf(parts, lookup) + 2).ToArray()); //Remove top directories
		}
		#endregion


		private bool IsRebuildRequired()
		{
			Startup.Logger.LogInformation("Offline file change check.");

			//No data files
			if (!Directory.EnumerateFiles(dataPath, "*.*", SearchOption.AllDirectories).Any())
				return false;

			string[] hashes = new[]
			{
				GetDirectoryHash(dataPath),
				GetDirectoryHash(outputPath)
			};

			//Check for offline changes
			if (!hashes.SequenceEqual(Startup.Settings.DirectoryHash) /*|| Startup.Settings.RebuildOnLoad*/)
			{
				ForceRebuild();
				return true;
			}

			return false;
		}

		private string GetDirectoryHash(string directory)
		{
			var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories).OrderBy(x => x);
			using (IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
			{
				foreach (var f in files)
				{
					FileInfo info = new FileInfo(f);
					md5.AppendData(Encoding.UTF8.GetBytes(info.FullName)); //path
					md5.AppendData(BitConverter.GetBytes(info.Length)); //size
					md5.AppendData(BitConverter.GetBytes(info.LastWriteTimeUtc.Ticks)); //last written
				}

				return (!files.Any() ? new byte[16] : md5.GetHashAndReset()).ToMD5String(); //Enforce empty hash string if no files
			}
		}

		private void LoadSettings()
		{
			LocaleFlags locale = Enum.TryParse(Startup.Settings.Locale, true, out LocaleFlags tmp) ? tmp : LocaleFlags.enUS;

			Startup.Logger.LogConsole($"Default Locale set to {locale}.");

			settings = new CASCSettings()
			{
				Host = Startup.Settings.HostDomain,
				BasePath = _env.WebRootPath,
				OutputPath = "Output",
				SystemFilesPath = "SystemFiles",
				PatchUrl = Startup.Settings.PatchUrl,
				Logger = Startup.Logger,
				Cache = Startup.Cache,
				Locale = locale,
				CDNs = new HashSet<string>(),
                StaticMode = Startup.Settings.StaticMode
            };

			settings.CDNs.Add(settings.Host);

			if (Startup.Settings.CDNs != null)
				foreach (var cdn in Startup.Settings.CDNs)
					settings.CDNs.Add(cdn);
		}


		public void Dispose()
		{
			changes?.Clear();
			changes = null;
			timer?.Dispose();
			timer = null;
			watcher?.Dispose();
			watcher = null;
		}
	}
}
