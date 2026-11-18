namespace LanForge.Shared.Models;

/// <summary>
/// Respuesta de error estructurada de la API: código estable, mensaje legible y
/// el detalle por campo (lista vacía en errores que no son de validación).
/// </summary>
public sealed record ApiErrorResponse(
    string Code,
    string Message,
    IReadOnlyList<ApiFieldError> Errors);
