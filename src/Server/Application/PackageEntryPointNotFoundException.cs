namespace LanForge.Server.Application;

/// <summary>El punto de entrada indicado no existe entre los archivos del paquete.</summary>
public sealed class PackageEntryPointNotFoundException : Exception
{
    public PackageEntryPointNotFoundException(string entryPoint)
        : base($"El punto de entrada '{entryPoint}' no existe en la lista de archivos.")
    {
        EntryPoint = entryPoint;
    }

    public string EntryPoint { get; }

    public string Field => "entryPoint";
}
