using System.IO;

namespace CASCEdit
{
    public class Helper
    {
        public static string GetCDNPath(string filename, string type = "", string basepath = "", bool staticoverride = false)
        {
            string path = "";

            if (!(CASContainer.Settings?.StaticMode ?? staticoverride))
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

            Directory.CreateDirectory(Path.Combine(CASContainer.Settings?.OutputPath ?? "Output", Path.GetDirectoryName(path)));
            return path;
        }

        public static string FixOutputPath(string path, string type = "", string basepath = "")
        {
            if (!CASContainer.Settings.StaticMode)
            {
                return path;
            }

            if (Path.GetDirectoryName(path) == CASContainer.Settings.OutputPath)
            {
                path = Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(Path.GetFileName(path), type, basepath));
            }

            return path;
        }
    }
}
