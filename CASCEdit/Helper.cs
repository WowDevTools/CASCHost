using System.IO;

namespace CASCEdit
{
    public class Helper
    {
        public static string GetCDNPath(string filename, string type = "", string basepath = "")
        {
            if(basepath == "")
            {
                basepath = "tpr" + Path.DirectorySeparatorChar + "wow" + Path.DirectorySeparatorChar;
            }

            switch (Path.GetFileName(filename))
            {
                case "versions":
                case "cdns":
                    return "wow" + Path.DirectorySeparatorChar + filename;
                case ".build.info":
                    return filename;
                default:
                    return basepath + type + Path.DirectorySeparatorChar + filename[0] + filename[1] + Path.DirectorySeparatorChar + filename[2] + filename[3] + Path.DirectorySeparatorChar + filename;
            }
        }
    }
}
