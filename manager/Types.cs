namespace manager;

public sealed record Room(string Id, string Name, int Port, string Owner, long Created);
public sealed record RoomInfo(string Id, string Name, int Port, string Status, long Created);
public sealed record LoginDto(string? Password);
public sealed record CreateRoomDto(string? Name, string? OwnerPassword);

public interface IRoomRuntime
{
    Task<string> StatusAsync(Room room);
    Task StartAsync(Room room);
    Task StopAsync(Room room);
    Task RemoveAsync(Room room, bool purgeData);
    Task<string> LogsAsync(Room room, int tail);
}