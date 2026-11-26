using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using core;

namespace roomhost
{
    /// <summary>API de administración de la sala. Solo arranca si existe ROOM_ADMIN_TOKEN.</summary>
    internal static class AdminApi
    {
        private sealed class Def
        {
            public string Id, Key, Group;
            public Type Type;
            public Def(string key, Type type, string group = null)
            {
                Key = key; Type = type; Group = group;
                Id = group == null ? key : group + "/" + key;
            }
        }

        // Lista cerrada: name, port y owner NO están (los controlan las variables de entorno).
        private static readonly Def[] Defs =
        {
            new Def("topic", typeof(string)), new Def("bot", typeof(string)),
            new Def("logging", typeof(bool)), new Def("can_room_scribble", typeof(bool)),
            new Def("udp", typeof(bool)), new Def("voice", typeof(bool)), new Def("files", typeof(bool)),
            new Def("captcha", typeof(bool)), new Def("captcha_mode", typeof(int)),
            new Def("strict", typeof(bool)), new Def("script_can_level", typeof(bool)),
            new Def("commands", typeof(bool)), new Def("scripting", typeof(bool)),
            new Def("inroom_scripting", typeof(bool)), new Def("inroom_level", typeof(byte)),
            new Def("auto_ban_clear_enabled", typeof(bool)), new Def("auto_ban_clear_interval", typeof(int)),
            new Def("age_restrict", typeof(bool)), new Def("age_restrict_value", typeof(int)),
            new Def("reject_male", typeof(bool)), new Def("reject_female", typeof(bool)),
            new Def("reject_unknown", typeof(bool)), new Def("hide_ips", typeof(bool)),
            new Def("language", typeof(byte)), new Def("local_host", typeof(bool)),
            new Def("roomsearch", typeof(bool)), new Def("fonts_enabled", typeof(bool)),
            new Def("link_mode", typeof(int)), new Def("link_reconnect", typeof(bool)),
            new Def("link_admin", typeof(bool)),
            new Def("enabled", typeof(bool), "web"), new Def("url", typeof(string), "web")
        };

        private sealed class NoLifetime : IHostLifetime
        {
            public Task WaitForStartAsync(CancellationToken token) { return Task.CompletedTask; }
            public Task StopAsync(CancellationToken token) { return Task.CompletedTask; }
        }

