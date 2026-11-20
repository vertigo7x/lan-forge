using LanForge.Shared.Manifest;

namespace LanForge.Server.Application;

/// <summary>
/// Reglas de las rutas que el organizador elige en el formulario de creación de
/// paquetes. Son reglas puras (no dependen del sistema de ficheros ni de la UI)
/// para que se puedan probar sin levantar la aplicación.
///
/// El formulario trabaja con rutas absolutas (son las que devuelve el dialogo del
/// sistema operativo), pero la publicación lleva la ruta del ejecutable relativa a
/// la carpeta origen: es lo que el manifiesto espera y lo que el cliente resuelve
/// contra el directorio de instalación del juego.
/// </summary>
public static class PackageSourcePaths
{
    /// <summary>Extensión de los ejecutables de Windows que admite el formulario.</summary>
    public const string ExecutableExtension = ".exe";

    /// <summary>
    /// Patrón del filtro del diálogo de archivos: solo ejecutables de Windows.
    /// Lo usa el diálogo nativo del sistema operativo, que lo aplica como
    /// comodín del nombre de archivo.
    /// </summary>
    public const string ExecutablePattern = "*" + ExecutableExtension;

    /// <summary>
    /// Indica si la ruta es un ejecutable: tiene que terminar en <c>.exe</c>.
    /// La comparacion no distingue mayusculas porque Windows no las distingue.
    /// </summary>
    public static bool IsExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Comprueba que la carpeta origen y el ejecutable son utilizables y devuelve
    /// el motivo por el que no lo son.
    ///
    /// Es la regla unica que comparten el boton de publicar (que se deshabilita)
    /// y los mensajes que se muestran junto a cada campo. Vive aqui para que el
    /// boton y el texto no puedan divergir: si el boton se deshabilita, el campo
    /// dice por que, y siempre con la misma redaccion.
    ///
    /// Devuelve null cuando las dos rutas son utilizables.
    /// </summary>
    public static string? Validate(string? sourceDirectory, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return MissingSourceDirectoryMessage;
        }

        if (!Directory.Exists(sourceDirectory))
        {
            return MissingSourceDirectoryMessage;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return MissingExecutableMessage;
        }

        // La extension se comprueba antes que la existencia: un archivo que no
        // es un .exe no vale como ejecutable aunque exista.
        if (!IsExecutable(executablePath))
        {
            return NotExecutableMessage;
        }

        if (!File.Exists(executablePath))
        {
            return MissingExecutableMessage;
        }

        // El ejecutable tiene que estar dentro de la carpeta origen: es lo que
        // convierte la ruta absoluta en la ruta relativa que espera el payload.
        // Sin esta comprobacion el boton se habilita y el envio falla despues
        // con un error que el organizador no puede entender.
        if (!IsInsideDirectory(sourceDirectory, executablePath))
        {
            return ExecutableOutsideSourceDirectoryMessage;
        }

        return null;
    }

    /// <summary>
    /// Indica si la ruta esta dentro del directorio indicado. La comparacion no
    /// distingue mayusculas porque Windows no las distingue, y las dos rutas se
    /// normalizan (separadores '/' y sin barra final) antes de compararlas.
    /// </summary>
    public static bool IsInsideDirectory(string directory, string path)
    {
        var root = NormalizeRoot(directory);
        var candidate = NormalizePath(path);

        return root.Length > 0 &&
               candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
               candidate.Length > root.Length;
    }

    /// <summary>
    /// Mensaje de la carpeta origen que no existe. No lleva la ruta porque la
    /// ruta ya se ve en el campo de al lado.
    /// </summary>
    public const string MissingSourceDirectoryMessage = "La carpeta origen no existe.";

    /// <summary>Message del ejecutable que no existe.</summary>
    public const string MissingExecutableMessage = "El ejecutable no existe.";

    /// <summary>
    /// Mensaje del archivo que no es un ejecutable de Windows. La extension
    /// admitida sale de <see cref="ExecutableExtension"/> para que el texto y la
    /// regla no puedan divergir.
    /// </summary>
    public static string NotExecutableMessage =>
        $"El ejecutable tiene que ser un archivo '{ExecutableExtension}'.";

    /// <summary>
    /// Mensaje del ejecutable que esta fuera de la carpeta origen. El ejecutable
    /// forma parte del paquete, asi que tiene que vivir dentro de la carpeta que
    /// se publica.
    /// </summary>
    public const string ExecutableOutsideSourceDirectoryMessage =
        "El ejecutable tiene que estar dentro de la carpeta origen.";

    /// <summary>
    /// Convierte la ruta absoluta del ejecutable en la ruta relativa que espera el
    /// payload (separadores <c>/</c>, sin ruta inicial ni <c>..</c>).
    ///
    /// Lanza <see cref="ArgumentException"/> si el ejecutable no esta dentro de la
    /// carpeta origen, si no termina en <c>.exe</c> o si alguna de las dos rutas no
    /// es utilizable.
    /// </summary>
    public static string ToPackageRelativePath(string sourceDirectory, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException(
                "La carpeta origen es obligatoria para calcular la ruta del ejecutable.",
                nameof(sourceDirectory));
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException(
                "El ejecutable es obligatorio para calcular la ruta del ejecutable.",
                nameof(executablePath));
        }

        if (!IsExecutable(executablePath))
        {
            throw new ArgumentException(
                $"El ejecutable tiene que terminar en '{ExecutableExtension}'.",
                nameof(executablePath));
        }

        var sourceRoot = NormalizeRoot(sourceDirectory);
        var executable = NormalizePath(executablePath);

        if (!executable.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                ExecutableOutsideSourceDirectoryMessage,
                nameof(executablePath));
        }

        var relative = executable[sourceRoot.Length..]
            .Replace('\\', '/');

        if (relative.Length == 0)
        {
            throw new ArgumentException(
                ExecutableOutsideSourceDirectoryMessage,
                nameof(executablePath));
        }

        // La ruta relativa se normaliza con las reglas del manifiesto: sin ruta
        // inicial, sin '..' y con separadores '/'.
        return ManifestFileEntry.ValidatePath(relative);
    }

    /// <summary>
    /// Normaliza una ruta para poder compararla: separadores '/' y sin barra
    /// final. La raiz de Windows ("C:") conserva su barra final para que la
    /// comparacion no confunda "C:/Juegos2" con una subcarpeta de "C:/Juegos".
    /// </summary>
    private static string NormalizeRoot(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Length == 0 ? normalized : normalized + "/";
    }

    /// <summary>
    /// Normaliza una ruta para poder compararla: separadores '/' y sin barra final.
    /// </summary>
    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
