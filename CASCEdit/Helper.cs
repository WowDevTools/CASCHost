using System.IO;

namespace CASCEdit
{
    public class Helper
    {
        public static string GetCDNPath(string filename, string type = "", string basepath = "")
        {
            string path = "";

            if (!CASCContainer.Settings.StaticMode)
            {
                return filename;
            }

            if(basepath == "")
            {
                basepath = "tpr" + Path.DirectorySeparatorChar + "wow" + Path.DirectorySeparatorChar;
            }

            switch (Path.GetFileName(filename))
            {
                case "versions":
                case "cdns":
                    path = "wow" + Path.DirectorySeparatorChar + filename;
                    break;
                case ".build.info":
                    return filename;
                default:
                    path =  basepath + type + Path.DirectorySeparatorChar + filename[0] + filename[1] + Path.DirectorySeparatorChar + filename[2] + filename[3] + Path.DirectorySeparatorChar + filename;
                    break;
            }

            Directory.CreateDirectory(Path.Combine(CASCContainer.Settings.OutputPath, Path.GetDirectoryName(path)));
            return path;
        }

        public static string FixOutputPath(string path, string type = "", string basepath = "")
        {
            if (!CASCContainer.Settings.StaticMode)
            {
                return path;
            }

            if (Path.GetDirectoryName(path) == CASCContainer.Settings.OutputPath)
            {
                path = Path.Combine(CASCContainer.Settings.OutputPath, Helper.GetCDNPath(Path.GetFileName(path), type, basepath));
            }

            return path;
        }
    }
}
