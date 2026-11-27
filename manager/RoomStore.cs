using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace manager;

public sealed class RoomStore
{
    private const string Columns = "id, name, port, owner, created, admin_token";
    private readonly string connectionString;
    private readonly object sync = new();

    public RoomStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

        using var c = Open();

        using (var create = c.CreateCommand())
        {
            create.CommandText = "create table if not exists rooms (id text primary key, name text not null, port integer not null unique, owner text not null, created integer not null, admin_token text not null default '')";
            create.ExecuteNonQuery();
        }

        // Migrar bases creadas antes de existir el token de administracion.
        bool hasToken = false;

        using (var info = c.CreateCommand())
        {
            info.CommandText = "pragma table_info(rooms)";
            using var r = info.ExecuteReader();

            while (r.Read())
                if (r.GetString(1) == "admin_token")
                    hasToken = true;
        }

        if (!hasToken)
        {
            using var alter = c.CreateCommand();
            alter.CommandText = "alter table rooms add column admin_token text not null default ''";
            alter.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(connectionString);
        c.Open();
        return c;
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static Room Read(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetInt64(4), r.GetString(5));

    public List<Room> List()
    {
        var list = new List<Room>();

        lock (sync)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "select " + Columns + " from rooms order by port";
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
            cmd.CommandText = "select " + Columns + " from rooms where id = $id";
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

            var room = new Room(Guid.NewGuid().ToString("N")[..8], name, port, owner, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), NewToken());

            using var ins = c.CreateCommand();
            ins.CommandText = "insert into rooms (id, name, port, owner, created, admin_token) values ($id, $name, $port, $owner, $created, $token)";
            ins.Parameters.AddWithValue("$id", room.Id);
            ins.Parameters.AddWithValue("$name", room.Name);
            ins.Parameters.AddWithValue("$port", room.Port);
            ins.Parameters.AddWithValue("$owner", room.Owner);
            ins.Parameters.AddWithValue("$created", room.Created);
            ins.Parameters.AddWithValue("$token", room.AdminToken);
            ins.ExecuteNonQuery();
            return room;
        }
    }

    /// <summary>Si la sala no tiene token (creada antes de esta version), genera uno.</summary>
    public Room EnsureToken(Room room)
    {
        if (!string.IsNullOrEmpty(room.AdminToken))
            return room;

        lock (sync)
        {
            var updated = room with { AdminToken = NewToken() };
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "update rooms set admin_token = $token where id = $id";
            cmd.Parameters.AddWithValue("$token", updated.AdminToken);
            cmd.Parameters.AddWithValue("$id", room.Id);
            cmd.ExecuteNonQuery();
            return updated;
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