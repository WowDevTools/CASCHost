using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Structs;
using CASCEdit.Helpers;
using CASCEdit.Handlers;
using CASCEdit.Logging;
using CASCEdit.Configs;
using CASCEdit.Patch;
using CASCEdit.IO;
using System.Security.Cryptography;
using CASCEdit;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;

namespace CASCEdit
{
    public static class CASCContainer
    {
        public static CASCSettings Settings { get; private set; }
        public static string BasePath => Settings.BasePath;
        public static ICASCLog Logger => Settings.Logger;

        public static SingleConfig BuildInfo { get; private set; }
        public static SingleConfig Versions { get; private set; }
        public static SingleConfig CDNs { get; private set; }
        public static MultiConfig BuildConfig { get; private set; }
        public static MultiConfig CDNConfig { get; private set; }

        public static LocalIndexHandler LocalIndexHandler { get; private set; }
        public static CDNIndexHandler CDNIndexHandler { get; private set; }
        public static EncodingHandler EncodingHandler { get; private set; }
        public static RootHandler RootHandler { get; private set; }
        public static DownloadHandler DownloadHandler { get; private set; }
        public static InstallHandler InstallHandler { get; private set; }

        public static void Open(CASCSettings settings)
        {
            Settings = settings;
            Settings.Format();
            Logger.LogInformation("Loading Configs...");

            Settings.Cache?.Load();

            // load previous build / blizzard build
            if (File.Exists(Path.Combine(Settings.OutputPath, Helper.GetCDNPath(".build.info"))))
            {
                LoadBuildInfo(Settings.OutputPath);

                if (BuildInfo["Version"] != Settings.Cache?.Version)
                {
                    Settings.Cache?.Clean();
                    LoadBuildInfo(Settings.SystemFilesPath);
                }
                else
                {
                    Settings.SystemFilesPath = Settings.OutputPath; //Update system path to latest output build
                }
            }
            else if (File.Exists(Path.Combine(settings.SystemFilesPath, ".build.info")))
            {
                LoadBuildInfo(Settings.SystemFilesPath);
            }
            else
            {
                LoadBuildInfo(BasePath);
            }

            // load hosted configs if not basic
            if (!Settings.Basic)
            {
                Versions = new SingleConfig(Settings.PatchUrl + "/versions", "Region", BuildInfo["Branch"]);
                CDNs = new SingleConfig(Settings.PatchUrl + "/cdns", "Name", BuildInfo["Branch"]);

				// download urls
				var cdns = CDNs["Hosts"].Split(' ').Select(x => $"http://{x}/{CDNs["Path"]}").ToList();				
				Settings.DownloadLocations = new HashSet<string>(cdns);
				settings.DownloadLocations.Add($"https://bnet.marlam.in/{CDNs["Path"]}"); //Thanks to Marlamin for this access

				// cdns urls
				foreach (var c in CDNs["Hosts"].Split(' '))
					settings.CDNs.Add(c);
            }

            LoadBuildConfig();
            LoadCDNConfig();
        }


        #region Load Configs
        private static void LoadBuildInfo(string path)
        {
            string buildInfoPath = Path.Combine(path, ".build.info");
            if (!File.Exists(buildInfoPath))
            {
                Logger.LogCritical("Missing Build Info", buildInfoPath);
                return;
            }

            BuildInfo = new SingleConfig(buildInfoPath, "Active", "1");
        }

        private static void LoadBuildConfig()
        {
            string buildKey = BuildInfo["Build Key"];
            string buildCfgPath = Path.Combine(Settings.SystemFilesPath, buildKey);
            string buildCfgLocalPath = Path.Combine(BasePath, "Data", "config", buildKey.Substring(0, 2), buildKey.Substring(2, 2), buildKey);

            if (File.Exists(buildCfgLocalPath))
            {
                BuildConfig = new MultiConfig(buildCfgLocalPath);
            }
            else
            {
                buildCfgPath = Helper.FixOutputPath(buildCfgPath, "config");

                if (!File.Exists(buildCfgPath))
                {
                    string url = "/config/" + buildKey.Substring(0, 2) + "/" + buildKey.Substring(2, 2) + "/" + buildKey;
                    if(!DataHandler.Download(url, buildCfgPath))
                        Logger.LogCritical($"Unable to download Build Config {buildKey}.");
                }

                BuildConfig = new MultiConfig(buildCfgPath);
            }
        }

