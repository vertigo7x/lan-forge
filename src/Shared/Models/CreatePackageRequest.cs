namespace LanForge.Shared.Models;

/// <summary>
/// Solicitud de creación de un paquete (POST /api/v1/packages).
/// Los campos son opcionales en el tipo para que el servidor pueda validar
/// cada uno por separado y devolver un error estructurado por campo.
/// </summary>
public sealed record CreatePackageRequest
{
    /// <summary>Identificador del juego (slug). Se valida con las reglas de GameId.</summary>
    public string? GameId { get; init; }

    /// <summary>Título del juego. Obligatorio.</summary>
    public string? Title { get; init; }

    /// <summary>Versión del paquete (semver-lite). Se valida con las reglas de PackageVersion.</summary>
    public string? Version { get; init; }

    /// <summary>Directorio del servidor con los archivos del juego. Obligatorio.</summary>
    public string? SourceDirectory { get; init; }

    /// <summary>Ruta relativa del ejecutable dentro del paquete. Obligatoria.</summary>
    public string? EntryPoint { get; init; }

    /// <summary>Argumentos de arranque. Opcionales.</summary>
    public string? Arguments { get; init; }
}
