using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MyMcp.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Information;
});

builder.Services.AddSingleton(CreateWorkspaceOptions(builder.Configuration));
builder.Services.AddSingleton<WorkspaceService>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

static WorkspaceOptions CreateWorkspaceOptions(Microsoft.Extensions.Configuration.ConfigurationManager configuration)
{
    var root = configuration["root"]
        ?? Environment.GetEnvironmentVariable("MYMCP_ROOT")
        ?? Directory.GetCurrentDirectory();

    return new WorkspaceOptions(Path.GetFullPath(root));
}
