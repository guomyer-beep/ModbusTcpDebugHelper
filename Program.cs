using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModbusTcpDebugHelper.Models;
using ModbusTcpDebugHelper.Services;

var builder = WebApplication.CreateBuilder(args);

// 控制器 + JSON 配置：中文不转义成 \uXXXX，便于阅读
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

// 单例：先注册日志，再注册业务（业务依赖日志）
builder.Services.AddSingleton<LogStore>();
builder.Services.AddSingleton<ModbusClientService>();

var app = builder.Build();

// 全局异常中间件兜底：任何未捕获异常 → 500 JSON
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(OperationResult.Fail($"服务器内部错误：{ex.Message}"));
    }
});

// 静态文件（wwwroot）+ 默认打开 index.html
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

var url = builder.Configuration["Web:Url"] ?? "http://localhost:8080";
app.Run(url);