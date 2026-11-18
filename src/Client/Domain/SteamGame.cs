namespace LanForge.Client.Domain;

/// <summary>Juego instalado detectado en una biblioteca local de Steam.</summary>
public sealed record SteamGame(
    int AppId,
    string Name,
    string InstallPath,
    string LaunchCommand)
{
    public static SteamGame Create(int appId, string name, string installPath) =>
        new(appId, name, installPath, $"steam://run/{appId}");
}
