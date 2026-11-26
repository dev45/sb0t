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
using iconnect;

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

                app.MapGet("/admin/users", async Task<IResult> () =>
                {
                    var r = await RoomControl.RunAsync(() =>
                    {
                        var list = new List<Dictionary<string, object>>();
                        RoomControl.Users.Ares(u => list.Add(Describe(u, "ares")));
                        RoomControl.Users.Web(u => list.Add(Describe(u, "web")));
                        return list;
                    });

                    return Answer(r);
                });

                app.MapPost("/admin/users/{id}/kick", async Task<IResult> (ushort id, string kind) => await UserAction(id, kind, false));
                app.MapPost("/admin/users/{id}/ban", async Task<IResult> (ushort id, string kind) => await UserAction(id, kind, true));

                app.MapGet("/admin/bans", async Task<IResult> () =>
                {
                    var r = await RoomControl.RunAsync(() =>
                    {
                        var list = new List<Dictionary<string, object>>();
                        RoomControl.Users.Banned(b => list.Add(new Dictionary<string, object>
                        {
                            ["id"] = BanId(b),
                            ["name"] = b.Name,
                            ["version"] = b.Version,
                            ["ip"] = b.ExternalIP == null ? null : b.ExternalIP.ToString(),
                            ["localIp"] = b.LocalIP == null ? null : b.LocalIP.ToString(),
                            ["port"] = b.Port,
                            ["guid"] = b.Guid.ToString()
                        }));
                        return list;
                    });

                    return Answer(r);
                });

                app.MapPost("/admin/bans/unban", async Task<IResult> (HttpRequest req) =>
                {
                    var (id, bad) = await ReadText(req, "id", 1, 300);

                    if (bad != null)
                        return bad;

                    var r = await RoomControl.RunAsync(() =>
                    {
                        var matches = new List<IBan>();
                        RoomControl.Users.Banned(b => { if (BanId(b) == id) matches.Add(b); });

                        // Se desbanea fuera del recorrido, por si la lista se modifica al desbanear.
                        foreach (IBan b in matches)
                            b.Unban();

                        return matches.Count;
                    });

                    if (!r.Completed || r.Error != null)
                        return Answer(r);

                    if (r.Value == 0)
                        return Results.Json(Error("Ban no encontrado"), statusCode: 404);

                    return Results.Json(new Dictionary<string, object> { ["unbanned"] = r.Value });
                });

                app.MapPut("/admin/topic", async Task<IResult> (HttpRequest req) =>
                {
                    var (text, bad) = await ReadText(req, "text", 2, 200);

                    if (bad != null)
                        return bad;

                    var r = await RoomControl.RunAsync(() =>
                    {
                        RoomControl.Room.Topic = text;
                        return RoomControl.Room.Topic;
                    });

                    if (!r.Completed || r.Error != null)
                        return Answer(r);

                    return Results.Json(new Dictionary<string, object> { ["topic"] = r.Value });
                });

                app.MapPost("/admin/announce", async Task<IResult> (HttpRequest req) =>
                {
                    var (text, bad) = await ReadText(req, "text", 1, 400);

                    if (bad != null)
                        return bad;

                    var r = await RoomControl.RunAsync(() =>
                    {
                        int sent = 0;
                        Action<IUser> send = u => { u.Print(text); sent++; };
                        RoomControl.Users.Ares(send);
                        RoomControl.Users.Web(send);
                        return sent;
                    });

                    if (!r.Completed || r.Error != null)
                        return Answer(r);

                    return Results.Json(new Dictionary<string, object> { ["sent"] = r.Value });
                });

                app.MapGet("/admin/stats", async Task<IResult> () =>
                {
                    var r = await RoomControl.RunAsync(() =>
                    {
                        IStats s = RoomControl.Stats;

                        return new Dictionary<string, object>
                        {
                            ["dataReceived"] = s.DataReceived,
                            ["dataSent"] = s.DataSent,
                            ["publicMessages"] = s.PublicMessages,
                            ["privateMessages"] = s.PrivateMessages,
                            ["invalidLoginAttempts"] = s.InvalidLoginAttempts,
                            ["floodCount"] = s.FloodCount,
                            ["rejectionCount"] = s.RejectionCount,
                            ["joinCount"] = s.JoinCount,
                            ["partCount"] = s.PartCount,
                            ["peakUserCount"] = s.PeakUserCount,
                            ["currentUserCount"] = s.CurrentUserCount
                        };
                    });

                    return Answer(r);
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

        private static Dictionary<string, object> Describe(IUser u, string kind)
        {
            return new Dictionary<string, object>
            {
                ["id"] = u.ID,
                ["kind"] = kind,
                ["name"] = u.Name,
                ["ip"] = u.ExternalIP == null ? null : u.ExternalIP.ToString(),
                ["level"] = u.Level.ToString(),
                ["owner"] = u.Owner,
                ["muzzled"] = u.Muzzled,
                ["cloaked"] = u.Cloaked,
                ["quarantined"] = u.Quarantined,
                ["connected"] = u.Connected,
                ["version"] = u.Version,
                ["joinTime"] = u.JoinTime
            };
        }

        private static string BanId(IBan b)
        {
            return b.Guid + "|" + b.ExternalIP + "|" + b.Port;
        }

        private static IResult Answer<T>(RoomControl.Result<T> r)
        {
            if (r.Completed && r.Error == null)
                return Results.Json(r.Value);

            return Results.Json(Error(r.Completed ? "Error interno: " + r.Error : r.Error), statusCode: 503);
        }

        private static async Task<IResult> UserAction(ushort id, string kind, bool ban)
        {
            string k = (kind ?? "").ToLowerInvariant();

            if (k != "" && k != "ares" && k != "web")
                return Results.Json(Error("kind debe ser ares o web"), statusCode: 400);

            var r = await RoomControl.RunAsync(() =>
            {
                IUser found = null;
                Action<IUser> look = u => { if (found == null && u.ID == id) found = u; };

                if (k != "web") RoomControl.Users.Ares(look);
                if (k != "ares") RoomControl.Users.Web(look);

                if (found == null)
                    return "notfound";

                if (found.Owner)
                    return "owner";

                string name = found.Name;

                if (ban)
                {
                    found.Ban();

                    // No se sabe si Ban() tambien desconecta: si sigue conectado, se desconecta.
                    if (found.Connected)
                        found.Disconnect();
                }
                else
                    found.Disconnect();

                return "ok:" + name;
            });

            if (!r.Completed || r.Error != null)
                return Answer(r);

            if (r.Value == "notfound")
                return Results.Json(Error("Usuario no encontrado"), statusCode: 404);

            if (r.Value == "owner")
                return Results.Json(Error("No se puede expulsar ni banear al dueno de la sala"), statusCode: 400);

            return Results.Json(new Dictionary<string, object> { ["ok"] = true, ["name"] = r.Value.Substring(3) });
        }

        private static async Task<(string, IResult)> ReadText(HttpRequest req, string field, int min, int max)
        {
            if (req.ContentLength == null || req.ContentLength > 4096)
                return (null, Results.Json(Error("Cuerpo ausente o demasiado grande"), statusCode: 413));

            JsonDocument doc;

            try { doc = await JsonDocument.ParseAsync(req.Body); }
            catch (JsonException) { return (null, Results.Json(Error("JSON no valido"), statusCode: 400)); }

            using (doc)
            {
                JsonElement el;

                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty(field, out el)
                    || el.ValueKind != JsonValueKind.String)
                    return (null, Results.Json(Error("Falta el campo de texto '" + field + "'"), statusCode: 400));

                string s = (el.GetString() ?? "").Trim();

                if (s.Length < min || s.Length > max)
                    return (null, Results.Json(Error("'" + field + "' debe tener entre " + min + " y " + max + " caracteres"), statusCode: 400));

                if (s.Any(Char.IsControl))
                    return (null, Results.Json(Error("'" + field + "' no puede tener saltos de linea ni caracteres de control"), statusCode: 400));

                return (s, null);
            }
        }

        private static bool SafeEquals(string a, string b)
        {
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(a)),
                SHA256.HashData(Encoding.UTF8.GetBytes(b)));
        }
    }
}