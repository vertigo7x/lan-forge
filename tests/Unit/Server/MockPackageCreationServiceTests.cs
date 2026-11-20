using System.Diagnostics;
using LanForge.Server.Application;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de la capa de servicio simulada de creación de paquetes. Verifican que
/// el mock reproduce el contrato que cumplirá el cliente HTTP real: latencia,
/// éxito, errores de validación por campo y sustitución transparente.
/// </summary>
public sealed class MockPackageCreationServiceTests : IDisposable
{
    private readonly TemporaryGameFolder _game = new();

    public void Dispose() => _game.Dispose();

    /// <summary>
    /// Peticion valida: la carpeta y el ejecutable existen de verdad porque el
    /// validador del formulario comprueba que ambas rutas esten en el disco.
    /// </summary>
    private PackageCreationRequest ValidRequest(string gameId = "neon-drift") =>
        new(gameId, "Neon Drift", "1.0.0", _game.SourceDirectory, _game.ExecutablePath, "--fullscreen");

    [Fact]
    public async Task Devuelve_exito_para_una_peticion_valida()
    {
        var service = new MockPackageCreationService(TimeSpan.Zero);

        var result = await service.CreateAsync(ValidRequest());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Package);
        Assert.Equal("neon-drift", result.Package!.GameId);
        Assert.Equal("Neon Drift", result.Package.Title);
        Assert.Equal("1.0.0", result.Package.Version);
        Assert.Empty(result.FieldErrors);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task El_titulo_vacio_es_un_error_de_validacion_como_en_el_servidor()
    {
        // El servicio local exige el titulo; el mock reproduce esa misma regla
        // para que la UI se comporte igual en pruebas y en producción.
        var service = new MockPackageCreationService(TimeSpan.Zero);

        var result = await service.CreateAsync(ValidRequest() with { Title = "  " });

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors.ContainsKey(PackageCreationFields.Title));
    }

    [Fact]
    public async Task Devuelve_error_de_validacion_por_campo()
    {
        var service = new MockPackageCreationService(TimeSpan.Zero);

        var result = await service.CreateAsync(ValidRequest() with { Version = "no-es-una-version" });

        Assert.False(result.Succeeded);
        Assert.Null(result.Package);
        Assert.True(result.FieldErrors.ContainsKey(PackageCreationFields.Version));
        Assert.False(string.IsNullOrWhiteSpace(result.FieldErrors[PackageCreationFields.Version]));
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task Devuelve_todos_los_campos_incorrectos_a_la_vez()
    {
        var service = new MockPackageCreationService(TimeSpan.Zero);

        var result = await service.CreateAsync(new PackageCreationRequest("", "", "", "", "", null));

        Assert.False(result.Succeeded);
        Assert.Contains(PackageCreationFields.GameId, result.FieldErrors.Keys);
        Assert.Contains(PackageCreationFields.Title, result.FieldErrors.Keys);
        Assert.Contains(PackageCreationFields.Version, result.FieldErrors.Keys);
        Assert.Contains(PackageCreationFields.SourceDirectory, result.FieldErrors.Keys);
        Assert.Contains(PackageCreationFields.EntryPoint, result.FieldErrors.Keys);
    }

    [Fact]
    public async Task Simula_la_latencia_configurada()
    {
        var latencia = TimeSpan.FromMilliseconds(250);
        var service = new MockPackageCreationService(latencia);

        var stopwatch = Stopwatch.StartNew();
        await service.CreateAsync(ValidRequest());
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(200),
            $"La latencia simulada fue de {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Marca_el_estado_de_carga_mientras_responde()
    {
        var service = new MockPackageCreationService(TimeSpan.FromMilliseconds(120));

        var tarea = service.CreateAsync(ValidRequest());
        Assert.True(service.IsBusy);

        await tarea;
        Assert.False(service.IsBusy);
    }

    [Fact]
    public async Task El_comportamiento_puede_forzar_un_error_concreto()
    {
        var service = new MockPackageCreationService(
            TimeSpan.Zero,
            _ => PackageCreationResult.Failure("El servidor no responde."));

        var result = await service.CreateAsync(ValidRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("El servidor no responde.", result.ErrorMessage);
        Assert.Empty(result.FieldErrors);
    }

    [Fact]
    public async Task Registra_las_peticiones_recibidas()
    {
        var service = new MockPackageCreationService(TimeSpan.Zero);

        await service.CreateAsync(ValidRequest("juego-1"));
        await service.CreateAsync(ValidRequest("juego-2"));

        Assert.Equal(2, service.Requests.Count);
        Assert.Equal("juego-1", service.Requests[0].GameId);
        Assert.Equal("juego-2", service.Requests[1].GameId);
    }

    [Fact]
    public async Task Respeta_el_token_de_cancelacion()
    {
        var service = new MockPackageCreationService(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();

        var tarea = service.CreateAsync(ValidRequest(), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarea);
        Assert.False(service.IsBusy);
    }

    [Fact]
    public void Rechaza_una_latencia_negativa()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MockPackageCreationService(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void El_mock_satisface_el_contrato_del_servicio()
    {
        // El consumidor solo depende de la interfaz: cualquier implementacion
        // valida como servicio y se puede sustituir sin tocar al consumidor.
        IPackageCreationService service = new MockPackageCreationService();

        Assert.IsAssignableFrom<IPackageCreationService>(service);
    }
}
