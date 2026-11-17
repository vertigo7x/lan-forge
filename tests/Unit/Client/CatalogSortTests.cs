using LanForge.Client.ViewModels;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Client;

public sealed class CatalogSortTests
{
    private static readonly CatalogEntry Drift = new("b", "Neon Drift", "b", "1.0.0", 1, 1);
    private static readonly CatalogEntry Arena = new("a", "arena", "a", "1.0.0", 1, 1);
    private static readonly CatalogEntry Pixel = new("c", "Pixel Pirates", "c", "1.0.0", 1, 1);

    [Fact]
    public void Orden_ascendente_ignora_mayusculas_y_acentos_no_afectan_al_orden()
    {
        var result = CatalogSort.Apply([Drift, Arena, Pixel], CatalogSortOrder.NameAscending);
        Assert.Equal(["arena", "Neon Drift", "Pixel Pirates"], result.Select(e => e.Title));
    }

    [Fact]
    public void Orden_descendente_es_el_reves()
    {
        var result = CatalogSort.Apply([Drift, Arena, Pixel], CatalogSortOrder.NameDescending);
        Assert.Equal(["Pixel Pirates", "Neon Drift", "arena"], result.Select(e => e.Title));
    }
}
