using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LanForge.Server.ViewModels;

/// <summary>
/// Fila del catalogo en el organizador: el paquete publicado y su boton de borrar.
/// La fila se reutiliza entre refrescos, asi que sus datos son actualizables en sitio;
/// el estado de confirmacion del borrado vive en la fila y sobrevive al refresco.
/// </summary>
public sealed class CatalogRow : INotifyPropertyChanged
{
    private string _title;
    private string _version;
    private int _fileCount;
    private long _totalSizeBytes;
    private string _confirmText = "Borrar";

    public CatalogRow(string gameId, string title, string version, int fileCount, long totalSizeBytes)
    {
        GameId = gameId;
        _title = title;
        _version = version;
        _fileCount = fileCount;
        _totalSizeBytes = totalSizeBytes;
    }

    public string GameId { get; }

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public string Version
    {
        get => _version;
        private set => SetField(ref _version, value);
    }

    public int FileCount
    {
        get => _fileCount;
        private set => SetField(ref _fileCount, value);
    }

    public long TotalSizeBytes
    {
        get => _totalSizeBytes;
        private set => SetField(ref _totalSizeBytes, value);
    }

    public string Description =>
        $"{Title}  —  v{Version}  —  {FileCount} archivos, {TotalSizeBytes:N0} bytes  ({GameId})";

    /// <summary>Texto del boton: la primera pulsacion pide confirmacion, la segunda borra.</summary>
    public string ConfirmText
    {
        get => _confirmText;
        set => SetField(ref _confirmText, value);
    }

    /// <summary>
    /// Actualiza los datos del paquete sin tocar el estado de confirmacion: si el
    /// organizador ya ha pedido la confirmacion del borrado, un refresco no puede
    /// cancelarla.
    /// </summary>
    public void Update(string title, string version, int fileCount, long totalSizeBytes)
    {
        Title = title;
        Version = version;
        FileCount = fileCount;
        TotalSizeBytes = totalSizeBytes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
