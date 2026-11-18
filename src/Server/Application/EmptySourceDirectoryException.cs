namespace LanForge.Server.Application;

/// <summary>
/// El directorio origen del paquete no contiene ningún archivo.
/// Hereda de <see cref="InvalidOperationException"/> para no romper a quien la
/// capturaba como error genérico de publicación.
/// </summary>
public sealed class EmptySourceDirectoryException : InvalidOperationException
{
    public EmptySourceDirectoryException(string sourceDirectory)
        : base($"El directorio fuente no contiene archivos: '{sourceDirectory}'.")
    {
        SourceDirectory = sourceDirectory;
    }

    public string SourceDirectory { get; }
}
