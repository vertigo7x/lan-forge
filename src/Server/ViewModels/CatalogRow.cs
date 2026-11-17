using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LanForge.Server.ViewModels;

/// <summary>Fila del catalogo en el organizador: el paquete publicado y su boton de borrar.</summary>
public sealed class CatalogRow : INotifyPropertyChanged
{
    private string _confirmText = "Borrar";

    public CatalogRow(string gameId, string title, string version, int fileCount, long totalSizeBytes)
    {
        GameId = gameId;
        Title = title;
        Version = version;
        FileCount = fileCount;
        TotalSizeBytes = totalSizeBytes;
    }

    public string GameId { get; }

    public string Title { get; }

    public string Version { get; }

    public int FileCount { get; }

    public long TotalSizeBytes { get; }

    public string Description =>
        $"{Title}  —  v{Version}  —  {FileCount} archivos, {TotalSizeBytes:N0} bytes  ({GameId})";

    /// <summary>Texto del boton: la primera pulsacion pide confirmacion, la segunda borra.</summary>
    public string ConfirmText
    {
        get => _confirmText;
        set
        {
            _confirmText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
