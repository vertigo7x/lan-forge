using LanForge.Server.Domain;

namespace LanForge.Server.ViewModels;

/// <summary>
/// Cache de las filas del catalogo del organizador. El listado se refresca cada 2
/// segundos; si en cada refresco se crearan filas nuevas, el boton de borrar perderia
/// su confirmacion (la guarda en la propia fila) y el organizador tendria que pulsar
/// dos veces seguidas en menos de 2 segundos. Reutilizando la fila, la confirmacion
/// sobrevive al refresco.
/// </summary>
public sealed class CatalogRowCache
{
    private readonly Dictionary<string, CatalogRow> _rows = new(StringComparer.Ordinal);

    /// <summary>
    /// Devuelve las filas del catalogo para los paquetes dados, reutilizando las filas
    /// que ya existen. Las filas de los juegos que ya no estan en el catalogo se
    /// descartan: una fila borrada no debe resucitar en el siguiente refresco.
    /// </summary>
    public CatalogRow[] Build(IReadOnlyList<Package> packages, IReadOnlyList<Game> games)
    {
        var titles = new Dictionary<string, string>(games.Count, StringComparer.Ordinal);
        foreach (var game in games)
        {
            titles[game.Id] = game.Title;
        }

        var rows = new CatalogRow[packages.Count];
        var present = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var title = titles.GetValueOrDefault(package.GameId, package.GameId);
            if (!_rows.TryGetValue(package.GameId, out var row))
            {
                row = new CatalogRow(package.GameId, title, package.Version, package.FileCount, package.TotalSizeBytes);
                _rows[package.GameId] = row;
            }
            else
            {
                // Actualizar los datos no toca el estado de confirmacion del borrado.
                row.Update(title, package.Version, package.FileCount, package.TotalSizeBytes);
            }

            rows[i] = row;
            present.Add(package.GameId);
        }

        // Descarta las filas de los juegos que ya no estan publicados.
        foreach (var gameId in _rows.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            _rows.Remove(gameId);
        }

        return rows;
    }

    /// <summary>Olvida la fila de un juego (por ejemplo, justo despues de borrarlo).</summary>
    public void Remove(string gameId) => _rows.Remove(gameId);
}
