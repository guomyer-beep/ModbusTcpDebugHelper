using System.Net.Sockets;
using ModbusTcpDebugHelper.Models;

namespace ModbusTcpDebugHelper.Services;

/// <summary>
/// 手写 Modbus TCP 主站客户端（无需第三方 Modbus 库）。
/// 直接通过 TcpClient/NetworkStream 构建 MBAP 头 + PDU 并收发，
/// 因此发送/接收到的字节就是真实报文，可原样打印（项目核心亮点）。
/// 仅实现保持寄存器读（FC03）与单寄存器写（FC06）。
/// </summary>
public sealed class ModbusClientService : IDisposable
{
    private const byte UnitId = 1;            // 从站单元标识
    private const int ConnectTimeoutMs = 3000; // 连接超时（毫秒）
    private const int TimeoutMs = 3000;        // 读写超时（毫秒）

    private readonly SemaphoreSlim _gate = new(1, 1); // 串行化所有连接/读写操作
    private readonly LogStore _log;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private bool _connected;
    private ushort _transactionId = 1;

    public ModbusClientService(LogStore log) => _log = log;

    public bool IsConnected => _connected && _tcp is { Connected: true };
    public string? Host { get; private set; }
    public int? Port { get; private set; }

    // ---------------- 连接管理 ----------------

