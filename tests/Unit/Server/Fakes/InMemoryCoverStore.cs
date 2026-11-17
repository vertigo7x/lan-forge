using LanForge.Server.Application;

namespace LanForge.UnitTests.Server.Fakes;

/// <summary>Portadas en memoria: guarda el contenido por nombre de archivo.</summary>
public sealed class InMemoryCoverStore : ICoverStore
{
    private readonly Dictionary<string, byte[]> _covers = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> SaveAsync(string gameId, string sourceImagePath, CancellationToken cancellationToken = default)
    {
        var fileName = $"{gameId}.png";
        _covers[fileName] = File.ReadAllBytes(sourceImagePath);
        return Task.FromResult(fileName);
    }

    public Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (!_covers.TryGetValue(fileName, out var bytes))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
    }

    public Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_covers.Remove(fileName));

    public bool Exists(string fileName) => _covers.ContainsKey(fileName);
}
