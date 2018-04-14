using CASCEdit.Helpers;
using CASCEdit.Logging;
using CASCEdit.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit
{
    public class CASSettings
    {
        public bool Basic { get; set; } = false;
        public string BasePath { get; set; }
        public string SystemFilesPath { get; set; }
        public string OutputPath { get; set; }
        public string PatchUrl { get; set; }        
        public string Host { get; set; }
        public ICASCLog Logger { get; set; } = new ConsoleLogger();
        public ICache Cache { get; set; }
		public LocaleFlags Locale { get; set; } = LocaleFlags.enUS;
        public bool StaticMode { get; set; } = false;


        public HashSet<string> DownloadLocations { get; set; }
		public HashSet<string> CDNs { get; set; }

		public void Format()
        {
            if (string.IsNullOrWhiteSpace(SystemFilesPath))
                OutputPath = BasePath;
            else if (!Directory.Exists(OutputPath))
                OutputPath = Path.Combine(BasePath, OutputPath);

            if (string.IsNullOrWhiteSpace(SystemFilesPath))
                SystemFilesPath = BasePath;
            else if (!Directory.Exists(SystemFilesPath))
                SystemFilesPath = Path.Combine(BasePath, SystemFilesPath);
        }
    }
}
