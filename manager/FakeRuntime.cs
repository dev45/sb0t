using System.Collections.Concurrent;

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
}