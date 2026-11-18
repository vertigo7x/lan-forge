using LanForge.Server.Http;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Sobrescritores de entorno de <see cref="ServerOptions"/>. El servidor de pruebas
/// que lanza la suite e2e de Playwright se configura por variables de entorno
/// (directorio de datos, puerto y direccion de escucha) para no tocar los datos
/// reales del usuario ni el puerto por defecto.
/// </summary>
public sealed class ServerOptionsEnvironmentTests
{
    /// <summary>
    /// Ejecuta el cuerpo con las variables de entorno indicadas y restaura despues
    /// las que habia: las variables de entorno son globales al proceso y las pruebas
    /// pueden correr en paralelo.
    /// </summary>
    private static ServerOptions WithEnvironment(
        string? dataDirectory,
        string? httpPort,
        string? listenAddress,
        Func<ServerOptions> action)
    {
        var previousData = Environment.GetEnvironmentVariable(ServerOptions.DataDirectoryVariable);
        var previousPort = Environment.GetEnvironmentVariable(ServerOptions.HttpPortVariable);
        var previousAddress = Environment.GetEnvironmentVariable(ServerOptions.ListenAddressVariable);

        try
        {
            Environment.SetEnvironmentVariable(ServerOptions.DataDirectoryVariable, dataDirectory);
            Environment.SetEnvironmentVariable(ServerOptions.HttpPortVariable, httpPort);
            Environment.SetEnvironmentVariable(ServerOptions.ListenAddressVariable, listenAddress);

            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerOptions.DataDirectoryVariable, previousData);
            Environment.SetEnvironmentVariable(ServerOptions.HttpPortVariable, previousPort);
            Environment.SetEnvironmentVariable(ServerOptions.ListenAddressVariable, previousAddress);
        }
    }

    [Fact]
    public void FromEnvironment_Sin_variables_devuelve_las_opciones_por_defecto()
    {
        var options = WithEnvironment(null, null, null, ServerOptions.FromEnvironment);

        Assert.Equal(ServerOptions.Default().DataDirectory, options.DataDirectory);
        Assert.Equal(ServerOptions.DefaultPort, options.HttpPort);
        Assert.Equal("0.0.0.0", options.ListenAddress);
    }

    [Fact]
    public void FromEnvironment_Aplica_el_directorio_el_puerto_y_la_direccion()
    {
        var options = WithEnvironment(
            "/tmp/lanforge-e2e-data",
            "8711",
            "127.0.0.1",
            ServerOptions.FromEnvironment);

        Assert.Equal("/tmp/lanforge-e2e-data", options.DataDirectory);
        Assert.Equal(8711, options.HttpPort);
        Assert.Equal("127.0.0.1", options.ListenAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-un-puerto")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    public void FromEnvironment_Un_puerto_invalido_se_ignora_y_conserva_el_por_defecto(string port)
    {
        var options = WithEnvironment(null, port, null, ServerOptions.FromEnvironment);

        // Un valor invalido no debe impedir que el servidor arranque: se ignora.
        Assert.Equal(ServerOptions.DefaultPort, options.HttpPort);
    }

    [Fact]
    public void FromEnvironment_Un_directorio_vacio_se_ignora_y_conserva_el_por_defecto()
    {
        var options = WithEnvironment("   ", null, null, ServerOptions.FromEnvironment);

        Assert.Equal(ServerOptions.Default().DataDirectory, options.DataDirectory);
    }
}
