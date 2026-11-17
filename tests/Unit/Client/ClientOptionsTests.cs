using LanForge.Client;

namespace LanForge.UnitTests.Client;

/// <summary>
/// Comparte la coleccion "Cliente-UI" porque toca la variable de entorno del
/// directorio de datos: si corriera en paralelo, romperia los tests de UI.
/// </summary>
[Collection("Cliente-UI")]
public sealed class ClientOptionsTests
{
    [Fact]
    public void Sin_variable_de_entorno_usa_el_perfil_del_usuario()
    {
        var previous = Environment.GetEnvironmentVariable(ClientOptions.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(ClientOptions.DataDirectoryVariable, null);

            var options = ClientOptions.Default();

            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LanForge", "Client"),
                options.DataDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClientOptions.DataDirectoryVariable, previous);
        }
    }

    [Fact]
    public void La_variable_de_entorno_fija_el_directorio_de_datos()
    {
        var previous = Environment.GetEnvironmentVariable(ClientOptions.DataDirectoryVariable);
        var root = Path.Combine(Path.GetTempPath(), "lanforge-options-test");
        try
        {
            Environment.SetEnvironmentVariable(ClientOptions.DataDirectoryVariable, root);

            var options = ClientOptions.Default();

            Assert.Equal(root, options.DataDirectory);
            Assert.Equal(Path.Combine(root, "downloads"), options.DownloadsDirectory);
            Assert.Equal(Path.Combine(root, "games"), options.InstallRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClientOptions.DataDirectoryVariable, previous);
        }
    }
}
