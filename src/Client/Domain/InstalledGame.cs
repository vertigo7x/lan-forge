namespace LanForge.Client.Domain;

/// <summary>Juego portable instalado localmente.</summary>
public sealed record InstalledGame(
    string GameId,
    string PackageId,
    string Version,
    string InstallPath,
    string EntryPoint,
    string? Arguments,
    DateTimeOffset InstalledUtc);
