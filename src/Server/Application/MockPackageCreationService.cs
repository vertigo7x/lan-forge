namespace LanForge.Server.Application;

/// <summary>
/// Implementación simulada de <see cref="IPackageCreationService"/>. No habla con
/// ningún endpoint: reproduce el comportamiento observable del servidor real
/// (latencia, éxito y errores de validación por campo) para que el formulario
/// pueda probarse de forma aislada.
///
/// El contrato es idéntico al que cumplirá el cliente HTTP real, así que
/// sustituir esta implementación por la real no obliga a tocar la vista ni el
/// view model: ambos dependen solo de <see cref="IPackageCreationService"/>.
/// </summary>
public sealed class MockPackageCreationService : IPackageCreationService
{
    /// <summary>Latencia por defecto que simula el servicio: 400 ms.</summary>
    public static readonly TimeSpan DefaultLatency = TimeSpan.FromMilliseconds(400);

    private readonly TimeSpan _latency;
    private readonly Func<PackageCreationRequest, PackageCreationResult> _behaviour;
    private readonly List<PackageCreationRequest> _requests = [];

    /// <summary>
    /// Crea un mock que simula el servicio real: devuelve éxito para una petición
    /// válida y un error de validación por campo para una petición inválida.
    /// </summary>
    /// <param name="latency">Retardo simulado antes de responder.</param>
    public MockPackageCreationService(TimeSpan? latency = null)
        : this(latency ?? DefaultLatency, ValidateThenSucceed)
    {
    }

    /// <summary>
    /// Crea un mock con un comportamiento propio, útil para forzar un error concreto
    /// en una prueba o en una pantalla de demostración.
    /// </summary>
    /// <param name="latency">Retardo simulado antes de responder.</param>
    /// <param name="behaviour">Función que decide la respuesta a partir de la petición.</param>
    public MockPackageCreationService(
        TimeSpan latency,
        Func<PackageCreationRequest, PackageCreationResult> behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        if (latency < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(latency), latency, "La latencia no puede ser negativa.");
        }

        _latency = latency;
        _behaviour = behaviour;
    }

    /// <summary>Peticiones recibidas por el mock, en orden de llegada.</summary>
    public IReadOnlyList<PackageCreationRequest> Requests => _requests;

    /// <summary>True mientras el mock está simulando la respuesta del servidor.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Simula la creación de un paquete: espera la latencia configurada y devuelve
    /// el resultado decidido por el comportamiento del mock.
    /// </summary>
    public async Task<PackageCreationResult> CreateAsync(
        PackageCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);
        IsBusy = true;
        try
        {
            await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);
            return _behaviour(request);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Comportamiento por defecto: aplica las validaciones del formulario y, si
    /// todas pasan, devuelve el paquete creado. Es el mismo criterio que aplica
    /// el servidor, por lo que el formulario ejercita los dos caminos (éxito y
    /// error de validación por campo) sin tocar la red.
    /// </summary>
    private static PackageCreationResult ValidateThenSucceed(PackageCreationRequest request)
    {
        var fieldErrors = PackageCreationValidator.Validate(request);
        if (fieldErrors.Count > 0)
        {
            return PackageCreationResult.Invalid(fieldErrors);
        }

        // El resumen simula un paquete con un archivo y tamaño 0: la vista solo
        // lo usa para el mensaje de exito, no para calcular nada.
        return PackageCreationResult.Success(new PackageCreationSummary(
            PackageId: request.GameId,
            GameId: request.GameId,
            Title: request.Title,
            Version: request.Version,
            FileCount: 1,
            TotalSizeBytes: 0));
    }
}
