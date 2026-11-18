namespace LanForge.Server.Application;

/// <summary>El directorio origen de la creación del paquete no existe.</summary>
public sealed class PackageSourceNotFoundException : Exception
{
    public PackageSourceNotFoundException(string sourceDirectory)
        : base($"El directorio fuente no existe: '{sourceDirectory}'.")
    {
        SourceDirectory = sourceDirectory;
    }

    public string SourceDirectory { get; }
}
