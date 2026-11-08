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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Jurassic;
using Jurassic.Library;

namespace scripting.Statics
{
    [JSObject(Name = "Registry")]
    class JSRegistry : ObjectInstance
    {
        private static readonly object sync = new object();
        private static Dictionary<string, Dictionary<string, string>> store;

        private static string FilePath
        {
            get { return Path.Combine(Server.DataPath, "registry.json"); }
        }

        public JSRegistry(ScriptEngine engine)
            : base(engine)
        {
            this.PopulateFunctions();

            DefineProperty(Engine.Symbol.ToStringTag, new PropertyDescriptor("Registry", PropertyAttributes.Sealed), true);
        }

        // ---- almacenamiento (siempre llamar dentro de lock(sync)) ----

        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            if (store != null)
                return store;

            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(FilePath))
                {
                    var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(FilePath));

                    if (raw != null)
                        foreach (var script in raw)
                            result[script.Key] = new Dictionary<string, string>(
                                script.Value ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Archivo corrupto: se conserva una copia para no perder datos al sobrescribir.
                try { File.Copy(FilePath, FilePath + ".corrupt", true); } catch { }
            }

            store = result;
            return store;
        }

        private static bool Save()
        {
            try
            {
                Directory.CreateDirectory(Server.DataPath);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, true); // escritura atómica
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ScriptName(ScriptEngine eng)
        {
            return (eng.UserData as string) ?? String.Empty;
        }

        // ---- API para los scripts ----

        [JSFunction(Name = "exists", Flags = JSFunctionFlags.HasEngineParameter, IsWritable = false, IsEnumerable = true)]
        public static bool Exists(ScriptEngine eng, String key)
        {
            lock (sync)
            {
                Dictionary<string, string> values;
                return Load().TryGetValue(ScriptName(eng), out values) && values.ContainsKey(key);
            }
        }

        [JSFunction(Name = "getValue", Flags = JSFunctionFlags.HasEngineParameter, IsWritable = false, IsEnumerable = true)]
        public static String GetValue(ScriptEngine eng, String key)
        {
            lock (sync)
            {
                Dictionary<string, string> values;
                string value;

                if (Load().TryGetValue(ScriptName(eng), out values) && values.TryGetValue(key, out value))
                    return value;

                return null;
            }
        }

        [JSFunction(Name = "getKeys", Flags = JSFunctionFlags.HasEngineParameter, IsWritable = false, IsEnumerable = true)]
        public static Objects.JSRegistryKeyCollection GeyKeys(ScriptEngine eng)
        {
            string[] keys = new string[0];

            lock (sync)
            {
                Dictionary<string, string> values;

                if (Load().TryGetValue(ScriptName(eng), out values))
                {
                    keys = new string[values.Count];
                    values.Keys.CopyTo(keys, 0);
                }
            }

            return new Objects.JSRegistryKeyCollection(eng.Object.InstancePrototype, keys, eng.UserData as string);
        }

        [JSFunction(Name = "setValue", Flags = JSFunctionFlags.HasEngineParameter, IsWritable = false, IsEnumerable = true)]
        public static bool SetValue(ScriptEngine eng, String key, object value)
        {
            string text;

            if (value is double)
                text = ((double)value).ToString(CultureInfo.InvariantCulture);
            else if (value is int)
                text = ((int)value).ToString(CultureInfo.InvariantCulture);
            else if (value is String || value is ConcatenatedString)
                text = value.ToString();
            else
                return false;

            lock (sync)
            {
                var all = Load();
                string script = ScriptName(eng);
                Dictionary<string, string> values;

                if (!all.TryGetValue(script, out values))
                {
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    all[script] = values;
                }

                values[key] = text;
                return Save();
            }
        }

        [JSFunction(Name = "deleteValue", Flags = JSFunctionFlags.HasEngineParameter, IsWritable = false, IsEnumerable = true)]
        public static bool DeleteValue(ScriptEngine eng, String key)
        {
            lock (sync)
            {
                Dictionary<string, string> values;

                if (!Load().TryGetValue(ScriptName(eng), out values) || !values.Remove(key))
                    return false;

                return Save();
            }
        }

        [JSFunction(Name = "clear", Flags = JSFunctionFlags.HasEngineParameter, IsWritable = false, IsEnumerable = true)]
        public static bool Clear(ScriptEngine eng)
        {
            lock (sync)
            {
                if (!Load().Remove(ScriptName(eng)))
                    return false;

                return Save();
            }
        }
    }
}