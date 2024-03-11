using OpenTelemetryCollector.SiteExtension.Host;
using Serilog;
using Serilog.Events;

// TODO: can this become a .NET generic hosting instead of a web host? Maybe IIS will hate that
// TODO: slim/empty builder?
var builder = WebApplication.CreateBuilder(args);

var serilog = new LoggerConfiguration()
#if DEBUG
    .MinimumLevel.Verbose()
    .WriteTo.Console()
#else
    .MinimumLevel.Information()
    // FIXME: I wasn't seeing event logs in Azure, why isn't this working???
    .WriteTo.EventLog("OpenTelemetryCollector.SiteExtension")
    .WriteTo.Console()
    // TODO: don't hardcode this log path (do what the datadog aas extension does with envvars)
    .WriteTo.File(@"C:\home\LogFiles\OpenTelemetryCollector.SiteExtension\host-.log",
        rollingInterval: RollingInterval.Day)
#endif
    .CreateLogger();

builder.Host.UseSerilog(serilog);

builder.Services.AddOptions<CollectorOptions>()
    .BindConfiguration(CollectorOptions.SectionName)
    .ValidateOnStart();

builder.Services.AddHttpClient();
builder.Services.AddHostedService<CollectorMonitor>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.Run();
