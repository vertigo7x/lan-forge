using LanForge.Server.Domain;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;

namespace LanForge.Server.Application;

/// <summary>
/// Validación del formulario de creación de paquetes. Reutiliza los validadores del
/// dominio (GameId, PackageVersion, ManifestFileEntry) para que el cliente y el
/// servidor apliquen exactamente las mismas reglas.
/// </summary>
public static class PackageCreationValidator
{
    /// <summary>Límite del título. Guarda del lado del cliente, no lo impone el servidor.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>Límite de los argumentos de lanzamiento. Guarda del lado del cliente.</summary>
    public const int MaxArgumentsLength = 1024;

    /// <summary>Límite de la ruta de la carpeta origen. Guarda del lado del cliente.</summary>
    public const int MaxSourceDirectoryLength = 4096;

    /// <summary>
    /// Valida la petición y devuelve los errores por campo. El diccionario está vacío
    /// cuando la petición es válida.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(PackageCreationRequest request)
    {
        var errors = new Dictionary<string, string>();

        ValidateGameId(request.GameId, errors);
        ValidateTitle(request.Title, errors);
        ValidateVersion(request.Version, errors);
        ValidateSourceDirectory(request.SourceDirectory, errors);
        ValidateEntryPoint(request.EntryPoint, errors);
        ValidateArguments(request.Arguments, errors);

        return errors;
    }

    private static void ValidateGameId(string gameId, IDictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            errors[PackageCreationFields.GameId] = "El id del juego es obligatorio.";
            return;
        }

        if (!GameId.IsValid(gameId))
        {
            errors[PackageCreationFields.GameId] =
                $"Id de juego inválido: usa de 1 a {GameId.MaxLength} caracteres [a-zA-Z0-9_-].";
        }
    }

    private static void ValidateTitle(string title, IDictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors[PackageCreationFields.Title] = "El título es obligatorio.";
            return;
        }

        if (title.Length > MaxTitleLength)
        {
            errors[PackageCreationFields.Title] = $"El título no puede pasar de {MaxTitleLength} caracteres.";
        }
    }

    private static void ValidateVersion(string version, IDictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            errors[PackageCreationFields.Version] = "La versión es obligatoria.";
            return;
        }

        if (!PackageVersion.IsValid(version))
        {
            errors[PackageCreationFields.Version] =
                $"Versión inválida: usa el formato 1.0.0 (hasta {PackageVersion.MaxLength} caracteres).";
        }
    }

    private static void ValidateSourceDirectory(string sourceDirectory, IDictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            errors[PackageCreationFields.SourceDirectory] = "Indica la carpeta del juego portable.";
            return;
        }

        if (sourceDirectory.Length > MaxSourceDirectoryLength)
        {
            errors[PackageCreationFields.SourceDirectory] =
                $"La ruta de la carpeta no puede pasar de {MaxSourceDirectoryLength} caracteres.";
            return;
        }

        // La carpeta se elige con el dialogo del sistema operativo, asi que ya
        // existe en el momento de elegirla. Se vuelve a comprobar aqui porque el
        // campo puede guardar la ruta de una eleccion anterior y el organizador
        // puede moverla o borrarla antes de publicar.
        if (!Directory.Exists(sourceDirectory))
        {
            errors[PackageCreationFields.SourceDirectory] =
                $"La carpeta origen no existe: '{sourceDirectory}'.";
        }
    }

    private static void ValidateEntryPoint(string entryPoint, IDictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            errors[PackageCreationFields.EntryPoint] = "Indica el ejecutable del juego.";
            return;
        }

        // La extension se comprueba antes que la existencia: un archivo que no es
        // un .exe no vale como ejecutable aunque exista.
        if (!PackageSourcePaths.IsExecutable(entryPoint))
        {
            errors[PackageCreationFields.EntryPoint] =
                $"El ejecutable tiene que ser un archivo '{PackageSourcePaths.ExecutableExtension}'.";
            return;
        }

        // El ejecutable se elige con el dialogo de archivos, asi que existe cuando
        // se elige. Se comprueba aqui porque la ruta guardada puede dejar de ser
        // valida antes de publicar (el organizador puede moverla o borrarla).
        if (!File.Exists(entryPoint))
        {
            errors[PackageCreationFields.EntryPoint] =
                $"El ejecutable no existe: '{entryPoint}'.";
        }
    }

    private static void ValidateArguments(string? arguments, IDictionary<string, string> errors)
    {
        if (arguments is null)
        {
            return;
        }

        if (arguments.Length > MaxArgumentsLength)
        {
            errors[PackageCreationFields.Arguments] =
                $"Los argumentos no pueden pasar de {MaxArgumentsLength} caracteres.";
        }
    }
}
