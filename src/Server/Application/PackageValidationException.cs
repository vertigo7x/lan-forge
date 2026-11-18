using LanForge.Shared.Models;

namespace LanForge.Server.Application;

/// <summary>
/// Error de aplicación con errores estructurados por campo para una solicitud de creación inválida.
/// </summary>
public sealed class PackageValidationException : Exception
{
    public PackageValidationException(IReadOnlyList<ApiFieldError> errors)
        : base("La solicitud no cumple las validaciones del paquete.")
    {
        Errors = errors;
    }

    public IReadOnlyList<ApiFieldError> Errors { get; }
}
