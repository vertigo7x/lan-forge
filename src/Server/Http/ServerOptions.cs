namespace LanForge.Server.Http;

/// <summary>Configuración del host HTTP del servidor.</summary>
public sealed record ServerOptions(string DataDirectory, int HttpPort, string ListenAddress = "0.0.0.0")
{
    public const int DefaultPort = 8710;

    /// <summary>Variable de entorno que sobrescribe el directorio de datos.</summary>
    public const string DataDirectoryVariable = "LANFORGE_DATA_DIR";

    /// <summary>Variable de entorno que sobrescribe el puerto HTTP.</summary>
    public const string HttpPortVariable = "LANFORGE_HTTP_PORT";

    /// <summary>Variable de entorno que sobrescribe la direccion de escucha.</summary>
    public const string ListenAddressVariable = "LANFORGE_LISTEN_ADDRESS";

    public static ServerOptions Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanForge", "Server"),
        DefaultPort);

    /// <summary>
    /// Opciones por defecto con los sobrescritores de entorno aplicados. Las variables de
    /// entorno permiten arrancar el servidor real de pruebas (el que lanza la suite e2e)
    /// en un puerto y un directorio de datos propios, sin tocar el codigo ni depender
    /// de los datos del usuario. Un valor de entorno invalido se ignora: el servidor
    /// arranca con su configuracion por defecto en lugar de fallar.
    /// </summary>
    public static ServerOptions FromEnvironment()
    {
        var options = Default();

        var dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            options = options with { DataDirectory = dataDirectory };
        }

        var listenAddress = Environment.GetEnvironmentVariable(ListenAddressVariable);
        if (!string.IsNullOrWhiteSpace(listenAddress))
        {
            options = options with { ListenAddress = listenAddress };
        }

        var port = Environment.GetEnvironmentVariable(HttpPortVariable);
        if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort) && parsedPort is > 0 and <= 65535)
        {
            options = options with { HttpPort = parsedPort };
        }

        return options;
    }
}
