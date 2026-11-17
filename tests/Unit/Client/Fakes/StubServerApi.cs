using LanForge.Client.Application;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Protocol;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Client.Fakes;

/// <summary>IServerApi en memoria: sirve un manifiesto y contenido de archivos, con soporte de resume.</summary>
public sealed class StubServerApi : IServerApi
{
    private readonly Dictionary<string, byte[]> _files;

    public SignedPackageManifest Manifest { get; }
    public List<(string Path, long ResumeFrom)> DownloadRequests { get; } = [];

    private byte[]? _archive;

    private byte[] Archive => _archive ??= PackageExtractor.BuildArchive(_files);

    public StubServerApi(SignedPackageManifest manifest, Dictionary<string, byte[]> files, KeyPair? identityKeys = null)
    {
        Manifest = manifest;
        _files = files;
        identityKeys ??= Ed25519.GenerateKeyPair();
        _hello = new HelloResponse(
            ProtocolVersion.Current,
            Fingerprint.FromPublicKey(identityKeys.PublicKey).ToString(),
            "Stub", "stub-id");
    }

    private readonly HelloResponse _hello;

    public string BaseUrl => "http://stub.local:8710";
    public string ClientId => "test-client";

    public Task<HelloResponse> GetHelloAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_hello);

    public Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogEntry>>([
            new CatalogEntry(Manifest.GameId, Manifest.GameTitle, Manifest.PackageId, Manifest.Version,
                _files.Values.Sum(f => (long)f.Length), _files.Count)]);

    public Task<SignedPackageManifest> GetManifestAsync(string packageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Manifest);

    public Task<Stream> OpenDownloadAsync(string packageId, string relativePath, long resumeFromBytes, CancellationToken cancellationToken = default)
    {
        DownloadRequests.Add((relativePath, resumeFromBytes));
        var content = _files[relativePath];
        return Task.FromResult<Stream>(new MemoryStream(content, writable: false)
        {
            Position = (int)Math.Min(resumeFromBytes, content.Length),
        });
    }

    public Task<Stream> OpenArchiveDownloadAsync(string packageId, long resumeFromBytes, CancellationToken cancellationToken = default)
    {
        DownloadRequests.Add(("<zip>", resumeFromBytes));
        return Task.FromResult<Stream>(new MemoryStream(Archive, writable: false)
        {
            Position = (int)Math.Min(resumeFromBytes, Archive.Length),
        });
    }

    public Task<byte[]?> DownloadCoverAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);
}
