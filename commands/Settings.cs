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
using System.IO;
using System.Text.Json;

namespace commands
{
    class Settings
    {
        public static bool DisableAdmins { get; set; }

        public static bool AdminAnnounce
        {
            get { return Get<bool>("adminannounce"); }
            set { Set("adminannounce", value); }
        }

        public static bool Colors
        {
            get { return Get<bool>("colors"); }
            set { Set("colors", value); }
        }

        public static bool Stealth
        {
            get { return Get<bool>("stealth"); }
            set { Set("stealth", value); }
        }

        public static bool Url
        {
            get { return Get<bool>("url"); }
            set { Set("url", value); }
        }

        public static byte MuzzleTimeout
        {
            get { return Get<byte>("mtimeout"); }
            set { Set("mtimeout", value); }
        }

        public static bool Clock
        {
            get { return Get<bool>("clock"); }
            set { Set("clock", value); }
        }

        public static bool AnonMonitoring
        {
            get { return Get<bool>("anonmonitoring"); }
            set { Set("anonmonitoring", value); }
        }

        public static bool ShareFileMonitoring
        {
            get { return Get<bool>("sharemonitoring"); }
            set { Set("sharemonitoring", value); }
        }

        public static bool Filtering
        {
            get { return Get<bool>("filtering"); }
            set { Set("filtering", value); }
        }

        public static bool CapsMonitoring
        {
            get { return Get<bool>("capsmonitoring"); }
            set { Set("capsmonitoring", value); }
        }

        public static bool IdleMonitoring
        {
            get { return Get<bool>("idlemonitoring"); }
            set { Set("idlemonitoring", value); }
        }

        public static bool General
        {
            get { return Get<bool>("general"); }
            set { Set("general", value); }
        }

        public static bool GreetMsg
        {
            get { return Get<bool>("greetmsg"); }
            set { Set("greetmsg", value); }
        }

        public static bool PMGreetMsg
        {
            get { return Get<bool>("pmgreetmsg"); }
            set { Set("pmgreetmsg", value); }
        }

        public static bool RoomInfo
        {
            get { return Get<bool>("roominfo"); }
            set { Set("roominfo", value); }
        }

        public static bool LastSeen
        {
            get { return Get<bool>("lastseen"); }
            set { Set("lastseen", value); }
        }

        public static bool History
        {
            get { return Get<bool>("history"); }
            set { Set("history", value); }
        }

        public static String PMGreetMsgText
        {
            get { return Get<String>("pmgreetmsgtext"); }
            set { Set("pmgreetmsgtext", value); }
        }

        public static String Status
        {
            get { return Get<String>("status"); }
            set { Set("status", value); }
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
        private static Dictionary<string, JsonElement> cache;

        // Carpeta de datos de la sala: variable de entorno ROOM_DATA o ./data
        private static string DataDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("ROOM_DATA");
                return String.IsNullOrEmpty(env) ? Path.Combine(AppContext.BaseDirectory, "data") : env;
            }
        }

        private static string FilePath { get { return Path.Combine(DataDir, "settings.json"); } }

        private static Dictionary<string, JsonElement> Load()
        {
            if (cache != null)
                return cache;

            try
            {
                if (File.Exists(FilePath))
                    cache = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(FilePath));
            }
            catch { }

            if (cache == null)
                cache = new Dictionary<string, JsonElement>();

            return cache;
        }

        public static T Get<T>(String name)
        {
            Type type = typeof(T);

            if (Array.IndexOf(AcceptableTypes, type) < 0)
                throw new Exception("Setting type not supported");

            lock (sync)
            {
                if (Load().TryGetValue(name, out JsonElement element))
                {
                    try { return element.Deserialize<T>(); }
                    catch { }
                }
            }

            // Mismos valores por defecto que antes
            if (type == typeof(String))
                return (T)(object)String.Empty;

            return default(T);
        }

        public static bool Set(String name, object value)
        {
            if (value == null || Array.IndexOf(AcceptableTypes, value.GetType()) < 0)
                return false;

            lock (sync)
            {
                try
                {
                    Dictionary<string, JsonElement> data = Load();
                    data[name] = JsonSerializer.SerializeToElement(value, value.GetType());

                    Directory.CreateDirectory(DataDir);
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(tmp, FilePath, true);   // escritura atómica
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        }
    }

