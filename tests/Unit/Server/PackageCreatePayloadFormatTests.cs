using System.Net;
using System.Text;
using System.Text.Json;
using LanForge.Server.Application;
using LanForge.Server.ViewModels;

namespace LanForge.UnitTests.Server;

/// <summary>
/// El formato del payload de publicación: lo que sale por el cable cuando el
/// organizador pulsa el botón de publicar.
///
/// Estas pruebas capturan el CUERPO REAL de la petición HTTP (no un doble de
/// prueba que mira el objeto C#), para fijar el formato previo del contrato:
///
/// - La carpeta origen viaja como la RUTA ABSOLUTA elegida, sin transformar.
/// - El ejecutable viaja como la RUTA RELATIVA dentro de esa carpeta
///   (<c>game.exe</c>), que es lo que el endpoint y el manifiesto esperan.
/// - Los nombres de los campos del JSON son los del contrato
///   (<c>gameId</c>, <c>title</c>, <c>version</c>, <c>sourceDirectory</c>,
///   <c>entryPoint</c>, <c>arguments</c>) y no cambian.
/// - La ruta es la del SERVIDOR: la que devuelve el diálogo del sistema
///   operativo, no una ruta del cliente.
///
/// El cuerpo se compara campo a campo con un lector de JSON, en vez de
/// comparar cadenas: asi el test dice que campo ha cambiado y por cuanto, y
/// no falla por el orden de las propiedades.
/// </summary>
public sealed class PackageCreatePayloadFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"lanforge-payload-{Guid.NewGuid():N}");

    private readonly string _sourceDirectory;
    private readonly string _executable;

    public PackageCreatePayloadFormatTests()
    {
        _sourceDirectory = Path.Combine(_root, "game");
        Directory.CreateDirectory(_sourceDirectory);
        _executable = Path.Combine(_sourceDirectory, "game.exe");
        File.WriteAllText(_executable, "MZ");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_conserva_la_carpeta_absoluta_y_el_ejecutable_relativo()
    {
        var body = await CapturePayloadAsync(_sourceDirectory, _executable);

        // La carpeta origen viaja tal cual, como ruta absoluta.
        Assert.Equal(_sourceDirectory, body.GetProperty("sourceDirectory").GetString());

        // El ejecutable viaja como la ruta relativa dentro de la carpeta.
        Assert.Equal("game.exe", body.GetProperty("entryPoint").GetString());
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_usa_los_nombres_del_contrato()
    {
        var body = await CapturePayloadAsync(_sourceDirectory, _executable);

        // El contrato del endpoint: estos nombres no pueden cambiar sin romper
        // el servidor y el cliente a la vez.
        Assert.True(body.TryGetProperty("gameId", out _));
        Assert.True(body.TryGetProperty("title", out _));
        Assert.True(body.TryGetProperty("version", out _));
        Assert.True(body.TryGetProperty("sourceDirectory", out _));
        Assert.True(body.TryGetProperty("entryPoint", out _));
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_lleva_los_datos_del_formulario_sin_cambiarlos()
    {
        var body = await CapturePayloadAsync(_sourceDirectory, _executable);

        Assert.Equal("neon-drift", body.GetProperty("gameId").GetString());
        Assert.Equal("Neon Drift", body.GetProperty("title").GetString());
        Assert.Equal("1.2.3", body.GetProperty("version").GetString());
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_lleva_los_argumentos_cuando_hay()
    {
        var body = await CapturePayloadAsync(_sourceDirectory, _executable, "--fullscreen");

        Assert.Equal("--fullscreen", body.GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_omite_los_argumentos_cuando_no_hay()
    {
        var body = await CapturePayloadAsync(_sourceDirectory, _executable);

        // Los argumentos son opcionales: sin ellos, la propiedad no aparece en
        // el JSON (las opciones compartidas omiten los nulos).
        Assert.False(body.TryGetProperty("arguments", out _));
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_es_el_json_del_contrato()
    {
        // El cuerpo exacto, para atar el formato previo de una vez. Las
        // opciones JSON compartidas usan nombres en camelCase y omiten los
        // nulos, asi que el cuerpo tiene estas seis claves y nada mas.
        var raw = await CaptureRawPayloadAsync(_sourceDirectory, _executable, "--fullscreen");

        using var document = JsonDocument.Parse(raw);
        var properties = document.RootElement.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["arguments", "entryPoint", "gameId", "sourceDirectory", "title", "version"],
            properties);
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_no_lleva_la_ruta_absoluta_del_ejecutable()
    {
        var body = await CapturePayloadAsync(_sourceDirectory, _executable);

        // Si la ruta absoluta del ejecutable se colara en el payload, el servidor
        // la interpretaria como una ruta relativa y publicaria un archivo
        // llamado "C:\...". La ruta absoluta solo vale para el servidor.
        Assert.DoesNotContain(
            _sourceDirectory,
            body.GetProperty("entryPoint").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_conserva_el_nombre_del_ejecutable_en_mayusculas()
    {
        // Un «GAME.EXE» es un ejecutable igual que un «game.exe»: la carpeta se
        // envia igual y la ruta relativa conserva el nombre tal como esta en el
        // disco.
        var executable = Path.Combine(_sourceDirectory, "GAME.EXE");
        File.WriteAllText(executable, "MZ");

        var body = await CapturePayloadAsync(_sourceDirectory, executable);

        Assert.Equal(_sourceDirectory, body.GetProperty("sourceDirectory").GetString());
        Assert.Equal("GAME.EXE", body.GetProperty("entryPoint").GetString());
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_lleva_el_ejecutable_relativo_de_un_subdirectorio()
    {
        // El ejecutable no tiene que estar en la raiz de la carpeta: puede estar
        // en un subdirectorio («bin/game.exe»). La ruta relativa lo conserva y el
        // servidor lo resuelve dentro del paquete.
        var bin = Path.Combine(_sourceDirectory, "bin");
        Directory.CreateDirectory(bin);
        var executable = Path.Combine(bin, "game.exe");
        File.WriteAllText(executable, "MZ");

        var body = await CapturePayloadAsync(_sourceDirectory, executable);

        Assert.Equal(_sourceDirectory, body.GetProperty("sourceDirectory").GetString());
        Assert.Equal("bin/game.exe", body.GetProperty("entryPoint").GetString());

        // La ruta relativa no puede llevar la carpeta absoluta delante.
        Assert.DoesNotContain(
            _sourceDirectory,
            body.GetProperty("entryPoint").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_recorta_los_espacios_de_los_campos()
    {
        // El organizador puede dejar espacios alrededor de lo que escribe o de
        // lo que elige. Los espacios sobrantes no pueden viajar al servidor: la
        // carpeta se manda como la ruta absoluta real y el ejecutable como la
        // ruta relativa.
        var body = await CapturePayloadAsync(
            _sourceDirectory,
            _executable,
            arguments: "  --fullscreen  ");

        Assert.Equal(_sourceDirectory, body.GetProperty("sourceDirectory").GetString());
        Assert.Equal("game.exe", body.GetProperty("entryPoint").GetString());
        Assert.Equal("--fullscreen", body.GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task El_cliente_manda_el_ejecutable_absoluto_y_el_servidor_el_relativo()
    {
        // INVARIANTE DEL CONTRATO: el formulario manda la ruta ABSOLUTA del
        // ejecutable (la que devuelve el dialogo del sistema operativo y la que
        // se comprueba contra el disco), pero el payload lleva la ruta RELATIVA
        // dentro de la carpeta, que es lo que el endpoint firma en el manifiesto
        // y lo que el cliente resuelve contra el directorio de instalacion.
        //
        // BuildFormRequest y BuildPayloadRequest son casi identicos y solo se
        // diferencian en esa linea: si alguien los unifica, el servidor
        // recibiria una ruta absoluta y publicaria un archivo llamado
        // «C:\...». Este test ata las dos mitades.
        var raw = await CaptureRawPayloadAsync(_sourceDirectory, _executable);

        using var document = JsonDocument.Parse(raw);
        var sourceDirectory = document.RootElement.GetProperty("sourceDirectory").GetString();
        var entryPoint = document.RootElement.GetProperty("entryPoint").GetString();

        // La carpeta origen viaja ABSOLUTA: es la ruta del servidor.
        Assert.Equal(_sourceDirectory, sourceDirectory);
        Assert.True(Path.IsPathRooted(sourceDirectory!));

        // El ejecutable viaja RELATIVO a esa carpeta.
        Assert.Equal("game.exe", entryPoint);
        Assert.False(Path.IsPathRooted(entryPoint!));
        Assert.DoesNotContain(
            sourceDirectory!,
            entryPoint!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_cuerpo_de_la_peticion_conserva_la_ruta_absoluta_del_ejecutable_que_el_organizador_eligio()
    {
        // La ruta que elige el organizador se guarda tal cual en el campo y es
        // la que se comprueba contra el disco: no se normaliza, no se acorta y
        // no se transforma antes de validar.
        var capture = new CapturingHandler();
        using var service = new HttpPackageCreationService("http://127.0.0.1:8710", capture);
        var picker = new FixedPathPicker(_sourceDirectory, _executable);
        var viewModel = new PackageCreateViewModel(service, picker)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
        };

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();

        // El campo conserva la ruta absoluta tal cual.
        Assert.Equal(_sourceDirectory, viewModel.SourceDirectory);
        Assert.Equal(_executable, viewModel.EntryPoint);

        // Y el payload convierte solo el ejecutable, no la carpeta.
        await viewModel.SubmitAsync();

        using var document = JsonDocument.Parse(capture.Body!);
        Assert.Equal(
            _sourceDirectory,
            document.RootElement.GetProperty("sourceDirectory").GetString());
        Assert.Equal(
            "game.exe",
            document.RootElement.GetProperty("entryPoint").GetString());
    }

    /// <summary>
    /// Publica el formulario contra un cliente HTTP que captura el cuerpo de
    /// la petición y devuelve una respuesta correcta.
    ///
    /// Se usa el cliente HTTP real (<see cref="HttpPackageCreationService"/>):
    /// el formato del payload es responsabilidad suya, asi que probarlo con un
    /// doble de la interfaz no probaria nada.
    /// </summary>
    private async Task<JsonElement> CapturePayloadAsync(
        string sourceDirectory,
        string executable,
        string? arguments = null)
    {
        var raw = await CaptureRawPayloadAsync(sourceDirectory, executable, arguments);

        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Igual que <see cref="CapturePayloadAsync"/>, pero devuelve el cuerpo en
    /// crudo: sirve para comprobar el JSON exacto y no solo sus campos.
    /// </summary>
    private async Task<string> CaptureRawPayloadAsync(
        string sourceDirectory,
        string executable,
        string? arguments = null)
    {
        var capture = new CapturingHandler();
        using var service = new HttpPackageCreationService("http://127.0.0.1:8710", capture);
        var picker = new FixedPathPicker(sourceDirectory, executable);
        var viewModel = new PackageCreateViewModel(service, picker)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
            Arguments = arguments ?? string.Empty,
        };

        // El organizador elige la carpeta y el ejecutable con los dialogos del
        // sistema operativo: aqui se simula lo que devolverian.
        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();

        Assert.Equal(sourceDirectory, viewModel.SourceDirectory);
        Assert.Equal(executable, viewModel.EntryPoint);

        await viewModel.SubmitAsync();

        Assert.False(viewModel.StatusIsError, viewModel.StatusMessage);
        Assert.NotNull(capture.Body);
        Assert.Equal(HttpStatusCode.Created, capture.StatusCode);

        return capture.Body!;
    }

    /// <summary>
    /// Selector de rutas que devuelve siempre las mismas rutas: el diálogo del
    /// sistema operativo no se puede abrir en las pruebas, pero lo que importa
    /// aqui es el payload que sale despues.
    /// </summary>
    private sealed class FixedPathPicker : IPathPicker
    {
        private readonly string _folder;
        private readonly string _executable;

        public FixedPathPicker(string folder, string executable)
        {
            _folder = folder;
            _executable = executable;
        }

        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_folder);

        public Task<string?> PickExecutableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_executable);
    }

    /// <summary>Manejador que guarda el cuerpo de la petición y responde 201.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        public HttpStatusCode StatusCode { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            StatusCode = HttpStatusCode.Created;

            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"packageId":"neon-drift","gameId":"neon-drift","title":"Neon Drift","version":"1.2.3","fileCount":1,"totalSizeBytes":2}""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return response;
        }
    }
}
