using LanForge.Shared.Manifest;

namespace LanForge.Server.Application;

/// <summary>
/// Reglas de las rutas que el organizador elige en el formulario de creación de
/// paquetes. Son reglas puras (no dependen del sistema de ficheros ni de la UI)
/// para que se puedan probar sin levantar la aplicación.
///
/// El formulario trabaja con rutas absolutas (son las que devuelve el dialogo del
/// sistema operativo), pero el payload del endpoint lleva la ruta del ejecutable
/// relativa a la carpeta origen: es lo que el servidor espera y lo que el cliente
/// resuelve contra el directorio de instalación del juego.
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
                "El ejecutable tiene que estar dentro de la carpeta origen.",
                nameof(executablePath));
        }

        var relative = executable[sourceRoot.Length..]
            .Replace('\\', '/');

        if (relative.Length == 0)
        {
            throw new ArgumentException(
                "El ejecutable tiene que estar dentro de la carpeta origen.",
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