    public OperationResult Connect(string host, int port)
    {
        _gate.Wait();
        try
        {
            DisconnectInternal();

            var tcp = new TcpClient();
            try
            {
                // 连接带超时：不可达时快速失败，而非等待操作系统默认 ~21s
                using var cts = new CancellationTokenSource(ConnectTimeoutMs);
                tcp.ConnectAsync(host, port, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                tcp.Dispose();
                _log.LogConnection(host, port, "fail:连接超时");
                return OperationResult.Fail("连接超时，目标不可达");
            }

            var stream = tcp.GetStream();
            stream.ReadTimeout = TimeoutMs;
            stream.WriteTimeout = TimeoutMs;

            _tcp = tcp;
            _stream = stream;
            _connected = true;
            Host = host;
            Port = port;

            _log.LogConnection(host, port, "success");
            return OperationResult.Ok("连接成功");
        }
        catch (SocketException ex)
        {
            _log.LogConnection(host, port, $"fail:{ex.Message}");
            return ex.SocketErrorCode switch
            {
                SocketError.TimedOut => OperationResult.Fail("连接超时，目标不可达"),
                SocketError.ConnectionRefused => OperationResult.Fail("连接被拒绝：请检查 IP/端口或确认从站已启动"),
                _ => OperationResult.Fail($"连接失败：{ex.Message}")
            };
        }
        catch (Exception ex)
        {
            _log.LogConnection(host, port, $"fail:{ex.Message}");
            return OperationResult.Fail($"连接失败：{ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public OperationResult Disconnect()
    {
        _gate.Wait();
        try
        {
            DisconnectInternal();
            return OperationResult.Ok("连接已断开");
        }
        finally
        {
            _gate.Release();
        }
    }

    public StatusDto GetStatus() => new()
    {
        Connected = IsConnected,
        Host = Host,
        Port = Port
    };

    private void DisconnectInternal()
    {
        _stream?.Dispose();
        _stream = null;
        _tcp?.Dispose();
        _tcp = null;
        _connected = false;
        Host = null;
        Port = null;
    }

    // ---------------- 读保持寄存器（功能码 03） ----------------

    public OperationResult ReadHolding(ushort startAddress, ushort quantity)
    {
        _gate.Wait();
        try
        {
            if (!IsConnected || _stream == null)
                return OperationResult.Fail("请先连接设备");

            ushort transId = NextTransactionId();
            byte[] request = BuildRequest(transId, 0x03, U16(startAddress), U16(quantity));
            string reqHex = ToHex(request);

            byte[] response;
            string respHex;
            try
            {
                _stream.Write(request, 0, request.Length);
                _stream.Flush();
                _log.LogPacket("TX", "读保持寄存器", reqHex);

                response = ReadFrame(_stream);
                respHex = ToHex(response);
                _log.LogPacket("RX", "读保持寄存器", respHex);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                _connected = false;
                return OperationResult.Fail("通信中断或连接已关闭，请重新连接");
            }

            try
            {
                ushort[] values = ParseReadResponse(response, quantity);
                _log.LogRegister("read", startAddress, null, quantity);

                var registers = new List<RegisterValue>(values.Length);
                for (int i = 0; i < values.Length; i++)
                    registers.Add(new RegisterValue { Address = startAddress + i + 1, Value = values[i] }); // 展示地址 1 起始

                return new OperationResult
                {
                    Success = true,
                    Message = $"读取成功，共 {values.Length} 个寄存器",
                    Registers = registers,
                    RawRequestHex = reqHex,
                    RawResponseHex = respHex
                };
            }
            catch (ModbusFrameException ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------- 写单寄存器（功能码 06） ----------------

    public OperationResult WriteHolding(ushort address, ushort value)
    {
        _gate.Wait();
        try
        {
            if (!IsConnected || _stream == null)
                return OperationResult.Fail("请先连接设备");

            ushort transId = NextTransactionId();
            byte[] request = BuildRequest(transId, 0x06, U16(address), U16(value));
            string reqHex = ToHex(request);

            byte[] response;
            string respHex;
            try
            {
                _stream.Write(request, 0, request.Length);
                _stream.Flush();
                _log.LogPacket("TX", "写单寄存器", reqHex);

                response = ReadFrame(_stream);
                respHex = ToHex(response);
                _log.LogPacket("RX", "写单寄存器", respHex);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                _connected = false;
                return OperationResult.Fail("通信中断或连接已关闭，请重新连接");
            }

            try
            {
                ParseWriteResponse(response);
                _log.LogRegister("write", address, value, 1);
                return new OperationResult
                {
                    Success = true,
                    Message = "写入成功",
                    RawRequestHex = reqHex,
                    RawResponseHex = respHex
                };
            }
            catch (ModbusFrameException ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------- 帧构建 / 解析 ----------------

    private static byte[] BuildRequest(ushort transId, byte func, byte[] a, byte[] b)
    {
        // MBAP 头(7) + 功能码(1) + 数据(a+b)
        var frame = new byte[7 + 1 + a.Length + b.Length];
        U16(transId).CopyTo(frame, 0);
        frame[2] = 0; frame[3] = 0;                                   // 协议标识 = 0x0000
        U16((ushort)(1 + 1 + a.Length + b.Length)).CopyTo(frame, 4);  // 长度 = 单元标识 + 功能码 + 数据
        frame[6] = UnitId;
        frame[7] = func;
        a.CopyTo(frame, 8);
        b.CopyTo(frame, 8 + a.Length);
        return frame;
    }

    private static byte[] ReadFrame(NetworkStream stream)
    {
        var mbap = new byte[7];
        ReadExact(stream, mbap, 0, mbap.Length);
        int length = (mbap[4] << 8) | mbap[5];   // 单元标识 + PDU 的字节数
        if (length < 2) throw new IOException("响应长度非法");
        int pduLen = length - 1;
        var pdu = new byte[pduLen];
        ReadExact(stream, pdu, 0, pduLen);

        var full = new byte[mbap.Length + pdu.Length];
        mbap.CopyTo(full, 0);
        pdu.CopyTo(full, mbap.Length);
        return full;
    }

    private static void ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buffer, offset + total, count - total);
            if (n == 0) throw new IOException("连接被对端关闭");
            total += n;
        }
    }

    private static ushort[] ParseReadResponse(byte[] frame, ushort expectedQty)
    {
        byte func = frame[7];
        if (func == 0x83) throw new ModbusFrameException(MapErrorCode(frame[8]));
        if (func != 0x03) throw new ModbusFrameException($"从站返回未知功能码 0x{func:X2}");

        byte byteCount = frame[8];
        if (byteCount != expectedQty * 2)
            throw new ModbusFrameException("响应字节数与请求数量不一致");

        var values = new ushort[expectedQty];
        for (int i = 0; i < expectedQty; i++)
            values[i] = (ushort)((frame[9 + i * 2] << 8) | frame[10 + i * 2]);
        return values;
    }

    private static void ParseWriteResponse(byte[] frame)
    {
        byte func = frame[7];
        if (func == 0x86) throw new ModbusFrameException(MapErrorCode(frame[8]));
        if (func != 0x06) throw new ModbusFrameException($"从站返回未知功能码 0x{func:X2}");
    }

    private static string MapErrorCode(byte code) => code switch
    {
        0x01 => "从站异常码 0x01：非法功能",
        0x02 => "从站异常码 0x02：非法数据地址",
        0x03 => "从站异常码 0x03：非法数据值",
        0x04 => "从站异常码 0x04：从站设备故障",
        _ => $"从站异常码 0x{code:X2}"
    };

    private ushort NextTransactionId()
    {
        ushort id = _transactionId++;
        if (_transactionId == 0) _transactionId = 1;   // 跳过 0
        return id;
    }

    private static byte[] U16(ushort v) => new[] { (byte)(v >> 8), (byte)(v & 0xFF) };

    private static string ToHex(byte[] data) => string.Join(" ", data.Select(b => b.ToString("X2")));

    public void Dispose() => DisconnectInternal();
}

/// <summary>从站返回异常响应（功能码 + 0x80）时抛出的异常。</summary>
public sealed class ModbusFrameException : Exception
{
    public ModbusFrameException(string message) : base(message) { }
}