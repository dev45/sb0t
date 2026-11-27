using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using manager;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

string adminPassword = Environment.GetEnvironmentVariable("MANAGER_ADMIN_PASSWORD") ?? "";

if (adminPassword.Length < 8)
{
    Console.Error.WriteLine("Define MANAGER_ADMIN_PASSWORD con al menos 8 caracteres.");
    return 1;
}

string dataDir = Environment.GetEnvironmentVariable("MANAGER_DATA") ?? Path.Combine(Directory.GetCurrentDirectory(), "managerdata");
string image = Environment.GetEnvironmentVariable("ROOM_IMAGE") ?? "ghcr.io/dev45/areschat-room:latest";
int portMin = int.Parse(Environment.GetEnvironmentVariable("MANAGER_PORT_MIN") ?? "5009");
int portMax = int.Parse(Environment.GetEnvironmentVariable("MANAGER_PORT_MAX") ?? "5100");
bool fake = string.Equals(Environment.GetEnvironmentVariable("MANAGER_RUNTIME"), "fake", StringComparison.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
builder.Services.AddSingleton(new RoomStore(Path.Combine(dataDir, "manager.db")));

if (fake)
    builder.Services.AddSingleton<IRoomRuntime, FakeRuntime>();
else
    builder.Services.AddSingleton<IRoomRuntime>(new DockerRuntime(image, Environment.GetEnvironmentVariable("DOCKER_HOST")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("login", l =>
    {
        l.PermitLimit = 5;
        l.Window = TimeSpan.FromMinutes(1);
        l.QueueLimit = 0;
    });
});

var app = builder.Build();

var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

static bool SafeEquals(string a, string b) =>
    CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(a)),
        SHA256.HashData(Encoding.UTF8.GetBytes(b)));

// Lista cerrada de rutas de la API de la sala que el gestor deja pasar.
var adminRoutes = new (string Method, Regex Path)[]
{
    ("GET", new Regex(@"^(status|settings|users|bans|stats)$", RegexOptions.CultureInvariant)),
    ("PUT", new Regex(@"^(settings|topic)$", RegexOptions.CultureInvariant)),
    ("POST", new Regex(@"^(announce|bans/unban|users/[0-9]{1,5}/(kick|ban))$", RegexOptions.CultureInvariant))
};

app.MapPost("/api/login", async (HttpContext ctx, LoginDto dto) =>
{
    if (!SafeEquals(dto.Password ?? "", adminPassword))
        return Results.Unauthorized();

    var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "admin") }, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity));
    return Results.Ok();
}).RequireRateLimiting("login");

app.MapPost("/api/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Ok();
});

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/me", () => Results.Ok());

api.MapGet("/rooms", async (RoomStore store, IRoomRuntime rt) =>
{
    var result = new List<RoomInfo>();

    foreach (var r in store.List())
    {
        string status;

        try { status = await rt.StatusAsync(r); }
        catch (Exception e) { status = "error: " + e.Message; }

        result.Add(new RoomInfo(r.Id, r.Name, r.Port, status, r.Created));
    }

    return Results.Ok(result);
});

api.MapPost("/rooms", async (CreateRoomDto dto, RoomStore store, IRoomRuntime rt) =>
{
    string name = (dto.Name ?? "").Trim();
    string owner = dto.OwnerPassword ?? "";

    if (name.Length is < 1 or > 60 || name.Any(char.IsControl))
        return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 60 caracteres." });

    if (owner.Length is < 8 or > 64 || owner.Any(char.IsControl))
        return Results.BadRequest(new { error = "La contraseña de dueño debe tener entre 8 y 64 caracteres." });

    var room = store.Add(name, owner, portMin, portMax);

    if (room is null)
        return Results.Conflict(new { error = "No quedan puertos libres en el rango " + portMin + "-" + portMax + "." });

    string? warning = null;

    try { await rt.StartAsync(room); }
    catch (Exception e) { warning = "Sala creada, pero no se pudo iniciar: " + e.Message; }

    return Results.Ok(new { id = room.Id, port = room.Port, warning });
});

api.MapPost("/rooms/{id}/start", async (string id, RoomStore store, IRoomRuntime rt) =>
{
    var room = store.Get(id);

    if (room is null)
        return Results.NotFound();

    room = store.EnsureToken(room);

    try { await rt.StartAsync(room); }
    catch (Exception e) { return Results.BadRequest(new { error = e.Message }); }

    return Results.Ok();
});

api.MapPost("/rooms/{id}/stop", async (string id, RoomStore store, IRoomRuntime rt) =>
{
    var room = store.Get(id);

    if (room is null)
        return Results.NotFound();

    try { await rt.StopAsync(room); }
    catch (Exception e) { return Results.BadRequest(new { error = e.Message }); }

    return Results.Ok();
});

api.MapDelete("/rooms/{id}", async (string id, bool? purge, RoomStore store, IRoomRuntime rt) =>
{
    var room = store.Get(id);

    if (room is null)
        return Results.NotFound();

    try { await rt.RemoveAsync(room, purge == true); }
    catch (Exception e) { return Results.BadRequest(new { error = e.Message }); }

    store.Delete(id);
    return Results.Ok();
});

api.MapGet("/rooms/{id}/logs", async (string id, int? tail, RoomStore store, IRoomRuntime rt) =>
{
    var room = store.Get(id);

    if (room is null)
        return Results.NotFound();

    try { return Results.Text(await rt.LogsAsync(room, Math.Clamp(tail ?? 200, 1, 2000))); }
    catch (Exception e) { return Results.BadRequest(new { error = e.Message }); }
});

// Panel por sala: reenvia a la API de administracion de la sala, solo las rutas permitidas.
api.MapMethods("/rooms/{id}/admin/{**path}", new[] { "GET", "PUT", "POST" }, async (string id, string path, string? kind, HttpRequest req, RoomStore store, IRoomRuntime rt) =>
{
    var room = store.Get(id);

    if (room is null)
        return Results.NotFound();

    if (!adminRoutes.Any(r => r.Method == req.Method && r.Path.IsMatch(path)))
        return Results.NotFound(new { error = "Ruta no permitida" });

    if (kind is not null && kind != "ares" && kind != "web")
        return Results.BadRequest(new { error = "kind debe ser ares o web" });

    string? body = null;

    if (req.Method != "GET")
    {
        using var reader = new StreamReader(req.Body, Encoding.UTF8);
        var buffer = new char[16385];
        int n = await reader.ReadBlockAsync(buffer, 0, buffer.Length);

        if (n > 16384)
            return Results.Json(new { error = "Cuerpo demasiado grande" }, statusCode: 413);

        body = n > 0 ? new string(buffer, 0, n) : null;
    }

    string target = kind is null ? path : path + "?kind=" + kind;
    var (status, text) = await rt.AdminAsync(room, req.Method, target, body);
    return Results.Text(text, "application/json", Encoding.UTF8, status);
});

app.Run();
return 0;