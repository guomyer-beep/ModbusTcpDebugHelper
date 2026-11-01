using Microsoft.Data.Sqlite;
using ModbusTcpDebugHelper.Models;

namespace ModbusTcpDebugHelper.Services;

/// <summary>
/// SQLite 日志存储。当前使用内存数据库（Data Source=:memory:）。
/// 单例 + 常开连接：SQLite 内存库每个连接一个独立数据库，连接关闭数据即丢，
/// 因此必须在构造时 Open 并持有到进程结束，才能跨 HTTP 请求共享数据。
/// 切换文件持久化：把 appsettings.json 的 LogStore:ConnectionString 改成 "Data Source=modbus.db" 即可。
/// </summary>
public sealed class LogStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _sync = new();

    public LogStore(IConfiguration config)
    {
        string cs = config["LogStore:ConnectionString"] ?? "Data Source=:memory:";
        _conn = new SqliteConnection(cs);
        _conn.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        Execute(@"
CREATE TABLE IF NOT EXISTS packet_log (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ts        TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    direction TEXT    NOT NULL,
    label     TEXT    NOT NULL,
    hex       TEXT    NOT NULL
);
CREATE TABLE IF NOT EXISTS connection_log (
    id     INTEGER PRIMARY KEY AUTOINCREMENT,
    ts     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    host   TEXT    NOT NULL,
    port   INTEGER NOT NULL,
    result TEXT    NOT NULL
);
CREATE TABLE IF NOT EXISTS register_log (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ts        TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    direction TEXT    NOT NULL,
    address   INTEGER NOT NULL,
    value     INTEGER,
    quantity  INTEGER DEFAULT 1
);");
    }

    public void LogPacket(string direction, string label, string hex)
    {
        lock (_sync)
            Execute("INSERT INTO packet_log(direction,label,hex) VALUES(@d,@l,@h)",
                ("@d", (object)direction), ("@l", (object)label), ("@h", (object)hex));
    }

    public void LogConnection(string host, int port, string result)
    {
        lock (_sync)
            Execute("INSERT INTO connection_log(host,port,result) VALUES(@h,@p,@r)",
                ("@h", (object)host), ("@p", (object)port), ("@r", (object)result));
    }

    public void LogRegister(string direction, ushort address, int? value, int quantity)
    {
        lock (_sync)
            Execute("INSERT INTO register_log(direction,address,value,quantity) VALUES(@d,@a,@v,@q)",
                ("@d", (object)direction), ("@a", (object)(int)address),
                ("@v", (object?)value ?? DBNull.Value), ("@q", (object)quantity));
    }

    public List<PacketLogEntry> GetPackets(int count)
    {
        lock (_sync)
        {
            var list = new List<PacketLogEntry>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id,ts,direction,label,hex FROM packet_log ORDER BY id DESC LIMIT @c";
            cmd.Parameters.AddWithValue("@c", count);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new PacketLogEntry
                {
                    Id = r.GetInt64(0), Ts = r.GetString(1), Direction = r.GetString(2),
                    Label = r.GetString(3), Hex = r.GetString(4)
                });
            return list;
        }
    }

    public List<RegisterLogEntry> GetRegisters(int count)
    {
        lock (_sync)
        {
            var list = new List<RegisterLogEntry>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id,ts,direction,address,value,quantity FROM register_log ORDER BY id DESC LIMIT @c";
            cmd.Parameters.AddWithValue("@c", count);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new RegisterLogEntry
                {
                    Id = r.GetInt64(0), Ts = r.GetString(1), Direction = r.GetString(2),
                    Address = r.GetInt32(3),
                    Value = r.IsDBNull(4) ? null : r.GetInt32(4),
                    Quantity = r.GetInt32(5)
                });
            return list;
        }
    }

    public List<ConnectionLogEntry> GetConnections(int count)
    {
        lock (_sync)
        {
            var list = new List<ConnectionLogEntry>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id,ts,host,port,result FROM connection_log ORDER BY id DESC LIMIT @c";
            cmd.Parameters.AddWithValue("@c", count);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ConnectionLogEntry
                {
                    Id = r.GetInt64(0), Ts = r.GetString(1), Host = r.GetString(2),
                    Port = r.GetInt32(3), Result = r.GetString(4)
                });
            return list;
        }
    }

    public void ClearPackets()
    {
        lock (_sync) Execute("DELETE FROM packet_log");
    }

    private void Execute(string sql, params (string, object?)[] ps)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}