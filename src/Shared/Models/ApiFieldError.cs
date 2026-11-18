namespace LanForge.Shared.Models;

/// <summary>Error de validación de un campo concreto de la solicitud.</summary>
public sealed record ApiFieldError(string Field, string Code, string Message);
