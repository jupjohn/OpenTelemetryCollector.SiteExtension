using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace OpenTelemetryCollector.SiteExtension.Host;

public partial class CollectorMonitor(
    ILogger<CollectorMonitor> logger,
    IOptions<CollectorOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    private Process? _collectorProcess;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            throw new InvalidOperationException("Non-Windows hosts are not yet supported");
        }

        var dllDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        var binaryLocation = new FileInfo(Path.Combine(dllDir, "otelcol.exe"));

        var isBinaryInstalled = await TryDownloadBinaryIfNotExistsAsync(binaryLocation, stoppingToken);
        if (isBinaryInstalled is false)
        {
            throw new Exception("Oh shit, something blew up!");
        }

        await RunCollectorToCompletion(binaryLocation, stoppingToken);
        await ShutdownCollector();
    }

    private async Task RunCollectorToCompletion(FileSystemInfo collectorBinary, CancellationToken stoppingToken)
    {
        var arguments = BuildConfigArguments(options.Value);
        _collectorProcess = Process.Start(
            new ProcessStartInfo(collectorBinary.FullName, arguments)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = false
            });

        if (_collectorProcess is null)
        {
            throw new NotImplementedException("TODO: Handle failure to create otelcol process");
        }

        // Maybe this should be a field of Timer and this class becomes a IHostedService with distinct Start/Stop
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);
        try
        {
            do
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                // TODO: stream collector stderr to event log

                // TODO: ensure running & healthy!
                // Check process hasn't exited
                // Call OTEL collector health endpoint (I think it has one???)
                if (_collectorProcess.HasExited)
                {
                    var errorOutput = await _collectorProcess.StandardError.ReadToEndAsync(stoppingToken);
                    logger.LogError("Collector exited with errors: {CollectorOutput}", errorOutput);
                    // TODO: implement restarts etc.
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // ignored, the app is shutting down
        }
    }

    private static string BuildConfigArguments(CollectorOptions optionsValue)
    {
        // TODO: would be nice to validate that config strings aren't going to escape into a shell, because they can...
        // All it takes is one semicolon
        var arguments = string.Empty;
        if (optionsValue.Configuration is not null)
        {
            arguments += "--config=" + optionsValue.Configuration;
        }

        foreach (var configuration in optionsValue.Configurations)
        {
            arguments += " --config=" + configuration;
        }

        return arguments;
    }

    private async Task ShutdownCollector()
    {
        if (_collectorProcess is null || _collectorProcess.HasExited)
        {
            logger.LogDebug("Collector process has already shutdown");
            return;
        }

        // Attempt to tell the collector to gracefully stop, otherwise we'll nuke it
        await _collectorProcess.StandardInput.WriteLineAsync("\x3");

        // TODO: check if this is enough time, and if we're shutting down the extension before the app itself (ouch!)
        logger.LogInformation("Waiting for Open Telemetry collector process {ProcessId} to shutdown", _collectorProcess.Id);
        _collectorProcess.WaitForExit(TimeSpan.FromSeconds(6));

        if (_collectorProcess.HasExited is false)
        {
            logger.LogInformation(
                "Open Telemetry collector did not shut down in time, killing process {ProcessId}",
                _collectorProcess.Id);

            _collectorProcess.Kill();
            return;
        }

        logger.LogInformation("Collector process has shutdown");
    }
}
