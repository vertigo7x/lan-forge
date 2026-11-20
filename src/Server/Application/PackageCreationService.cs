namespace LanForge.Server.Application;

/// <summary>
/// Claves de los campos del formulario de creación de paquetes.
/// </summary>
public static class PackageCreationFields
{
    public const string GameId = "gameId";
    public const string Title = "title";
    public const string Version = "version";
    public const string SourceDirectory = "sourceDirectory";
    public const string EntryPoint = "entryPoint";
    public const string Arguments = "arguments";
    public const string CoverPath = "coverPath";
}

/// <summary>Datos que rellena el organizador para publicar un juego portable.</summary>
public sealed record PackageCreationRequest(
    string GameId,
    string Title,
    string Version,
    string SourceDirectory,
    string EntryPoint,
    string? Arguments = null);

/// <summary>Resumen del paquete creado, tal y como lo devuelve el servidor.</summary>
public sealed record PackageCreationSummary(
    string PackageId,
    string GameId,
    string Title,
    string Version,
    int FileCount,
    long TotalSizeBytes);

/// <summary>
/// Resultado de crear un paquete. Los errores por campo van indexados por
/// <see cref="PackageCreationFields"/> para que la UI los muestre junto al campo.
/// </summary>
public sealed record PackageCreationResult(
    bool Succeeded,
    PackageCreationSummary? Package,
    IReadOnlyDictionary<string, string> FieldErrors,
    string? ErrorMessage)
{
    public static PackageCreationResult Success(PackageCreationSummary package) =>
        new(true, package, new Dictionary<string, string>(), null);

    public static PackageCreationResult Invalid(IReadOnlyDictionary<string, string> fieldErrors, string? message = null) =>
        new(false, null, fieldErrors, message ?? "Revisa los campos marcados.");

    public static PackageCreationResult Failure(string message) =>
        new(false, null, new Dictionary<string, string>(), message);
}

/// <summary>
/// Capa de servicio del formulario de creación de paquetes.
/// </summary>
public interface IPackageCreationService
{
    Task<PackageCreationResult> CreateAsync(PackageCreationRequest request, CancellationToken cancellationToken = default);
}
