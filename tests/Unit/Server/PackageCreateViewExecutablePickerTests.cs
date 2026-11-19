using Avalonia.Platform.Storage;
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

    // ---------- El diálogo de la carpeta origen ----------

    [Fact]
    public void El_dialogo_de_la_carpeta_elige_una_sola_carpeta()
    {
        // El formulario publica una carpeta: elegir varias no tiene sentido y
        // el selector solo puede devolver una ruta.
        var options = PackageCreateView.SourceDirectoryPickerOptions();

        Assert.False(options.AllowMultiple);
    }

    [Fact]
    public void El_dialogo_de_la_carpeta_no_filtra_por_extension()
    {
        // A diferencia del ejecutable, la carpeta no lleva filtro. Avalonia no
        // admite filtros de extension en el dialogo de carpetas: la propiedad
        // FileTypeFilter no existe en FolderPickerOpenOptions, asi que el
        // dialogo muestra todas las carpetas. Lo que se comprueba despues es
        // que la carpeta exista (PackageSourcePaths.Validate).
        //
        // Se comprueba por reflexion porque la propiedad no existe: si alguien
        // la anadiera al modelo de Avalonia, esta prueba avisaria.
        Assert.Null(typeof(FolderPickerOpenOptions).GetProperty("FileTypeFilter"));
        Assert.Null(PackageCreateView.SourceDirectoryPickerOptions().GetType()
            .GetProperty("FileTypeFilter"));
    }

    [Fact]
    public void El_dialogo_de_la_carpeta_tiene_titulo()
    {
        // Sin titulo, el diálogo sale con el nombre generico del sistema
        // operativo y el organizador no sabe que tiene que elegir la carpeta
        // del juego portable.
        var options = PackageCreateView.SourceDirectoryPickerOptions();

        Assert.Equal("Elige la carpeta del juego portable", options.Title);
    }

    [Fact]
    public void El_dialogo_del_ejecutable_tiene_titulo()
    {
        var options = PackageCreateView.ExecutablePickerOptions();

        Assert.Equal("Elige el ejecutable del juego", options.Title);
    }
}
