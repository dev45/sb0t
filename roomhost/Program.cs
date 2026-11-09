using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using core;

namespace roomhost
{
    internal static class Program
    {
        private static readonly object logLock = new object();

        private static int Main(string[] args)
        {
            // Suscribirse ANTES de crear el servidor: ServerCore.Log() dispara el evento sin comprobar si hay suscriptores.
            if (args.Length > 0 && args[0] == "--selftest")
                return SelfTest.Run() ? 0 : 1;

            ServerCore.LogUpdate += OnLog;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write("UNHANDLED EXCEPTION", e.ExceptionObject as Exception);

            SeedBasics();
            SeedDefaults();

            ServerCore server = new ServerCore();

            SeedCommandLevels(server);
            SeedOwner();
            SetupExtensions();

            if (!server.Open())
            {
                Console.Error.WriteLine("No se pudo iniciar la sala. Revisa serverlog.txt.");
                return 1;
            }

            ManualResetEventSlim stop = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };

            // SIGTERM es lo que envía "docker stop"
            using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); }))
                stop.Wait();

            Write("Cerrando la sala...", null);
            server.Close();
            return 0;
        }

        /// <summary>Ajustes de primera vez. Faltan por revisar los valores por defecto de los interruptores (bool).</summary>
        private static void SeedBasics()
        {
            Apply("port", (ushort)5009, "ROOM_PORT");
            Apply("name", "sb0t room", "ROOM_NAME");
            Apply("bot", "sb0t", "ROOM_BOT");
            Apply("topic", "welcome to my room", "ROOM_TOPIC");
            Apply("commands", true, "ROOM_COMMANDS");

            if (Settings.Get<byte[]>("udp_address") == null)
                Settings.Set("udp_address", IPAddress.Any.GetAddressBytes());

            if (Settings.Get<byte[]>("guid") == null)
                Settings.Set("guid", Guid.NewGuid().ToByteArray());
        }

        private static void SeedCommandLevels(ServerCore server)
        {
            var defaults = server.DefaultCommandLevels;

            if (defaults == null)
                return;

            foreach (var c in defaults)
                if (!Settings.Has(c.Name, "commands"))
                    Settings.Set(c.Name, (byte)c.Level, "commands");
        }

        /// <summary>Valores que la ventana original sembraba al abrirse.</summary>
        private static void SeedDefaults()
        {
            if (Settings.Get<int>("auto_ban_clear_interval") == 0)
                Settings.Set("auto_ban_clear_interval", 1);

            if (Settings.Get<int>("age_restrict_value") == 0)
                Settings.Set("age_restrict_value", 18);

            if (Settings.Get<byte>("language") == 0)
                Settings.Set("language", (byte)10);

            string web = Environment.GetEnvironmentVariable("ROOM_WEB_URL");

            if (!String.IsNullOrEmpty(web))
                Settings.Set("url", web, "web");
            else if (String.IsNullOrEmpty(Settings.Get<String>("url", "web")))
                Settings.Set("url", "http://chatrooms.marsproject.net/ibot.aspx", "web");
        }

        /// <summary>Inicializa el gestor de extensiones (la ventana lo hacía antes de arrancar) y carga las pedidas en ROOM_EXTENSIONS.</summary>
        private static void SetupExtensions()
        {
            core.Extensions.ExtensionManager.Setup();

            string list = Environment.GetEnvironmentVariable("ROOM_EXTENSIONS");

            if (String.IsNullOrWhiteSpace(list))
                return;

            foreach (string name in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = name.Trim();

                if (core.Extensions.ExtensionManager.LoadPlugin(n) == null)
                    Write("No se pudo cargar la extensión: " + n, null);
                else
                    Write("Extensión cargada: " + n, null);
            }
        }

        private static void SeedOwner()
        {
            string owner = Environment.GetEnvironmentVariable("ROOM_OWNER");

            if (!String.IsNullOrEmpty(owner))
            {
                Settings.Set("owner", owner);
                return;
            }

            if (!String.IsNullOrEmpty(Settings.Get<String>("owner")))
                return;

            const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            char[] chars = new char[16];

            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

            owner = new string(chars);
            Settings.Set("owner", owner);

            // Solo a la consola, nunca al archivo de log.
            Console.WriteLine("Contraseña de dueño generada (guárdala, no se vuelve a mostrar): " + owner);
        }

        // Una variable de entorno siempre manda; si no hay, solo se escribe el valor por defecto la primera vez.
        private static void Apply(string key, object fallback, string envName)
        {
            string env = Environment.GetEnvironmentVariable(envName);

            if (!String.IsNullOrEmpty(env))
            {
                object value;

                if (fallback is ushort) value = ushort.Parse(env);
                else if (fallback is bool) value = Boolean.Parse(env);
                else value = env;

                Settings.Set(key, value);
            }
            else if (!Settings.Has(key))
                Settings.Set(key, fallback);
        }

        private static void OnLog(object sender, ServerLogEventArgs e)
        {
            Write(e.Message, e.Error);
        }

        private static void Write(string message, Exception error)
        {
            string line = DateTime.UtcNow.ToString("u") + " " + (message ?? String.Empty);

            if (error != null)
                line += Environment.NewLine + error;

            lock (logLock)
            {
                Console.WriteLine(line);

                try { File.AppendAllText(AppPaths.Combine("serverlog.txt"), line + Environment.NewLine); }
                catch { }
            }
        }
    }
}