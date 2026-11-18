using System.Net;
using System.Net.Http.Json;
using LanForge.Shared;
using LanForge.Shared.Models;

namespace LanForge.Server.Application;

/// <summary>
/// Cliente HTTP real del endpoint de creación de paquetes (POST /api/v1/packages).
///
/// Sustituye a la implementación simulada: el formulario de la UI del organizador
/// habla con el mismo proceso que aloja el host web, así que la llamada sale por
/// la red contra la ruta real y los errores por campo que devuelve el servidor se
/// mapean tal cual a los campos del formulario.
///
/// Contrato del endpoint (ver <c>contrato-api-creacion-paquetes.md</c>):
/// <list type="bullet">
/// <item>201: <see cref="CreatePackageResponse"/> con el paquete creado.</item>
/// <item>400: <see cref="ApiErrorResponse"/> con el detalle por campo.</item>
/// <item>409: <see cref="ApiErrorResponse"/> sin detalle por campo (versión duplicada).</item>
/// </list>
/// </summary>
public sealed class HttpPackageCreationService : IPackageCreationService, IDisposable
{
    /// <summary>Ruta del endpoint de creación de paquetes.</summary>
    public const string PackagesPath = "/api/v1/packages";

    /// <summary>
    /// Identificador de cliente que se envia en la cabecera de correlacion. La UI del
    /// organizador actua como cliente del propio servidor.
    /// </summary>
    public const string ClientId = "organizer-ui";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Func<string>? _baseUrlFactory;

    /// <summary>
    /// Crea el cliente contra un host concreto.
    /// </summary>
    /// <param name="baseUrl">Direccion base del servidor (por ejemplo <c>http://127.0.0.1:8710</c>).</param>
    /// <param name="handler">Manejador de HTTP. Si se indica, el cliente no lo es disposable.</param>
    /// <param name="timeout">Tiempo maximo de espera de la respuesta.</param>
    public HttpPackageCreationService(
        string baseUrl,
        HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ownsHttpClient = handler is null;
        _http.BaseAddress = new Uri(NormalizeBaseUrl(baseUrl));
        _http.Timeout = timeout ?? DefaultTimeout;
        _http.DefaultRequestHeaders.Add("X-LanForge-Client-Id", ClientId);
    }

    /// <summary>
    /// Crea el cliente cuya direccion base se resuelve en el primer uso. Hace falta
    /// cuando el puerto todavia no se sabe al registrar el servicio: el sistema puede
    /// asignar uno libre cuando el puerto configurado es 0.
    /// </summary>
    /// <param name="baseUrlFactory">Fabrica de la direccion base del servidor.</param>
    /// <param name="handler">Manejador de HTTP. Si se indica, el cliente no lo es disposable.</param>
    /// <param name="timeout">Tiempo maximo de espera de la respuesta.</param>
    public HttpPackageCreationService(
        Func<string> baseUrlFactory,
        HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrlFactory);

