namespace LanForge.Server.Application;

/// <summary>
/// Conflicto de publicación: la versión indicada ya está publicada para ese juego.
/// Hereda de <see cref="InvalidOperationException"/> para no romper a quien la
/// capturaba como error genérico de publicación.
/// </summary>
public sealed class PackageVersionConflictException : InvalidOperationException
{
    public PackageVersionConflictException(string gameId, string version)
        : base($"Ya está publicada la versión '{version}' de '{gameId}'.")
    {
        GameId = gameId;
        Version = version;
    }

    public string GameId { get; }

    public string Version { get; }
}
