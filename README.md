# OpenTelemetryCollector.SiteExtension

An Azure site extension to run an OpenTelemetry collector in a sidecar configuration alongside your web app

## THIS IS A WORK IN PROGRESS

This code is NOT production ready, and I wouldn't trust it in its current state for a few reasons:

- Collector failures probably (untested) don't cause the app to restart, or at least won't cause the collector to restart
- The code is ripped straight from a POC I made when I was sick, so the thinking behind it may not have been the best (I am going to rereview this entire thing shortly)
- Nothing has been optimized - expect it to take up more RAM than necessary
- Logging to the event log wasn't working last time I checked, so if it falls over - good luck!

## Usage

This is a WIP, so here's how I'm currently packaging things up for testing:

1. `dotnet publish`
1. Upload contents of the `<project_dir>/bin/<build_configuration>/net8.0/publish/` to the app's `site/SiteExtensions` directory
1. Add the following required config values to the app's envvar (example config will dump data to disk):
    - `OTELCOL_SITE_EXTENSION:BinaryLocation`: `https://github.com/open-telemetry/opentelemetry-collector-releases/releases/download/v0.105.0/otelcol-contrib_0.105.0_windows_amd64.tar.gz`
    - `OTELCOL_SITE_EXTENSION:Configuration`: `https://raw.githubusercontent.com/jupjohn/OpenTelemetryCollector.SiteExtension/main/test/test-otelcol-config.yaml`
1. Stop & start the webapp (restarts do not reload the extension)

*James is yet to package this up, and submit it to Azure's extension feed*

## Things of note (for when I work on documentation)

### Configuration options

| Environment Variable                        | Default               | Description                                                                                                                                                                                                                                                                                                                                                            |
|---------------------------------------------|-----------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `OTELCOL_SITE_EXTENSION:BinaryLocation`     | *none, required*      | A HTTPS URL to fetch the Open Telemetry collector binary archive from. The archive must be a gzipped tar file, containing an file matching `OTELCOL_SITE_EXTENSION:OTELCOL_SITE_EXTENSION:BinaryLocation`                                                                                                                                                              |
| `OTELCOL_SITE_EXTENSION:BinaryHash`         | *none, not required*  | An (optional) SHA256 hash to validate the archive file against                                                                                                                                                                                                                                                                                                         |
| `OTELCOL_SITE_EXTENSION:Configuration`      | *none*                | A configuration provider for the collector binary; either a HTTPS URL to a YAML config, a raw YAML document (with `::` for path separation) starting with `yaml:`, or an environment variable reference startin with `env:` (basically any syntax supported by the binary's `--config` parameter, see https://opentelemetry.io/docs/collector/configuration/#location) |
| `OTELCOL_SITE_EXTENSION:Configurations:[x]` | *none*                | Same as `OTELCOL_SITE_EXTENSION:Configuration`, but allows for multiple more providers to be added via array syntax                                                                                                                                                                                                                                                    |
| `OTELCOL_SITE_EXTENSION:ArchivedBinaryName` | `otelcol-contrib.exe` | The name of the collector binary file inside of the downloaded archive, to extract to disk                                                                                                                                                                                                                                                                             |

## License

This project is licensed under the BSD 2-Clause "Simplified" License. See [LICENSE](./LICENSE) for more information.
