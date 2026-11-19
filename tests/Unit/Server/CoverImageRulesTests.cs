using LanForge.Server.Application;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de las reglas de las imagenes de portada: formato, tipo MIME y tamaño.
/// Son la base del selector del formulario y del almacen de portadas, asi que las
/// dos capas tienen que aplicar exactamente las mismas.
/// </summary>
public sealed class CoverImageRulesTests
{
    [Theory]
    [InlineData("portada.png")]
    [InlineData("portada.PNG")]
    [InlineData("portada.jpg")]
    [InlineData("portada.jpeg")]
    [InlineData("portada.webp")]
    public void Acepta_los_formatos_de_imagen_admitidos(string fileName)
    {
        Assert.Null(CoverImageRules.Validate(fileName, "image/png", 1024));
    }

    [Theory]
    [InlineData("documento.txt")]
    [InlineData("paquete.zip")]
    [InlineData("imagen.bmp")]
    [InlineData("imagen.gif")]
    [InlineData("sin_extension")]
    public void Rechaza_los_ficheros_que_no_son_imagen_admitidas(string fileName)
    {
        var error = CoverImageRules.Validate(fileName, "image/png", 1024);

        Assert.NotNull(error);
        Assert.Contains("PNG", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rechaza_un_tipo_mime_que_no_es_de_imagen()
    {
        var error = CoverImageRules.Validate("portada.png", "application/pdf", 1024);

        Assert.NotNull(error);
        Assert.Contains("no es una imagen", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Acepta_un_tipo_mime_desconocido_porque_la_extension_sigue_siendo_obligatoria()
    {
        // Algunos sistemas de ficheros no informan del tipo MIME: la extension
        // sigue validando el formato.
        Assert.Null(CoverImageRules.Validate("portada.png", contentType: null, sizeBytes: 1024));
        Assert.Null(CoverImageRules.Validate("portada.png", contentType: string.Empty, sizeBytes: 1024));
    }

    [Fact]
    public void Acepta_el_tipo_mime_de_jpeg_asi_lo_denomine_el_sistema()
    {
        Assert.Null(CoverImageRules.Validate("portada.jpg", "image/jpg", 1024));
    }

    [Fact]
    public void Rechaza_una_imagen_que_pasa_del_tamano_maximo()
    {
        var error = CoverImageRules.Validate(
            "portada.png",
            "image/png",
            CoverImageRules.MaxSizeBytes + 1);

        Assert.NotNull(error);
        Assert.Contains("5 MB", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Acepta_una_imagen_justo_en_el_tamano_maximo()
    {
        Assert.Null(CoverImageRules.Validate("portada.png", "image/png", CoverImageRules.MaxSizeBytes));
    }

    [Fact]
    public void Rechaza_un_fichero_vacio()
    {
        var error = CoverImageRules.Validate("portada.png", "image/png", 0);

        Assert.NotNull(error);
    }

    [Fact]
    public void Rechaza_un_fichero_renombrado_como_png_cuyo_contenido_no_es_una_imagen()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "portada.png");
        File.WriteAllText(path, "esto no es una imagen");

        var error = CoverImageRules.ValidateFile(path, contentType: null);

        Assert.NotNull(error);
        Assert.Contains("contenido", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rechaza_un_png_con_cabecera_valida_pero_cuerpo_truncado()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "portada.png");
        File.WriteAllBytes(path,
        [
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x00,
        ]);

        var error = CoverImageRules.ValidateFile(path);

        Assert.NotNull(error);
        Assert.Contains("contenido", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task La_copia_temporal_sobredimensionada_elimina_el_fichero_parcial_despues_de_cerrar_el_stream()
    {
        using var temporaryDirectory = new TempDirectory();
        await using var source = new MemoryStream(new byte[CoverImageRules.MaxSizeBytes + 1]);

        var (path, error) = await CoverTemporaryFile.CopyFromAsync(
            source,
            "portada.png",
            temporaryDirectory: temporaryDirectory.Path);

        Assert.Null(path);
        Assert.Contains("5 MB", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory.Path, "lanforge-cover-*"));
    }

    [Fact]
    public void El_tamano_maximo_es_de_cinco_megabytes()
    {
        Assert.Equal(5 * 1024 * 1024, CoverImageRules.MaxSizeBytes);
    }
}
