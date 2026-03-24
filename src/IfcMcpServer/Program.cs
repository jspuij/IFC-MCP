using IfcMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<ModelSession>();
builder.Services.AddSingleton<ElementQueryService>();
builder.Services.AddSingleton<QuantityCalculator>();
builder.Services.AddSingleton<ExcelExporter>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "ifc-mcp-server", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
