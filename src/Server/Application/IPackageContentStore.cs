namespace LanForge.Server.Application;

/// <summary>Puerto de almacenamiento del contenido de los paquetes (un .zip por paquete).</summary>
public interface IPackageContentStore
{
    /// <summary>Comprime <paramref name="sourceDirectory"/> y lo guarda como el paquete activo de <paramref name="packageId"/> (reemplazando el anterior).</summary>
    Task ImportArchiveAsync(string packageId, string sourceDirectory, CancellationToken cancellationToken = default);

    /// <summary>Abre el .zip del paquete para descarga completa (con Range).</summary>
    Task<Stream?> OpenArchiveAsync(string packageId, CancellationToken cancellationToken = default);

    /// <summary>Abre un archivo concreto dentro del .zip (descarga por archivo, compatibilidad).</summary>
    Task<Stream?> OpenReadAsync(string packageId, string relativePath, CancellationToken cancellationToken = default);
}