        private static void LoadCDNConfig()
        {
            string cdnKey = BuildInfo["CDN Key"];
            string cdnCfgPath = Path.Combine(Settings.SystemFilesPath, cdnKey);
            string cdnCfgLocalPath = Path.Combine(BasePath, "Data", "config", cdnKey.Substring(0, 2), cdnKey.Substring(2, 2), cdnKey);

            if (File.Exists(cdnCfgLocalPath))
            {
                CDNConfig = new MultiConfig(cdnCfgLocalPath);
            }
            else
            {
                cdnCfgPath = Helper.FixOutputPath(cdnCfgPath, "config");

                if (!File.Exists(cdnCfgPath))
                {
                    string url = "/config/" + cdnKey.Substring(0, 2) + "/" + cdnKey.Substring(2, 2) + "/" + cdnKey;
                    if(!DataHandler.Download(url, cdnCfgPath))
                        Logger.LogCritical($"Unable to download CDN Config {cdnKey}.");
                }

                CDNConfig = new MultiConfig(cdnCfgPath);
            }
        }
        #endregion

        #region Load Handlers
        public static void OpenLocalIndices()
        {
            LocalIndexHandler = new LocalIndexHandler();
        }

        public static void OpenCdnIndices(bool load)
        {
            if (CDNConfig != null)
                CDNIndexHandler = new CDNIndexHandler(load);
        }

        public static void OpenEncoding()
        {
            Logger.LogInformation("Loading Encoding...");

            string key = BuildConfig.GetKey("encoding").ToString();
            var path = Path.Combine(Settings.SystemFilesPath, key);

            LocalIndexEntry idxInfo = LocalIndexHandler?.GetIndexInfo(BuildConfig.GetKey("encoding"));
            if (idxInfo != null)
            {
                path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                EncodingHandler = new EncodingHandler(DataHandler.Read(path, idxInfo));
            }
            else
            {
                path = Helper.FixOutputPath(path, "data");

                if (!File.Exists(path))
                {
                    string url = "/data/" + key.Substring(0, 2) + "/" + key.Substring(2, 2) + "/" + key;
                    if(!DataHandler.Download(url, path))
                        Logger.LogCritical($"Unable to download Encoding {key}.");
                }

                EncodingHandler = new EncodingHandler(DataHandler.ReadDirect(path));
            }
        }

        public static void OpenRoot(LocaleFlags locale, uint minimumid = 0)
        {
            Logger.LogInformation("Loading Root...");

            var rootkey = BuildConfig.GetKey("root");
            if (!EncodingHandler.Data.TryGetValue(rootkey, out EncodingEntry enc))
            {
                Logger.LogCritical($"Encoding missing Root {rootkey.ToString()}");
                return;
            }

            LocalIndexEntry idxInfo = LocalIndexHandler?.GetIndexInfo(enc.Keys[0]);
            if (idxInfo != null)
            {
                var path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                RootHandler = new RootHandler(DataHandler.Read(path, idxInfo), locale, minimumid);
            }
            else
            {
                string key = enc.Keys[0].ToString();
                string path = Path.Combine(Settings.SystemFilesPath, key);
                path = Helper.FixOutputPath(path, "data");

                if (!File.Exists(path))
                {
                    string url = "/data/" + key.Substring(0, 2) + "/" + key.Substring(2, 2) + "/" + key;
                    if(!DataHandler.Download(url, path))
                        Logger.LogCritical($"Unable to download Root {key}.");
                }

                RootHandler = new RootHandler(DataHandler.ReadDirect(path), locale, minimumid);
            }
        }

        public static void OpenDownload()
        {
            Logger.LogInformation("Loading Download...");

            var downloadkey = BuildConfig.GetKey("download");
            if (!EncodingHandler.Data.TryGetValue(downloadkey, out EncodingEntry enc))
            {
                Logger.LogCritical($"Encoding missing Download {downloadkey.ToString()}");
                return;
            }

            LocalIndexEntry idxInfo = LocalIndexHandler?.GetIndexInfo(enc.Keys[0]);
            if (idxInfo != null)
            {
                var path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                DownloadHandler = new DownloadHandler(DataHandler.Read(path, idxInfo));
            }
            else
            {
                string key = enc.Keys[0].ToString();
                string path = Path.Combine(Settings.SystemFilesPath, key);
                path = Helper.FixOutputPath(path, "data");

                if (!File.Exists(path))
                {
                    string url = "/data/" + key.Substring(0, 2) + "/" + key.Substring(2, 2) + "/" + key;
                    if(!DataHandler.Download(url, path))
                        Logger.LogCritical($"Unable to download Download {key}.");
                }

                DownloadHandler = new DownloadHandler(DataHandler.ReadDirect(path));
            }
        }

