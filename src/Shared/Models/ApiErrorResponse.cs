namespace LanForge.Shared.Models;

/// <summary>Error de validación de un campo concreto de la solicitud.</summary>
public sealed record ApiFieldError(string Field, string Code, string Message);

/// <summary>
/// Respuesta de error estructurada de la API: código estable, mensaje legible y
/// el detalle por campo (vacío en errores que no son de validación).
/// </summary>
public sealed record ApiErrorResponse(
    string Code,
    string Message,
    IReadOnlyList<ApiFieldError> Errors);
