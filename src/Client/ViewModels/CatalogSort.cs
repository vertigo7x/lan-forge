using LanForge.Shared.Models;

namespace LanForge.Client.ViewModels;

/// <summary>Orden del catalogo que elige el jugador con el boton de ordenar.</summary>
public enum CatalogSortOrder
{
    NameAscending,
    NameDescending,
}

/// <summary>Ordena las entradas del catalogo. Logica pura para poder testearla.</summary>
public static class CatalogSort
{
    public static IReadOnlyList<CatalogEntry> Apply(IEnumerable<CatalogEntry> entries, CatalogSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var ordered = order == CatalogSortOrder.NameDescending
            ? entries.OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase)
            : entries.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase);
        return ordered.ToList();
    }
}
