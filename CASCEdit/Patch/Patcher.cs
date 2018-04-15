using CASCEdit.IO;
using CASCEdit.Handlers;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CASCEdit.Patch
{
    public static class Patcher
    {
        const string AGENT_REGEX = @"(agent.(exe|app))";
        const string URL_REGEX = @"(?:(?:https?):\/\/|www\.)(?:\([-A-Z0-9+&@#\/%=~_|$?!:,.]*\)|[-A-Z0-9+&@#\/%=~_|$?!:,.])*(?:\([-A-Z0-9+&@#\/%=~_|$?!:,.]*\)|[A-Z0-9+&@#\/%=~_|$]\/(?:versions|cdns))";

        private static readonly string[] Files = new[]
        {
            "Wow.exe",
            "Wow-64.exe",
            @"World of Warcraft.app\Contents\MacOS\World of Warcraft"
        };

        public static void Run(string host)
        {
            CASContainer.OpenInstall();
            Parallel.ForEach(Files, file => Patch(file, host));
        }

        public static void Patch(string file, string host)
        {
            CASContainer.Logger.LogInformation($"Patching {Path.GetFileName(file)}");

            BLTEStream stream = OpenFile(file);
            if (stream == null)
                return;

            string outpath = Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(Path.GetFileName(file), "data"));
            using (var fs = new FileStream(outpath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs))
            {
                stream.CopyTo(fs);

                stream.Position = 0;
                byte[] content = new byte[stream.Length];
                stream.Read(content, 0, content.Length);

                var patches = BuildPatches(Encoding.UTF8.GetString(content), host);

                foreach (var patch in patches)
                {
                    long offset = SearchPattern(content, patch.Key);
                    if (offset >= 0)
                    {
                        fs.Position = offset;
                        bw.Write(patch.Value);
                    }
                }

                fs.Position = 0;
                fs.Flush();
            }

            stream?.Dispose();
        }

        private static BLTEStream OpenFile(string file)
        {
            var entry = CASContainer.InstallHandler.GetEntry(file);
            if (entry == null)
                return null;

            if (CASContainer.EncodingHandler.Data.TryGetValue(entry.MD5, out EncodingEntry enc))
            {
                string key = enc.Keys[0].ToString();
                string outpath = Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(key, "data"));

                if (!File.Exists(outpath))
                {
                    string url = "/data/" + key.Substring(0, 2) + "/" + key.Substring(2, 2) + "/" + key;
                    if (!DataHandler.Download(url, outpath))
                        CASContainer.Logger.LogWarning($"Unable to download executable {file}.");
                }

                return DataHandler.ReadDirect(outpath);
            }

            return null;
        }

        private static Dictionary<byte[], byte[]> BuildPatches(string content, string host)
        {
            // match agent = Regex.Match(content, AGENT_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase); //Get Agent executable
            MatchCollection urls = Regex.Matches(content, URL_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase); //Get versions + cdns urls

            Dictionary<byte[], byte[]> patches = new Dictionary<byte[], byte[]>();
            foreach (Match url in urls)
            {
                string newurl = host + '/' + Path.GetFileName(url.Value);
                if (newurl.Length < url.Value.Length)
                    newurl = (newurl + "?").PadRight(url.Value.Length, 'X'); // pad with fake query string

                if (newurl != url.Value)
                    patches.Add(Encoding.UTF8.GetBytes(url.Value), Encoding.UTF8.GetBytes(newurl)); // versions/cdns patch
            }

            return patches;
        }

        private static unsafe long SearchPattern(this byte[] haystack, byte[] needle)
        {
            fixed (byte* h = haystack) fixed (byte* n = needle)
            {
                for (byte* hNext = h, hEnd = h + haystack.Length + 1 - needle.Length, nEnd = n + needle.Length; hNext < hEnd; hNext++)
                    for (byte* hInc = hNext, nInc = n; *nInc == *hInc; hInc++)
                        if (++nInc == nEnd)
                            return hNext - h;
                return -1;
            }
        }

    }
}
