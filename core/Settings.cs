/*
    sb0t ares chat server
    Copyright (C) 2017  AresChat

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Diagnostics;
using System.Text.Json;
using System.IO;

namespace core
{
    public class Settings
    {
        public const String RELEASE_URL = "https://github.com/AresChat/sb0t/releases";
        public const String VERSION_CHECK_URL = "https://api.github.com/repos/AresChat/sb0t/releases";
        public const String VERSION_NUMBER = "5.39";

        public const String VERSION = "sb0t " + VERSION_NUMBER;
        public const ushort LINK_PROTO = 500;

        public static bool RUNNING { get; set; }
        public static String WebPath { get; set; }

        public static void Reset()
        {
            externalip = null;
            port = 0;
            name = null;
            language = 0;
            hide_ips = 0;

            WebPath = AppPaths.Combine("Style");

            if (!Directory.Exists(WebPath))
            {
                Directory.CreateDirectory(WebPath);
                WebPath += Path.DirectorySeparatorChar;
            }
            else WebPath += Path.DirectorySeparatorChar;

            DoOnce.Run();
        }

        public static void ScriptCanLevel(bool can)
        {
            Events.ScriptCanLevel(can);
        }

        private static int hide_ips = 0;
        public static bool HideIps
        {
            get
            {
                if (hide_ips == 0)
                    hide_ips = Get<bool>("hide_ips") ? 2 : 1;

                return hide_ips == 2;
            }
        }

        private static IPAddress externalip { get; set; }
        public static IPAddress ExternalIP
        {
            get
            {
                if (externalip != null)
                    return externalip;

                byte[] buffer = Get<byte[]>("ip");

                if (buffer != null)
                    externalip = new IPAddress(buffer);
                else
                    externalip = IPAddress.Loopback;

                return externalip;
            }
            set
            {
                externalip = value;
                Set("ip", externalip.GetAddressBytes());
            }
        }

        private static ushort port { get; set; }
        public static ushort Port
        {
            get
            {
                if (port == 0)
                    port = Get<ushort>("port");

                return port;
            }
        }

        private static String name { get; set; }
        public static String Name
        {
            get
            {
                if (name == null)
                    name = Get<String>("name");

                return name;
            }
        }

        private static byte language { get; set; }
        public static byte Language
        {
            get
            {
                if (language == 0)
                    language = Get<byte>("language");

                return language;
            }
        }

        private static String topic { get; set; }
        public static String Topic
        {
            get
            {
                if (topic == null)
                    topic = Get<String>("topic");

                if (topic.Length < 2)
                {
                    topic = "welcome to my room";
                    Set("topic", topic);
                }

                return topic;
            }
            set
            {
                topic = value;
                Set("topic", topic);
            }
        }

        private static Type[] AcceptableTypes = 
        {
            typeof(byte),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(String),
            typeof(byte[]),
            typeof(bool)
        };

        private static readonly object sync = new object();
        private static Dictionary<string, Dictionary<string, JsonElement>> store;

        // Se llama config.json (y no settings.json) para no chocar con el archivo del proyecto commands.
        private static string ConfigFile { get { return AppPaths.Combine("config.json"); } }

        private static string GroupName(String[] subkeys)
        {
            return String.Join("/", subkeys);
        }

        private static Dictionary<string, Dictionary<string, JsonElement>> Load()
        {
            if (store != null)
                return store;

            var result = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(ConfigFile))
                {
                    var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(ConfigFile));

                    if (raw != null)
                        foreach (var group in raw)
                            result[group.Key] = new Dictionary<string, JsonElement>(
                                group.Value ?? new Dictionary<string, JsonElement>(), StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Archivo corrupto: se guarda una copia antes de sobrescribir.
                try { File.Copy(ConfigFile, ConfigFile + ".corrupt", true); } catch { }
            }

            store = result;
            return store;
        }

        private static bool Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                string tmp = ConfigFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, ConfigFile, true); // escritura atómica
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool Has(String name, params String[] subkeys)
        {
            lock (sync)
            {
                Dictionary<string, JsonElement> group;
                return Load().TryGetValue(GroupName(subkeys), out group) && group.ContainsKey(name);
            }
        }

        public static T Get<T>(String name, params String[] subkeys)
        {
            Type type = typeof(T);

            if (Array.IndexOf(AcceptableTypes, type) < 0)
                throw new Exception("Setting type not supported");

            lock (sync)
            {
                Dictionary<string, JsonElement> group;
                JsonElement element;

                if (Load().TryGetValue(GroupName(subkeys), out group) && group.TryGetValue(name, out element))
                {
                    try { return element.Deserialize<T>(); }
                    catch { }
                }
            }

            // Mismos valores por defecto que con el registro: 0, false, null y "".
            if (type == typeof(String))
                return (T)(object)String.Empty;

            return default(T);
        }

        public static bool Set(String name, object value, params String[] subkeys)
        {
            if (value == null || Array.IndexOf(AcceptableTypes, value.GetType()) < 0)
                return false;

            lock (sync)
            {
                var all = Load();
                string key = GroupName(subkeys);
                Dictionary<string, JsonElement> group;

                if (!all.TryGetValue(key, out group))
                {
                    group = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    all[key] = group;
                }

                group[name] = JsonSerializer.SerializeToElement(value, value.GetType());
                return Save();
            }
        }
        public static IPAddress LocalIP
        {
            get
            {
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip;

                return IPAddress.Loopback;
            }
        }
    }
}
