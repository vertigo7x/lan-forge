using System.Text.RegularExpressions;

namespace LanForge.Server.Domain;

/// <summary>Validación del identificador de juego (slug seguro, también usado como nombre de directorio).</summary>
public static class GameId
{
    public const int MaxLength = 64;

    private static readonly Regex Pattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxLength && Pattern.IsMatch(value);

    public static string Validate(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"GameId inválido: '{value}'. Use 1-{MaxLength} caracteres [a-zA-Z0-9_-].",
                nameof(value));
        }

        return value;
    }
}

public sealed record Game
{
    public string Id { get; }
    public string Title { get; init; }
    public string? Description { get; init; }

    /// <summary>Nombre del archivo de portada en el directorio de covers del servidor; null si no tiene.</summary>
    public string? CoverFile { get; init; }

    public Game(string id, string title, string? description = null, string? coverFile = null)
    {
        Id = GameId.Validate(id);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("El título del juego no puede estar vacío.", nameof(title));
        }

        Title = title;
        Description = description;
        CoverFile = coverFile;
    }
}
