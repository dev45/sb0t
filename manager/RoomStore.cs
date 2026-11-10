using Microsoft.Data.Sqlite;

namespace manager;

public sealed class RoomStore
{
    private readonly string connectionString;
    private readonly object sync = new();

    public RoomStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "create table if not exists rooms (id text primary key, name text not null, port integer not null unique, owner text not null, created integer not null)";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(connectionString);
        c.Open();
        return c;
    }

    private static Room Read(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetInt64(4));

    public List<Room> List()
    {
        var list = new List<Room>();

        lock (sync)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "select id, name, port, owner, created from rooms order by port";
            using var r = cmd.ExecuteReader();

            while (r.Read())
                list.Add(Read(r));
        }

        return list;
    }

    public Room? Get(string id)
    {
        lock (sync)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "select id, name, port, owner, created from rooms where id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? Read(r) : null;
        }
    }

    /// <summary>Crea la sala con el primer puerto libre del rango; null si no queda ninguno.</summary>
    public Room? Add(string name, string owner, int portMin, int portMax)
    {
        lock (sync)
        {
            using var c = Open();
            var used = new HashSet<int>();

            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "select port from rooms";
                using var r = cmd.ExecuteReader();

                while (r.Read())
                    used.Add(r.GetInt32(0));
            }

            int port = 0;

            for (int p = portMin; p <= portMax; p++)
                if (!used.Contains(p))
                {
                    port = p;
                    break;
                }

            if (port == 0)
                return null;

            var room = new Room(Guid.NewGuid().ToString("N")[..8], name, port, owner, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            using var ins = c.CreateCommand();
            ins.CommandText = "insert into rooms (id, name, port, owner, created) values ($id, $name, $port, $owner, $created)";
            ins.Parameters.AddWithValue("$id", room.Id);
            ins.Parameters.AddWithValue("$name", room.Name);
            ins.Parameters.AddWithValue("$port", room.Port);
            ins.Parameters.AddWithValue("$owner", room.Owner);
            ins.Parameters.AddWithValue("$created", room.Created);
            ins.ExecuteNonQuery();
            return room;
        }
    }

    public void Delete(string id)
    {
        lock (sync)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "delete from rooms where id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }
}