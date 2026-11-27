using System.Net.Http.Headers;
using System.Text;

namespace manager;

/// <summary>Modo local: el gestor habla con una sala que ya corre en este equipo (sin Docker).</summary>
public sealed class LocalRuntime : IRoomRuntime
{
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string url = (Environment.GetEnvironmentVariable("LOCAL_ROOM_ADMIN_URL") ?? "http://127.0.0.1:8081").TrimEnd('/');
    private readonly string token = Environment.GetEnvironmentVariable("LOCAL_ROOM_ADMIN_TOKEN") ?? "";
    private readonly string? dataDir = Environment.GetEnvironmentVariable("LOCAL_ROOM_DATA");

    public LocalRuntime()
    {
        if (token.Length < 24)
            throw new InvalidOperationException("Define LOCAL_ROOM_ADMIN_TOKEN (el mismo ROOM_ADMIN_TOKEN de la sala, 24+ caracteres).");
    }

    private HttpRequestMessage Build(string method, string path, string? body)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url + "/admin/" + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body != null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        return req;
    }

    public async Task<string> StatusAsync(Room room)
    {
        try
        {
            using var resp = await http.SendAsync(Build("GET", "status", null));
            return resp.IsSuccessStatusCode ? "running" : "error " + (int)resp.StatusCode;
        }
        catch (Exception)
        {
            return "no responde";
        }
    }

    public Task StartAsync(Room room) =>
        throw new NotSupportedException("En modo local la sala se inicia y se detiene a mano.");

    public Task StopAsync(Room room) =>
        throw new NotSupportedException("En modo local la sala se inicia y se detiene a mano.");

    public Task RemoveAsync(Room room, bool purgeData) => Task.CompletedTask;

    public async Task<string> LogsAsync(Room room, int tail)
    {
        if (string.IsNullOrEmpty(dataDir))
            return "(define LOCAL_ROOM_DATA para ver serverlog.txt)";

        string path = Path.Combine(dataDir, "serverlog.txt");

        if (!File.Exists(path))
            return "(no existe " + path + ")";

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        var lines = new Queue<string>();
        string? line;

        while ((line = await sr.ReadLineAsync()) != null)
        {
            lines.Enqueue(line);

            if (lines.Count > tail)
                lines.Dequeue();
        }

        return string.Join("\n", lines);
    }

    public async Task<(int Status, string Body)> AdminAsync(Room room, string method, string path, string? body)
    {
        try
        {
            using var resp = await http.SendAsync(Build(method, path, body));
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
        catch (Exception e)
        {
            return (502, "{\"error\":\"No se pudo hablar con la sala local: " + e.Message.Replace("\"", "'") + "\"}");
        }
    }
}