namespace LanForge.Server.Http;

/// <summary>Configuración del host HTTP del servidor.</summary>
public sealed record ServerOptions(string DataDirectory, int HttpPort, string ListenAddress = "0.0.0.0")
{
    public const int DefaultPort = 8710;

    public static ServerOptions Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanForge", "Server"),
        DefaultPort);
}
