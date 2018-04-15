using System;
using CASCEdit;
using System.IO;

namespace CASCExtractor
{
    class Program
    {
        static readonly string BASEPATH = AppContext.BaseDirectory;

        static void Main(string[] args)
        {
            if (!File.Exists(Path.Combine(BASEPATH, ".build.info")))
            {
                Console.WriteLine("Error: Missing .build.info.");
                System.Threading.Thread.Sleep(1500);
                Environment.Exit(0);
            }

            var settings = new CASSettings() { BasePath = BASEPATH, Basic = true };
            CASContainer.Open(settings);

            if (!CASContainer.ExtractSystemFiles(Path.Combine(BASEPATH, "_SystemFiles")))
            {
                Console.WriteLine("Please ensure that you have a fully downloaded client.");
                System.Threading.Thread.Sleep(3000);
            }

            CASContainer.Close();
        }
    }
}