        public static void OpenInstall()
        {
            Logger.LogInformation("Loading Install...");

            var installkey = BuildConfig.GetKey("install");
            if (!EncodingHandler.Data.TryGetValue(installkey, out EncodingEntry enc))
            {
                Logger.LogCritical($"Encoding missing Install {installkey.ToString()}");
                return;
            }

            LocalIndexEntry idxInfo = LocalIndexHandler?.GetIndexInfo(enc.Keys[0]);
            if (idxInfo != null)
            {
                var path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                InstallHandler = new InstallHandler(DataHandler.Read(path, idxInfo));
            }
            else
            {
                string key = enc.Keys[0].ToString();
                string path = Path.Combine(Settings.SystemFilesPath, key);
                path = Helper.FixOutputPath(path, "data");

                if (!File.Exists(path))
                {
                    string url = "/data/" + key.Substring(0, 2) + "/" + key.Substring(2, 2) + "/" + key;
                    if(!DataHandler.Download(url, path))
                        Logger.LogCritical($"Unable to download Install {key}.");
                }

                InstallHandler = new InstallHandler(DataHandler.ReadDirect(path));
            }
        }

        public static void DownloadInstallAssets()
        {
            Logger.LogInformation("Loading Install Assets...");

            List<string> assets = new List<string>()
            {
                { "blobs" },
                { "bgdl" },
                { "blob/game" },
                { "blob/install" }
            };

            foreach(string asset in assets)
            {
                string outPath = Path.Combine(Settings.OutputPath, Path.GetFileName(asset));

                if(Settings.StaticMode)
                {
                    outPath = Path.Combine(Settings.OutputPath, "wow", asset);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                if(!File.Exists(outPath)) {
                    if (!DataHandler.Download(Settings.PatchUrl + "/" + asset, outPath))
                        Logger.LogCritical($"Unable to download Install {asset}.");
                }
            }
        }
        #endregion


        #region Save CASC
        public static void Save()
		{
			Settings.Cache?.Clean();

			//Patch exe
			//Patcher.Run("http://" + CASCContainer.Settings.Host);

			// Entries
			var entries = SaveEntries().Result;

			// CDN Archives
			Settings.Logger.LogInformation("Starting CDN Index.");
			CDNIndexHandler?.CreateArchive(entries);

			// Root
			Settings.Logger.LogInformation("Starting Root.");
			foreach (var entry in entries)
				RootHandler.AddEntry(entry.Path, entry);
			entries.Add(RootHandler.Write()); //Add to entry list

			// Download
			if (DownloadHandler != null)
			{
				Settings.Logger.LogInformation("Starting Download.");

				foreach (var entry in entries)
					DownloadHandler.AddEntry(entry);
				entries.Add(DownloadHandler.Write()); //Add to entry list
			}

			if(InstallHandler != null)
			{
				Settings.Logger.LogInformation("Starting Install.");
				InstallHandler.Write(entries);
			}

			// Encoding
			Settings.Logger.LogInformation("Starting Encoding.");
			foreach (var entry in entries)
				EncodingHandler.AddEntry(entry);
			entries.Insert(0, EncodingHandler.Write());


			Settings.Logger.LogInformation("Starting Configs.");

			// CDN Config
			CDNConfig.Remove("archive-group");
			CDNConfig.Remove("patch-archives");
			CDNConfig.Remove("patch-archive-group");

			// Build Config
			BuildConfig.Set("patch", "");
			BuildConfig.Set("patch-size", "0");
			BuildConfig.Set("patch-config", "");

			string buildconfig = BuildConfig.Write();
			string cdnconfig = CDNConfig.Write();
			string version = BuildInfo["Version"];

			// Build Info - redundant
			BuildInfo["Build Key"] = buildconfig;
			BuildInfo["CDN Key"] = cdnconfig;
			BuildInfo["CDN Hosts"] = string.Join(" ", Settings.CDNs);
			BuildInfo.Write();

			// CDNs file
			CDNs["Hosts"] = string.Join(" ", Settings.CDNs);
			CDNs.Write();

			// Versions file
			Versions["BuildConfig"] = buildconfig;
			Versions["CDNConfig"] = cdnconfig;
			Versions["VersionsName"] = version;
			Versions["BuildId"] = version.Split('.').Last();
			Versions.Write();

			// Done!
			Logger.LogInformation("CDN Config: " + cdnconfig);
			Logger.LogInformation("Build Config: " + buildconfig);

			// update Cache files
			Settings.Cache?.Save();

			// cleanup 
			entries.Clear();
			entries.TrimExcess();
			Close();
		}

		private async static Task<List<CASCResult>> SaveEntries()
		{
			// generate BLTE encoded files
			ConcurrentBag<CASCResult> entries = new ConcurrentBag<CASCResult>();
			Func<KeyValuePair<string, CASCFile>, bool> BuildBLTE = (file) =>
			{
				CASCResult res = DataHandler.Write(WriteMode.CDN, file.Value);
				res.DataHash = file.Value.DataHash;
				res.Path = file.Key;
				//res.HighPriority - unneeded really
				entries.Add(res);

				Logger.LogInformation($"{Path.GetFileName(res.Path)}: Hash: {res.Hash} Data: {res.DataHash}");
				return true;
			};

			// batch parallel blte encoding
			var encoder = new TransformBlock<KeyValuePair<string, CASCFile>, bool>(
				file => BuildBLTE(file),
				new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 500 }
			);

			var buffer = new BufferBlock<bool>();
			encoder.LinkTo(buffer);

			foreach (var file in RootHandler.NewFiles)
				encoder.Post(file);

			encoder.Complete();
			await encoder.Completion;

			RootHandler.NewFiles.Clear();
			return entries.ToList();
		}
		#endregion



