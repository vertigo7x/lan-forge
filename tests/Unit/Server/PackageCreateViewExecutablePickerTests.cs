using LanForge.Server.Application;
using LanForge.Server.Views;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas del selector nativo del ejecutable: el diálogo de archivos del
/// sistema operativo se abre filtrado a <c>*.exe</c> y solo deja elegir un
/// archivo existente con esa extensión.
///
/// El diálogo no se abre aquí (en headless no hay sistema operativo detrás):
/// lo que se comprueba son las opciones con las que se abriría.
/// </summary>
public sealed class PackageCreateViewExecutablePickerTests
{
    [Fact]
    public void El_dialogo_del_ejecutable_solo_filtra_por_exe()
    {
        var options = PackageCreateView.ExecutablePickerOptions();

        var filter = Assert.Single(options.FileTypeFilter!);
        Assert.Equal(["*.exe"], filter.Patterns);
    }

    [Fact]
    public void El_dialogo_del_ejecutable_no_admite_varios_archivos()
    {
        var options = PackageCreateView.ExecutablePickerOptions();

        Assert.False(options.AllowMultiple);
    }

    [Fact]
    public void El_patron_del_filtro_es_el_de_la_extension_aceptada()
    {
        // El filtro del diálogo y la regla que aplica el view model tienen que
        // ser la misma: si divergen, el diálogo deja elegir un archivo que el
        // formulario despues rechaza (o al reves).
        Assert.Equal($"*{PackageSourcePaths.ExecutableExtension}", PackageSourcePaths.ExecutablePattern);
        Assert.Equal("*.exe", PackageSourcePaths.ExecutablePattern);
    }
}
