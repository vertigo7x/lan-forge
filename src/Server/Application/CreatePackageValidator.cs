using LanForge.Server.Domain;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;

namespace LanForge.Server.Application;

/// <summary>
/// Validaciones de la solicitud de creación de un paquete (POST /api/v1/packages).
/// Devuelve un error por campo incumplido para que la API responda 400 con el detalle.
/// </summary>
public static class CreatePackageValidator
{
    /// <summary>Límite del título del juego.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Límite de los argumentos de arranque.</summary>
    public const int MaxArgumentsLength = 1024;

    /// <summary>Valida la solicitud y devuelve los errores por campo (vacío si es válida).</summary>
    public static IReadOnlyList<ApiFieldError> Validate(CreatePackageRequest? request)
    {
        var errors = new List<ApiFieldError>();
        if (request is null)
        {
            errors.Add(new ApiFieldError("body", "required", "El cuerpo de la solicitud es obligatorio."));
            return errors;
        }

        ValidateGameId(request.GameId, errors);
        ValidateTitle(request.Title, errors);
        ValidateVersion(request.Version, errors);
        ValidateSourceDirectory(request.SourceDirectory, errors);
        ValidateEntryPoint(request.EntryPoint, errors);
        ValidateArguments(request.Arguments, errors);
        return errors;
    }

    private static void ValidateGameId(string? gameId, List<ApiFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            errors.Add(new ApiFieldError("gameId", "required", "El identificador del juego es obligatorio."));
            return;
        }

        if (gameId.Length > GameId.MaxLength)
        {
            errors.Add(new ApiFieldError(
                "gameId", "too_long",
                $"El identificador del juego no puede superar {GameId.MaxLength} caracteres."));
            return;
        }

        if (!GameId.IsValid(gameId))
        {
            errors.Add(new ApiFieldError(
                "gameId", "invalid_format",
                $"El identificador del juego solo admite 1-{GameId.MaxLength} caracteres [a-zA-Z0-9_-]."));
        }
    }

    private static void ValidateTitle(string? title, List<ApiFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(new ApiFieldError("title", "required", "El título del juego es obligatorio."));
            return;
        }

        if (title.Length > MaxTitleLength)
        {
            errors.Add(new ApiFieldError(
                "title", "too_long",
                $"El título del juego no puede superar {MaxTitleLength} caracteres."));
        }
    }

    private static void ValidateVersion(string? version, List<ApiFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            errors.Add(new ApiFieldError("version", "required", "La versión del paquete es obligatoria."));
            return;
        }

        if (version.Length > PackageVersion.MaxLength)
        {
            errors.Add(new ApiFieldError(
                "version", "too_long",
                $"La versión del paquete no puede superar {PackageVersion.MaxLength} caracteres."));
            return;
        }

        if (!PackageVersion.IsValid(version))
        {
            errors.Add(new ApiFieldError(
                "version", "invalid_format",
                "La versión del paquete debe cumplir el formato N[.N[.N[.N]]][-pre-release]."));
        }
    }

    private static void ValidateSourceDirectory(string? sourceDirectory, List<ApiFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            errors.Add(new ApiFieldError("sourceDirectory", "required", "El directorio origen del paquete es obligatorio."));
            return;
        }

        if (!LooksAbsolute(sourceDirectory))
        {
            errors.Add(new ApiFieldError(
                "sourceDirectory", "invalid_path",
                "El directorio origen debe ser una ruta absoluta del servidor."));
        }
    }

    private static void ValidateEntryPoint(string? entryPoint, List<ApiFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            errors.Add(new ApiFieldError("entryPoint", "required", "El punto de entrada del paquete es obligatorio."));
            return;
        }

        try
        {
            ManifestFileEntry.ValidatePath(entryPoint);
        }
        catch (ArgumentException)
        {
            errors.Add(new ApiFieldError(
                "entryPoint", "invalid_path",
                "El punto de entrada debe ser una ruta relativa segura dentro del paquete."));
        }
    }

    private static void ValidateArguments(string? arguments, List<ApiFieldError> errors)
    {
        if (arguments is not null && arguments.Length > MaxArgumentsLength)
        {
            errors.Add(new ApiFieldError(
                "arguments", "too_long",
                $"Los argumentos de arranque no pueden superar {MaxArgumentsLength} caracteres."));
        }
    }

    /// <summary>
    /// Detecta rutas absolutas por patrón (raíz de Unix, UNC o unidad con letra),
    /// para que la comprobación no dependa del sistema de ficheros del host.
    /// </summary>
    private static bool LooksAbsolute(string path) =>
        path.StartsWith('/') ||
        path.StartsWith('\\') ||
        (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':');
}