        public static bool ExtractSystemFiles(string savepath)
        {
            if (Directory.Exists(savepath))
                Directory.Delete(savepath, true);
            Directory.CreateDirectory(savepath);

            Logger.LogInformation("Extracting System Files");

            OpenLocalIndices();

            // .build.info
			if(File.Exists(Path.Combine(BasePath, ".build.info")))
				File.Copy(Path.Combine(BasePath, ".build.info"), Path.Combine(savepath, ".build.info"), true);         

            // Build Config
            string buildKey = BuildInfo["Build Key"];
            string buildCfgLocalPath = Path.Combine(BasePath, "Data", "config", buildKey.Substring(0, 2), buildKey.Substring(2, 2), buildKey);
            if (File.Exists(buildCfgLocalPath))
            {
                File.Copy(buildCfgLocalPath, Path.Combine(savepath, buildKey), true);
            }
            else
            {
                Logger.LogCritical("Build Config missing.");
                return false;
            }

            // CDN Config
            string cdnKey = BuildInfo["CDN Key"];
            string cdnCfgLocalPath = Path.Combine(BasePath, "Data", "config", cdnKey.Substring(0, 2), cdnKey.Substring(2, 2), cdnKey);
            if (File.Exists(cdnCfgLocalPath))
            {
                File.Copy(cdnCfgLocalPath, Path.Combine(savepath, cdnKey), true);
            }
            else
            {
                Logger.LogCritical("CDN Config missing.");
                return false;
            }


            string path;

            // Encoding File
            LocalIndexEntry idxInfo = LocalIndexHandler?.GetIndexInfo(BuildConfig.GetKey("encoding"));
            if (idxInfo != null)
            {
                path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                DataHandler.Extract(path, Path.Combine(savepath, BuildConfig.GetKey("encoding").ToString()), idxInfo);
                OpenEncoding();
            }
            else
            {
                Logger.LogCritical("Encoding file missing.");
                return false;
            }

            // Root File
            var rootkey = BuildConfig.GetKey("root");
            if (EncodingHandler.Data.TryGetValue(rootkey, out EncodingEntry enc))
            {
                idxInfo = LocalIndexHandler?.GetIndexInfo(enc.Keys[0]);
                if (idxInfo != null)
                {
                    path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                    DataHandler.Extract(path, Path.Combine(savepath, enc.Keys[0].ToString()), idxInfo);
                }
                else
                {
                    Logger.LogCritical("Root file missing.");
                    return false;
                }
            }

            // Install File
            var installkey = BuildConfig.GetKey("install");
            if (EncodingHandler.Data.TryGetValue(installkey, out enc))
            {
                idxInfo = LocalIndexHandler?.GetIndexInfo(enc.Keys[0]);
                if (idxInfo != null)
                {
                    path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                    DataHandler.Extract(path, Path.Combine(savepath, enc.Keys[0].ToString()), idxInfo);
                }
                else
                {
                    Logger.LogCritical("Install file missing.");
                    return false;
                }
            }

            // Download File
            var downloadkey = BuildConfig.GetKey("download");
            if (EncodingHandler.Data.TryGetValue(downloadkey, out enc))
            {
                idxInfo = LocalIndexHandler?.GetIndexInfo(enc.Keys[0]);
                if (idxInfo != null)
                {
                    path = Path.Combine(BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
                    DataHandler.Extract(path, Path.Combine(savepath, enc.Keys[0].ToString()), idxInfo);
                }
                else
                {
                    Logger.LogCritical("Download file missing.");
                    return false;
                }
            }

            return true;
        }


        public static void Close()
        {
            RootHandler?.NewFiles.Clear();

            Settings = null;

            BuildInfo = null;
            Versions = null;
            CDNs = null;
            BuildConfig = null;
            CDNConfig = null;

            LocalIndexHandler = null;
            CDNIndexHandler = null;
            DownloadHandler = null;
            InstallHandler = null;

            EncodingHandler?.Dispose();
            RootHandler?.Dispose();

            // force GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
