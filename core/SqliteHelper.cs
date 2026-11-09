using System;
using System.IO;

namespace core
{
    internal static class SqliteHelper
    {
        /// <summary>Asegura la carpeta y crea un archivo vacío si no existe.</summary>
        public static void CreateFile(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));

            if (!String.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(path))
                using (File.Create(path)) { }
        }
    }
}