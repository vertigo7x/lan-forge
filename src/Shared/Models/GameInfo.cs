namespace LanForge.Shared.Models;

/// <summary>Juego conocido por el catálogo.</summary>
public sealed record GameInfo(string GameId, string Title, string? Description = null);