        _baseUrlFactory = baseUrlFactory;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ownsHttpClient = handler is null;
        _http.Timeout = timeout ?? DefaultTimeout;
        _http.DefaultRequestHeaders.Add("X-LanForge-Client-Id", ClientId);
    }

    /// <summary>
    /// Crea el cliente reutilizando un <see cref="HttpClient"/> ya configurado (por
    /// ejemplo el registrado en el contenedor de servicios de la aplicacion).
    /// </summary>
    public HttpPackageCreationService(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        _ownsHttpClient = false;
        if (_http.BaseAddress is null)
        {
            throw new ArgumentException(
                "El HttpClient debe tener una direccion base para llamar al endpoint de paquetes.",
                nameof(http));
        }
    }

    /// <summary>
    /// Envia la solicitud de creación al endpoint y traduce la respuesta al
    /// resultado que la UI entiende.
    /// </summary>
    public async Task<PackageCreationResult> CreateAsync(
        PackageCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureBaseAddress();

        var payload = new CreatePackageRequest
        {
            GameId = request.GameId,
            Title = request.Title,
            Version = request.Version,
            SourceDirectory = request.SourceDirectory,
            EntryPoint = request.EntryPoint,
            Arguments = request.Arguments,
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, PackagesPath)
        {
            Content = JsonContent.Create(payload, options: SharedJson.Options),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Red caida, servidor inaccesible o DNS: mensaje generico para el usuario.
            return PackageCreationResult.Failure(
                $"No se ha podido contactar con el servidor: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Tiempo de espera agotado: no es una cancelacion del usuario.
            return PackageCreationResult.Failure(
                $"El servidor no ha respondido a tiempo ({ex.Message}).");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var created = await response.Content
                    .ReadFromJsonAsync<CreatePackageResponse>(SharedJson.Options, cancellationToken)
                    .ConfigureAwait(false);

                if (created is null)
                {
                    return PackageCreationResult.Failure(
                        "El servidor ha devuelto una respuesta vacia al crear el paquete.");
                }

                return PackageCreationResult.Success(new PackageCreationSummary(
                    PackageId: created.PackageId,
                    GameId: created.GameId,
                    Title: created.Title,
                    Version: created.Version,
                    FileCount: created.FileCount,
                    TotalSizeBytes: created.TotalSizeBytes));
            }

            var error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            return error ?? PackageCreationResult.Failure(GenericErrorMessage(response.StatusCode));
        }
    }

    /// <summary>
    /// Fija la direccion base del cliente si venia de una fabrica: el puerto puede
    /// no conocerse hasta que el host arranca.
    /// </summary>
    private void EnsureBaseAddress()
    {
        if (_baseUrlFactory is null)
        {
            return;
        }

        if (_http.BaseAddress is not null)
        {
            return;
        }

        _http.BaseAddress = new Uri(NormalizeBaseUrl(_baseUrlFactory()));
    }

    /// <summary>
    /// Traduce el cuerpo de error del servidor a un resultado con los errores por
    /// campo. Devuelve null si el cuerpo no trae una estructura de error conocida.
    /// </summary>
    private static async Task<PackageCreationResult?> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ApiErrorResponse? error;
        try
        {
            error = await response.Content
                .ReadFromJsonAsync<ApiErrorResponse>(SharedJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            // El servidor no devolvio JSON: se cae al mensaje generico.
            return null;
        }

        if (error is null)
        {
            return null;
        }

        var fieldErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fieldError in error.Errors)
        {
            // El campo "body" no corresponde a ningun campo del formulario: se
            // descarta para que el mensaje general muestre el problema.
            if (string.IsNullOrWhiteSpace(fieldError.Field) ||
                fieldError.Field == "body" ||
                string.IsNullOrWhiteSpace(fieldError.Message))
            {
                continue;
            }

            // El primer error de un campo manda: es el que ve el usuario junto al
            // campo, y no tiene sentido acumular mensajes contradictorios.
            fieldErrors.TryAdd(fieldError.Field, fieldError.Message);
        }

        var message = string.IsNullOrWhiteSpace(error.Message)
            ? GenericErrorMessage(response.StatusCode)
            : error.Message;

        return fieldErrors.Count > 0
            ? PackageCreationResult.Invalid(fieldErrors, message)
            : PackageCreationResult.Failure(message);
    }

    /// <summary>Mensaje generico para fallos que no se pueden atribuir a un campo.</summary>
    private static string GenericErrorMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Conflict =>
            "El servidor ha indicado un conflicto al crear el paquete. Revisa la version indicada.",
        HttpStatusCode.BadRequest =>
            "El servidor ha rechazado los datos del formulario.",
        HttpStatusCode.InternalServerError =>
            "El servidor ha encontrado un error al crear el paquete. Intentalo de nuevo mas tarde.",
        _ => $"El servidor ha devuelto un error inesperado ({(int)statusCode}).",
    };

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        return normalized;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
