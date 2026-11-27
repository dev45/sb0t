using System.Collections.Concurrent;
using System.Text.Json;

namespace manager;

/// <summary>Simula los contenedores en memoria, para desarrollar la interfaz sin Docker.</summary>
public sealed class FakeRuntime : IRoomRuntime
{
    private readonly ConcurrentDictionary<string, string> status = new();

    public Task<string> StatusAsync(Room room) =>
        Task.FromResult(status.TryGetValue(room.Id, out var s) ? s : "sin crear");

    public Task StartAsync(Room room)
    {
        status[room.Id] = "running";
        return Task.CompletedTask;
    }

    public Task StopAsync(Room room)
    {
        if (status.ContainsKey(room.Id))
            status[room.Id] = "exited";

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Room room, bool purgeData)
    {
        status.TryRemove(room.Id, out _);
        return Task.CompletedTask;
    }

    public Task<string> LogsAsync(Room room, int tail) =>
        Task.FromResult("[modo fake] sala " + room.Name + " en el puerto " + room.Port + "\n");

    public Task<(int Status, string Body)> AdminAsync(Room room, string method, string path, string? body)
    {
        string p = path.Split('?')[0];

        object result = (method, p) switch
        {
            ("GET", "status") => new { version = "sb0t 5.39 (fake)", name = room.Name, topic = "tema de prueba", port = room.Port, running = true, uptimeSeconds = 42 },
            ("GET", "stats") => new { dataReceived = 1234, dataSent = 5678, publicMessages = 3, privateMessages = 1, joinCount = 2, currentUserCount = 1 },
            ("GET", "settings") => new[]
            {
                new { id = "topic", type = "string", value = (object)"tema de prueba" },
                new { id = "captcha", type = "bool", value = (object)false },
                new { id = "age_restrict_value", type = "number", value = (object)18 }
            },
            ("GET", "users") => new[]
            {
                new { id = 0, kind = "ares", name = "Usuario de prueba", ip = "127.0.0.1", level = "Regular", owner = false, version = "fake" }
            },
            ("GET", "bans") => Array.Empty<object>(),
            _ => new { ok = true }
        };

        return Task.FromResult((200, JsonSerializer.Serialize(result)));
    }
}