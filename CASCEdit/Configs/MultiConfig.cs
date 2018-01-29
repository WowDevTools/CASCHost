using CASCEdit.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit.Configs
{
    public class MultiConfig
    {
        private readonly string BasePath;
        private readonly Dictionary<string, List<string>> Data = new Dictionary<string, List<string>>();
        public List<string> this[string key] => Data.ContainsKey(key) ? Data[key] : new List<string>(new string[2]);

        public MultiConfig(string file)
        {
            BasePath = file;

            using (Stream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BinaryReader br = new BinaryReader(stream))
            {
                string content = Encoding.UTF8.GetString(br.ReadBytes((int)stream.Length));
                string[] lines = content.Split('\n');

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        Data.Add($"_BLANK_{Data.Count}", new List<string>() { line });
                        continue;
                    }

                    string[] tokens = line.Split('=');
                    var values = tokens[1].Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    Data.Add(tokens[0].Trim(), values.ToList());
                }
            }
        }


        public void Set(string key, string value, int index = 0)
        {
            if (Data.ContainsKey(key) && index + 1 <= Data[key].Count)
                Data[key][index] = value;
        }

        public bool Remove(string key) => Data.Remove(key);

        public MD5Hash GetKey(string key)
        {
            key = key.ToLowerInvariant();
            int index = (key == "encoding" ? 1 : 0);

            if (Data.ContainsKey(key))
                return new MD5Hash(this[key][index].ToByteArray());

            return null;
        }

        public string Write()
        {
            using (var md5 = MD5.Create())
            using (var stream = new MemoryStream())
            using (TextWriter writer = new StreamWriter(stream))
            {
                writer.NewLine = "\n";

                for (int i = 0; i < Data.Count; i++)
                {
                    var data = Data.ElementAt(i);

                    if (data.Key.StartsWith("_BLANK_"))
                        writer.Write(string.Join(" ", data.Value));
                    else
                        writer.Write($"{data.Key} = {string.Join(" ", data.Value)}"); //Write key value pair

                    if (i != Data.Count - 1)
                        writer.WriteLine();
                }

                writer.Flush();

                //Save file to disk
                string hash = md5.ComputeHash(stream.ToArray()).ToMD5String();
                var path = Path.Combine(CASCContainer.Settings.OutputPath, hash);

                File.Delete(Path.Combine(CASCContainer.Settings.OutputPath, Path.GetFileName(BasePath))); //Remove old

                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) //Save new
                {
                    stream.Position = 0;
                    stream.CopyTo(fs);
                    fs.Flush();
                }

                return hash;
            }
        }
    }
}
