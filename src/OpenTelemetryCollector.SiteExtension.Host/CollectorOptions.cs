using System.ComponentModel.DataAnnotations;

namespace OpenTelemetryCollector.SiteExtension.Host;

public class CollectorOptions : IValidatableObject
{
    public const string SectionName = "OTELCOL_SITE_EXTENSION";
    private const string OtelColConfigHelpUrl = "https://opentelemetry.io/docs/collector/configuration/#location";
    private const string InvalidConfigurationMessage = $"Must be a yaml literal, environment variable reference, or HTTPS url. See {OtelColConfigHelpUrl}";

    [Required]
    public Uri BinaryLocation { get; init; } = null!;
    public string? BinaryHash { get; init; } = null;

    public string? Configuration { get; init; } = null!;
    public string[] Configurations { get; init; } = [];

    public string? ArchivedBinaryName { get; init; } = "otelcol-contrib.exe";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BinaryLocation.IsAbsoluteUri is false || BinaryLocation.Scheme.StartsWith("http") is false)
        {
            yield return new ValidationResult("Must be a HTTP(S) url", [nameof(BinaryLocation)]);
        }

        if (BinaryHash is not null && BinaryHash.Length != 64)
        {
            yield return new ValidationResult("SHA256 hash is not of expected length", [nameof(BinaryHash)]);
        }

        if (Configuration is not null && IsConfigurationValueValid(Configuration) is false)
        {
            yield return new ValidationResult(InvalidConfigurationMessage, [nameof(Configuration)]);
        }

        for (var index = 0; index < Configurations.Length; index++)
        {
            var configuration = Configurations[index];
            if (IsConfigurationValueValid(configuration) is false)
            {
                yield return new ValidationResult(InvalidConfigurationMessage, [$"{nameof(Configurations)}:{index}"]);
            }
        }
    }

    private static bool IsConfigurationValueValid(string value)
    {
        // Might as well catch value errors at the extension level, before they make it to the collector's args
        var format = value.Split(':')[0];
        return format switch
        {
            "yaml" => true,
            "env" => true,
            "http" when Uri.IsWellFormedUriString(value, UriKind.Absolute) => true,
            "https" when Uri.IsWellFormedUriString(value, UriKind.Absolute) => true,
            _ => false
        };
    }
}
