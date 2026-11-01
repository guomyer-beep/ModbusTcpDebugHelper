namespace ModbusTcpDebugHelper.Models;

// --- 请求体 ---
public class ConnectRequest   { public string Host { get; set; } = ""; public int Port { get; set; } = 502; }
public class ReadRequest     { public int StartAddress { get; set; } public int Quantity { get; set; } }
public class WriteRequest    { public int Address { get; set; } public int Value { get; set; } }

// --- 响应体 ---
public class DefaultTargetDto { public string Host { get; set; } = ""; public int Port { get; set; } = 502; }

public class RegisterValue { public int Address { get; set; } public ushort Value { get; set; } }

public class StatusDto { public bool Connected { get; set; } public string? Host { get; set; } public int? Port { get; set; } }

public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<RegisterValue>? Registers { get; set; }
    public string? RawRequestHex { get; set; }
    public string? RawResponseHex { get; set; }

    public static OperationResult Ok(string message = "") => new() { Success = true, Message = message };
    public static OperationResult Fail(string message) => new() { Success = false, Message = message };
}

// --- 日志 DTO（时间存 SQLite datetime 字符串，列表按 id 倒序） ---
public class PacketLogEntry
{
    public long Id { get; set; }
    public string Ts { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hex { get; set; } = "";
}

public class RegisterLogEntry
{
    public long Id { get; set; }
    public string Ts { get; set; } = "";
    public string Direction { get; set; } = "";
    public int Address { get; set; }
    public int? Value { get; set; }
    public int Quantity { get; set; }
}

public class ConnectionLogEntry
{
    public long Id { get; set; }
    public string Ts { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Result { get; set; } = "";
}