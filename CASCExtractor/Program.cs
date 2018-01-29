using System;
using CASCEdit;
using System.IO;

namespace CASCExtractor
{
    class Program
    {
#if DEBUG
        static readonly string BASEPATH = @"D:\World of Warcraft";
#else
        static readonly string BASEPATH = AppContext.BaseDirectory;
#endif

        static void Main(string[] args)
        {
            if (!File.Exists(Path.Combine(BASEPATH, ".build.info")))
            {
                Console.WriteLine("Error: Missing .build.info.");
                System.Threading.Thread.Sleep(1500);
                Environment.Exit(0);
            }

            var settings = new CASCSettings() { BasePath = BASEPATH, Basic = true };
            CASCContainer.Open(settings);

            if (!CASCContainer.ExtractSystemFiles(Path.Combine(BASEPATH, "_SystemFiles")))
            {
                Console.WriteLine("Please ensure that you have a fully downloaded client.");
                System.Threading.Thread.Sleep(3000);
            }

            CASCContainer.Close();
        }
    }
}