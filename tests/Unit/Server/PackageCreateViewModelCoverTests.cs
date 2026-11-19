using LanForge.Server.Application;
using LanForge.Server.ViewModels;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas del selector de imagen del formulario de creacion de paquetes: validacion
/// del fichero, previsualizacion, limpieza y adjuntado de la portada al publicar.
/// </summary>
public sealed class PackageCreateViewModelCoverTests
{
    [Fact]
    public void Seleccionar_una_imagen_valida_guarda_el_fichero_y_muestra_la_previsualizacion()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var loader = new RecordingPreviewLoader();
        var viewModel = new PackageCreateViewModel(
            new RecordingPackageCreationService(),
            coverUploads: null,
            previewLoader: loader);

        var error = viewModel.SelectCover(image, "image/png");

        Assert.Null(error);
        Assert.Equal(image, viewModel.CoverPath);
        Assert.True(viewModel.HasCoverSelected);
        Assert.False(viewModel.HasCoverPathError);

        // La previsualizacion se pide al cargador con el fichero elegido.
        Assert.Equal(image, Assert.Single(loader.Requests));
    }

    [Fact]
    public void Seleccionar_un_png_renombrado_con_contenido_no_imagen_muestra_un_error()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        File.WriteAllText(image, "esto no es una imagen");
        var loader = new RecordingPreviewLoader();
        var viewModel = new PackageCreateViewModel(
            new RecordingPackageCreationService(),
            coverUploads: null,
            previewLoader: loader);

        var error = viewModel.SelectCover(image, "image/png");

        Assert.NotNull(error);
        Assert.False(viewModel.HasCoverSelected);
        Assert.Null(viewModel.CoverPreview);
        Assert.Empty(loader.Requests);
    }

    [Fact]
    public void Seleccionar_un_fichero_que_no_es_una_imagen_muestra_el_error_y_no_selecciona()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "documento.txt");
        File.WriteAllText(file, "esto no es una imagen");
        var viewModel = new PackageCreateViewModel(new RecordingPackageCreationService());

        var error = viewModel.SelectCover(file, "text/plain");

        Assert.NotNull(error);
        Assert.True(viewModel.HasCoverPathError);
        Assert.False(viewModel.HasCoverSelected);
        Assert.Equal(string.Empty, viewModel.CoverPath);
    }

    [Fact]
    public void Seleccionar_una_imagen_que_pasa_del_tamano_maximo_muestra_el_error()
    {
        using var temp = new TempDirectory();
        var image = Path.Combine(temp.Path, "portada.png");
        // Fichero valido en formato pero por encima del limite de 5 MB.
        File.WriteAllBytes(image, new byte[CoverImageRules.MaxSizeBytes + 1]);
        var viewModel = new PackageCreateViewModel(new RecordingPackageCreationService());

        var error = viewModel.SelectCover(image, "image/png");

        Assert.NotNull(error);
        Assert.Contains("5 MB", error, StringComparison.Ordinal);
        Assert.True(viewModel.HasCoverPathError);
        Assert.False(viewModel.HasCoverSelected);
    }

    [Fact]
    public void Una_seleccion_invalida_descarta_la_imagen_anterior()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var file = Path.Combine(temp.Path, "documento.txt");
        File.WriteAllText(file, "esto no es una imagen");
        var viewModel = new PackageCreateViewModel(new RecordingPackageCreationService());

        viewModel.SelectCover(image, "image/png");
        Assert.True(viewModel.HasCoverSelected);

        viewModel.SelectCover(file, "text/plain");

        // El usuario no debe poder adjuntar sin querer la imagen anterior.
        Assert.False(viewModel.HasCoverSelected);
        Assert.Equal(string.Empty, viewModel.CoverPath);
        Assert.Null(viewModel.CoverPreview);
    }

    [Fact]
    public void Limpiar_la_seleccion_quita_el_fichero_la_previsualizacion_y_el_error()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var viewModel = new PackageCreateViewModel(new RecordingPackageCreationService());
        viewModel.SelectCover(image, "image/png");

        viewModel.ClearCover();

        Assert.False(viewModel.HasCoverSelected);
        Assert.Equal(string.Empty, viewModel.CoverPath);
        Assert.Null(viewModel.CoverPreview);
        Assert.False(viewModel.HasCoverPathError);
    }

    [Fact]
    public void Limpiar_la_seleccion_borra_el_temporal_que_posee_el_formulario()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var temporary = Path.Combine(Path.GetTempPath(), $"lanforge-cover-{Guid.NewGuid():n}.png");
        File.Copy(image, temporary);
        var viewModel = new PackageCreateViewModel(new RecordingPackageCreationService());

        viewModel.SelectTemporaryCover(temporary, "image/png");
        viewModel.ClearCover();

        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public void Se_puede_sustituir_la_imagen_por_otra()
    {
        using var temp = new TempDirectory();
        var first = CreateImage(temp, "primera.png");
        var second = CreateImage(temp, "segunda.png");
        var viewModel = new PackageCreateViewModel(new RecordingPackageCreationService());

        viewModel.SelectCover(first, "image/png");
        viewModel.SelectCover(second, "image/jpeg");

        Assert.Equal(second, viewModel.CoverPath);
        Assert.False(viewModel.HasCoverPathError);
    }

    [Fact]
    public async Task Publicar_adjunta_la_portada_al_juego_creado()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var service = new RecordingPackageCreationService();
        var covers = new RecordingCoverUploadService();
        var viewModel = CreateValidViewModel(service, covers);
        viewModel.SelectCover(image, "image/png");

        await viewModel.SubmitAsync();

        var upload = Assert.Single(covers.Uploads);
        Assert.Equal("neon-drift", upload.GameId);
        Assert.Equal(image, upload.ImagePath);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task Publicar_sin_imagen_no_adjunta_ninguna_portada()
    {
        var service = new RecordingPackageCreationService();
        var covers = new RecordingCoverUploadService();
        var viewModel = CreateValidViewModel(service, covers);

        await viewModel.SubmitAsync();

        Assert.Empty(covers.Uploads);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task Publicar_con_una_portada_invalida_no_crea_el_paquete()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var service = new RecordingPackageCreationService();
        var covers = new RecordingCoverUploadService();
        var viewModel = CreateValidViewModel(service, covers);
        viewModel.SelectCover(image, "image/png");

        // La imagen desaparece entre la seleccion y el envio: el envio se para
        // con un error claro en vez de fallar por sorpresa mas tarde.
        File.Delete(image);

        await viewModel.SubmitAsync();

        Assert.Empty(service.Requests);
        Assert.Empty(covers.Uploads);
        Assert.True(viewModel.StatusIsError);
        Assert.True(viewModel.HasCoverPathError);
    }

    [Fact]
    public async Task Publicar_avisa_si_la_portada_no_se_ha_podido_adjuntar()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var service = new RecordingPackageCreationService();
        var covers = new RecordingCoverUploadService { Failure = new InvalidOperationException("el juego no existe") };
        var viewModel = CreateValidViewModel(service, covers);
        viewModel.SelectCover(image, "image/png");

        await viewModel.SubmitAsync();

        // El paquete si se ha creado: el aviso solo habla de la portada.
        Assert.Single(service.Requests);
        Assert.True(viewModel.StatusIsError);
        Assert.Contains("portada", viewModel.StatusMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publicar_avisa_si_no_hay_servicio_de_portadas_disponible()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var service = new RecordingPackageCreationService();
        var viewModel = CreateValidViewModel(service, coverUploads: null);
        viewModel.SelectCover(image, "image/png");

        await viewModel.SubmitAsync();

        Assert.Single(service.Requests);
        Assert.True(viewModel.StatusIsError);
        Assert.Contains("portada", viewModel.StatusMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tras_publicar_el_formulario_queda_sin_portada()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var viewModel = CreateValidViewModel(
            new RecordingPackageCreationService(),
            new RecordingCoverUploadService());
        viewModel.SelectCover(image, "image/png");

        await viewModel.SubmitAsync();

        Assert.False(viewModel.HasCoverSelected);
        Assert.Equal(string.Empty, viewModel.CoverPath);
        Assert.Null(viewModel.CoverPreview);
    }

    [Fact]
    public void Restablecer_el_formulario_tambien_quita_la_portada()
    {
        using var temp = new TempDirectory();
        var image = CreateImage(temp, "portada.png");
        var viewModel = new PackageCreateViewModel(new RecordingPackageCreationService());
        viewModel.SelectCover(image, "image/png");

        viewModel.Reset();

        Assert.False(viewModel.HasCoverSelected);
        Assert.Equal(string.Empty, viewModel.CoverPath);
        Assert.Null(viewModel.CoverPreview);
    }

    /// <summary>Crea un PNG minimo y valido en disco.</summary>
    private static string CreateImage(TempDirectory temp, string fileName)
    {
        var path = Path.Combine(temp.Path, fileName);
        // PNG de 1x1: bytes de cabecera y de datos validos para decodificar.
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static PackageCreateViewModel CreateValidViewModel(
        IPackageCreationService service,
        ICoverUploadService? coverUploads)
    {
        return new PackageCreateViewModel(service, coverUploads)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
            SourceDirectory = "/games/neon",
            EntryPoint = "game.exe",
        };
    }

    private sealed class RecordingPackageCreationService : IPackageCreationService
    {
        public List<PackageCreationRequest> Requests { get; } = [];

        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(PackageCreationResult.Success(
                new PackageCreationSummary(
                    request.GameId,
                    request.GameId,
                    request.Title,
                    request.Version,
                    3,
                    100)));
        }
    }

    private sealed class RecordingCoverUploadService : ICoverUploadService
    {
        public List<(string GameId, string ImagePath)> Uploads { get; } = [];

        public Exception? Failure { get; set; }

        public Task<string> UploadAsync(
            string gameId,
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Uploads.Add((gameId, imagePath));
            return Task.FromResult($"{gameId}.png");
        }
    }

    /// <summary>
    /// Cargador de previsualizacion que no depende de la plataforma de render: la
    /// decodificacion de imagenes necesita el render de Avalonia, que no esta
    /// disponible en todas las pruebas.
    /// </summary>
    private sealed class RecordingPreviewLoader : ICoverPreviewLoader
    {
        public List<string> Requests { get; } = [];

        public Avalonia.Media.Imaging.Bitmap? Load(string imagePath)
        {
            Requests.Add(imagePath);

            // Sin render no se puede decodificar: se comprueba que el view model
            // pide la previsualizacion del fichero correcto, no el pixel.
            return null;
        }
    }
}
