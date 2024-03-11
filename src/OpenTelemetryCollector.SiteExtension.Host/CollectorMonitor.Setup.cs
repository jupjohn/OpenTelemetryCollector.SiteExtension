using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace OpenTelemetryCollector.SiteExtension.Host;

public partial class CollectorMonitor
{
    private async ValueTask<bool> TryDownloadBinaryIfNotExistsAsync(FileInfo outputBinary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (outputBinary.Exists)
        {
            // TODO: revalidate sum? can we? sums are at the archive level, not the binary
            logger.LogDebug("Found existing Open Telemetry collector binary at {BinaryPath}, skipping download",
                outputBinary.FullName);
            return true;
        }

        if (TryCreateBinaryDirectory(outputBinary) is false)
        {
            return false;
        }

        logger.LogInformation("Downloading Open Telemetry collector binary to {FilePath}", outputBinary.FullName);
        return await DownloadBinaryForPlatformAsync(outputBinary, cancellationToken);
    }

    private bool TryCreateBinaryDirectory(FileInfo binaryLocation)
    {
        var parentDirectory = binaryLocation.Directory;
        if (parentDirectory is null)
        {
            logger.LogDebug("Couldn't get the parent directory of the target file {FilePath}", binaryLocation.FullName);
            return false;
        }

        if (parentDirectory.Exists)
        {
            return true;
        }

        try
        {
            binaryLocation.Create();
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Failed to create destination directory for binary at {DirectoryPath}", parentDirectory.FullName);
            return false;
        }

        return true;
    }

    private async Task<bool> DownloadBinaryForPlatformAsync(FileInfo outputBinary, CancellationToken cancellationToken)
    {
        var downloadedArtifact = await DownloadAndVerifyArtifactAsync(cancellationToken);
        if (downloadedArtifact.Success is false)
        {
            // TODO: handle?
            return false;
        }

        // TODO: we just assume gzipped tar file, would be nice to also support non-archived files (raw binaries)
        var decompressionStream = new GZipStream(new MemoryStream(downloadedArtifact.Data), CompressionMode.Decompress);

        var extracted = false;
        var expectedBinaryName = options.Value.ArchivedBinaryName;

        await using var tar = new TarReader(decompressionStream, leaveOpen: true);
        do
        {
            var entry = await tar.GetNextEntryAsync(copyData: false, cancellationToken);
            if (entry is null)
            {
                break;
            }

            if (entry.Name != expectedBinaryName)
            {
                continue;
            }

            // TODO: I've seen this write part of a file (possible app stop during init), need to ensure all is written
            await entry.ExtractToFileAsync(outputBinary.FullName, overwrite: true, cancellationToken);
            logger.LogDebug("Extracted {FileName} to {Destination}", entry.Name, outputBinary.FullName);

            extracted = true;
        } while (extracted is false);

        if (extracted is false)
        {
            logger.LogError("Failed to find collector binary with name {FileName} in received tar file", expectedBinaryName);
            return false;
        }

        return true;
    }

    private async Task<(bool Success, byte[] Data)> DownloadAndVerifyArtifactAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();

        const int maxDownloadAttempts = 3;
        var downloadAttempts = 0;
        do
        {
            downloadAttempts++;
            var binaryLocation = options.Value.BinaryLocation;
            using var response = await client.GetAsync(binaryLocation, cancellationToken);
            // TODO: handle things like 404

            logger.LogDebug("Beginning download of {CollectorUrl}", binaryLocation);

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength == 0)
            {
                logger.LogError("Failed to initiate collector artifact download: response contains 0 bytes");
                continue;
            }

            var fileData = new byte[contentLength!.Value];
            await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                // TODO: handle
                var readBytes = await responseStream.ReadAsync(fileData, cancellationToken);
                if (readBytes < fileData.Length)
                {
                    logger.LogError(
                        "Failed to download collector artifact: received content length ({BytesReceived}) was less than expected ({BytesExpected})",
                        readBytes, contentLength);
                    continue;
                }
            }

            var binaryHash = options.Value.BinaryHash;
            if (binaryHash is null)
            {
                logger.LogWarning("No binary hash was provided, implicitly trusting collector binary file");
                return (Success: true, Data: fileData);
            }

            var artifactHash = SHA256.HashData(fileData);
            var expectedHashBytes = Convert.FromHexString(binaryHash);

            if (artifactHash.AsSpan().SequenceEqual(expectedHashBytes) is false)
            {
                logger.LogError(
                    "Failed to verify collector artifact: content hash {ReceivedHash} does not match expected {ExpectedHash}",
                    Convert.ToHexString(artifactHash), binaryHash.ToUpper());
                downloadAttempts++;
                continue;
            }

            logger.LogInformation("Successfully downloaded & verified collector artifact");

            return (Success: true, Data: fileData);
        } while (downloadAttempts < maxDownloadAttempts);

        logger.LogInformation(
            "Giving up attempting download of collector artifact after {MaxRetryCount} attempts",
            maxDownloadAttempts);

        return (Success: false, Data: []);
    }
}
