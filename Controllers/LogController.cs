using Microsoft.AspNetCore.Mvc;
using ModbusTcpDebugHelper.Services;

namespace ModbusTcpDebugHelper.Controllers;

[ApiController]
[Route("api/logs")]
public class LogController : ControllerBase
{
    private readonly LogStore _log;

    public LogController(LogStore log) => _log = log;

    [HttpGet("packets")]
    public IActionResult Packets([FromQuery] int count = 50) => Ok(_log.GetPackets(count));

    [HttpGet("registers")]
    public IActionResult Registers([FromQuery] int count = 50) => Ok(_log.GetRegisters(count));

    [HttpGet("connections")]
    public IActionResult Connections([FromQuery] int count = 20) => Ok(_log.GetConnections(count));

    [HttpDelete("packets")]
    public IActionResult ClearPackets()
    {
        _log.ClearPackets();
        return Ok();
    }
}