        public static WebApplication StartIfConfigured(Action<string> log)
        {
            string token = Environment.GetEnvironmentVariable("ROOM_ADMIN_TOKEN") ?? "";

            if (token.Length == 0)
                return null; // desactivada por defecto

            if (token.Length < 24)
            {
                log("ROOM_ADMIN_TOKEN debe tener al menos 24 caracteres: API de administracion desactivada.");
                return null;
            }

            string url = Environment.GetEnvironmentVariable("ROOM_ADMIN_URL");

            if (String.IsNullOrEmpty(url))
                url = "http://127.0.0.1:8081";

            try
            {
                DateTime started = DateTime.UtcNow;
                var builder = WebApplication.CreateBuilder();
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton<IHostLifetime, NoLifetime>(); // el cierre lo controla Program.cs
                builder.WebHost.UseUrls(url);
                var app = builder.Build();

                app.Use(async (ctx, next) =>
                {
                    string header = ctx.Request.Headers["Authorization"];
                    string given = header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? header.Substring(7).Trim() : "";

                    if (!SafeEquals(given, token))
                    {
                        ctx.Response.StatusCode = 401;
                        return;
                    }

                    await next();
                });

                app.MapGet("/admin/status", () => Results.Json(new Dictionary<string, object>
                {
                    ["version"] = Settings.VERSION,
                    ["name"] = Settings.Name,
                    ["topic"] = Settings.Topic,
                    ["port"] = Settings.Port,
                    ["running"] = Settings.RUNNING,
                    ["uptimeSeconds"] = (long)(DateTime.UtcNow - started).TotalSeconds
                }));

                app.MapGet("/admin/settings", () =>
                {
                    var list = new List<Dictionary<string, object>>();

                    foreach (Def d in Defs)
                        list.Add(new Dictionary<string, object>
                        {
                            ["id"] = d.Id,
                            ["type"] = d.Type == typeof(bool) ? "bool" : d.Type == typeof(string) ? "string" : "number",
                            ["value"] = Read(d)
                        });

                    return Results.Json(list);
                });

                app.MapPut("/admin/settings", async (HttpRequest req) =>
                {
                    if (req.ContentLength == null || req.ContentLength > 16384)
                        return Results.Json(Error("Cuerpo ausente o demasiado grande"), statusCode: 413);

                    JsonDocument doc;

                    try { doc = await JsonDocument.ParseAsync(req.Body); }
                    catch (JsonException) { return Results.Json(Error("JSON no valido"), statusCode: 400); }

                    using (doc)
                    {
                        if (doc.RootElement.ValueKind != JsonValueKind.Object)
                            return Results.Json(Error("Se esperaba un objeto JSON"), statusCode: 400);

                        var defs = new List<Def>();
                        var values = new List<object>();

                        // Primero se valida todo; solo si todo es correcto se guarda.
                        foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                        {
                            Def d = Defs.FirstOrDefault(x => x.Id == p.Name);

                            if (d == null)
                                return Results.Json(Error("Ajuste no permitido: " + p.Name), statusCode: 400);

                            object value; string error;

                            if (!TryParse(d, p.Value, out value, out error))
                                return Results.Json(Error(p.Name + ": " + error), statusCode: 400);

                            defs.Add(d);
                            values.Add(value);
                        }

                        for (int i = 0; i < defs.Count; i++)
                            Settings.Set(defs[i].Key, values[i], Sub(defs[i]));

                        return Results.Json(new Dictionary<string, object> { ["saved"] = defs.Count, ["restartRequired"] = true });
                    }
                });

                app.StartAsync().GetAwaiter().GetResult();
                log("API de administracion escuchando en " + url);
                return app;
            }
            catch (Exception e)
            {
                log("No se pudo iniciar la API de administracion: " + e.Message);
                return null;
            }
        }

        public static void Stop(WebApplication app)
        {
            if (app != null)
                app.StopAsync().GetAwaiter().GetResult();
        }

        private static Dictionary<string, string> Error(string message)
        {
            return new Dictionary<string, string> { ["error"] = message };
        }

        private static string[] Sub(Def d)
        {
            return d.Group == null ? new string[0] : new[] { d.Group };
        }

        private static object Read(Def d)
        {
            if (d.Type == typeof(bool)) return Settings.Get<bool>(d.Key, Sub(d));
            if (d.Type == typeof(int)) return Settings.Get<int>(d.Key, Sub(d));
            if (d.Type == typeof(byte)) return Settings.Get<byte>(d.Key, Sub(d));
            return Settings.Get<string>(d.Key, Sub(d));
        }

        private static bool TryParse(Def d, JsonElement v, out object value, out string error)
        {
            value = null; error = null;

            if (d.Type == typeof(bool))
            {
                if (v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False)
                { error = "se esperaba true o false"; return false; }

                value = v.GetBoolean();
                return true;
            }

            if (d.Type == typeof(int))
            {
                int i = 0;

                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out i) || i < 0 || i > 100000)
                { error = "se esperaba un entero entre 0 y 100000"; return false; }

                value = i;
                return true;
            }

            if (d.Type == typeof(byte))
            {
                byte b = 0;

                if (v.ValueKind != JsonValueKind.Number || !v.TryGetByte(out b))
                { error = "se esperaba un entero entre 0 y 255"; return false; }

                value = b;
                return true;
            }

            if (v.ValueKind != JsonValueKind.String)
            { error = "se esperaba un texto"; return false; }

            string s = v.GetString() ?? "";

            if (s.Length > 500 || s.Any(Char.IsControl))
            { error = "texto demasiado largo o con caracteres de control"; return false; }

            value = s;
            return true;
        }

        private static bool SafeEquals(string a, string b)
        {
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(a)),
                SHA256.HashData(Encoding.UTF8.GetBytes(b)));
        }
    }
}