using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LanForge.Server.Application;

namespace LanForge.Server.Infrastructure;

/// <summary>Branding persistente en <c>{dataDir}/branding</c>.</summary>
public sealed class FileLanPartyBrandingStore : ILanPartyBrandingStore
{
    private static readonly Regex FileNamePattern = new(
        "^lan-party-[a-f0-9]{64}\\.(png|jpg|jpeg|webp)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _directory;
    private readonly string _metadataPath;

    public string? LogoFileName { get; private set; }

    public FileLanPartyBrandingStore(string dataDirectory)
    {
        _directory = Path.Combine(dataDirectory, "branding");
        Directory.CreateDirectory(_directory);
        _metadataPath = Path.Combine(_directory, "branding.json");
        if (File.Exists(_metadataPath))
        {
            try
            {
                var fileName = JsonSerializer.Deserialize<BrandingData>(File.ReadAllText(_metadataPath))?.LogoFileName;
                LogoFileName = !string.IsNullOrWhiteSpace(fileName) && FileNamePattern.IsMatch(fileName) &&
                    File.Exists(Path.Combine(_directory, fileName))
                    ? fileName
                    : null;
            }
            catch (JsonException)
            {
                // Un estado de branding corrupto no debe impedir arrancar el servidor.
                LogoFileName = null;
            }
        }
    }

    public Task<string> SaveLogoAsync(string sourceImagePath, CancellationToken cancellationToken = default)
    {
        var error = LanPartyLogoRules.ValidateFile(sourceImagePath);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(sourceImagePath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(sourceImagePath).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceImagePath))).ToLowerInvariant();
        var fileName = $"lan-party-{hash}{extension}";
        File.Copy(sourceImagePath, Path.Combine(_directory, fileName), overwrite: true);

        var previous = LogoFileName;
        LogoFileName = fileName;
        File.WriteAllText(_metadataPath, JsonSerializer.Serialize(new BrandingData(fileName)));
        if (!string.IsNullOrWhiteSpace(previous) && !string.Equals(previous, fileName, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(previous);
        }

        return Task.FromResult(fileName);
    }

    public Task<Stream?> OpenLogoAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(fileName, LogoFileName, StringComparison.OrdinalIgnoreCase) ||
            !FileNamePattern.IsMatch(fileName))
        {
            return Task.FromResult<Stream?>(null);
        }

        var path = Path.Combine(_directory, fileName);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null);
    }

    public Task ClearLogoAsync(CancellationToken cancellationToken = default)
    {
        TryDelete(LogoFileName);
        LogoFileName = null;
        File.Delete(_metadataPath);
        return Task.CompletedTask;
    }

    private void TryDelete(string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName) && FileNamePattern.IsMatch(fileName))
        {
            File.Delete(Path.Combine(_directory, fileName));
        }
    }

    private sealed record BrandingData(string? LogoFileName);
}
