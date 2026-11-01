using System.Net;
using Microsoft.AspNetCore.Mvc;
using ModbusTcpDebugHelper.Models;
using ModbusTcpDebugHelper.Services;

namespace ModbusTcpDebugHelper.Controllers;

[ApiController]
[Route("api/modbus")]
public class ModbusController : ControllerBase
{
    private readonly ModbusClientService _service;
    private readonly IConfiguration _config;

    public ModbusController(ModbusClientService service, IConfiguration config)
    {
        _service = service;
        _config = config;
    }

    // 返回默认目标（供前端预填 IP/端口）
    [HttpGet("default")]
    public IActionResult DefaultTarget() => Ok(new DefaultTargetDto
    {
        Host = _config["Modbus:DefaultHost"] ?? "127.0.0.1",
        Port = _config.GetValue("Modbus:DefaultPort", 502)
    });

    [HttpPost("connect")]
    public IActionResult Connect([FromBody] ConnectRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Host) || !IPAddress.TryParse(req.Host, out _))
            return BadRequest(OperationResult.Fail("IP 格式非法"));
        if (req.Port is < 1 or > 65535)
            return BadRequest(OperationResult.Fail("端口需在 1~65535 之间"));
        return Ok(_service.Connect(req.Host, req.Port));
    }

    [HttpPost("disconnect")]
    public IActionResult Disconnect() => Ok(_service.Disconnect());

    [HttpGet("status")]
    public IActionResult Status() => Ok(_service.GetStatus());

    [HttpPost("read-holding")]
    public IActionResult ReadHolding([FromBody] ReadRequest req)
    {
        if (req.StartAddress is < 1 or > 65536)
            return BadRequest(OperationResult.Fail("起始地址需在 1~65536 之间"));
        if (req.Quantity is < 1 or > 125)
            return BadRequest(OperationResult.Fail("数量需在 1~125 之间"));
        // 界面 1 起始地址 → 协议 0 起始
        return Ok(_service.ReadHolding((ushort)(req.StartAddress - 1), (ushort)req.Quantity));
    }

    [HttpPost("write-holding")]
    public IActionResult WriteHolding([FromBody] WriteRequest req)
    {
        if (req.Address is < 1 or > 65536)
            return BadRequest(OperationResult.Fail("地址需在 1~65536 之间"));
        if (req.Value is < 0 or > 65535)
            return BadRequest(OperationResult.Fail("数值需在 0~65535 之间"));
        return Ok(_service.WriteHolding((ushort)(req.Address - 1), (ushort)req.Value));
    }
}