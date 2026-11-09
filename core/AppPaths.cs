using System;
using System.IO;

namespace core
{
    /// <summary>Todas las rutas de datos de la sala salen de aquí.</summary>
    public static class AppPaths
    {
        /// <summary>Carpeta raíz de la sala: variable ROOM_DATA, o ./data junto al ejecutable.</summary>
        public static string Root { get; private set; }

        static AppPaths()
        {
            string env = Environment.GetEnvironmentVariable("ROOM_DATA");

            Root = Path.GetFullPath(String.IsNullOrWhiteSpace(env)
                ? Path.Combine(AppContext.BaseDirectory, "data")
                : env);

            Directory.CreateDirectory(Root);
        }

        /// <summary>Une partes a la carpeta raíz (no crea nada).</summary>
        public static string Combine(params string[] parts)
        {
            string path = Root;

            foreach (string part in parts)
                path = Path.Combine(path, part);

            return path;
        }
    }
}