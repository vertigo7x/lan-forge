using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LanForge.Shared.Models;

namespace LanForge.Client.ViewModels;

public enum GameCardState
{
    Available,
    Installed,
    UpdateAvailable,
    Downloading,
}

/// <summary>View model de la game-card del design (components/game-card.uicomponent).</summary>
public sealed class GameCardItem : INotifyPropertyChanged
{
    public CatalogEntry Entry { get; }

    public GameCardState State { get; private set; }

    private int _progressPercent;

    public GameCardItem(
        CatalogEntry entry,
        string? installedVersion,
        Func<GameCardItem, Task> install,
        Func<GameCardItem, Task> play,
        Func<GameCardItem, Task>? uninstall = null)
    {
        Entry = entry;
        State = installedVersion is null
            ? GameCardState.Available
            : installedVersion == entry.Version ? GameCardState.Installed : GameCardState.UpdateAvailable;
        InstalledVersion = installedVersion;

        InstallCommand = new DelegateCommand(() => install(this));
        PlayCommand = new DelegateCommand(() => play(this));
        if (uninstall is not null)
        {
            UninstallCommand = new DelegateCommand(() => uninstall(this));
        }
    }

    public string? InstalledVersion { get; }

    public ICommand InstallCommand { get; }

    public ICommand PlayCommand { get; }

    public ICommand? UninstallCommand { get; }

    /// <summary>El comando de la acción principal: Jugar si está instalado, instalar/actualizar en caso contrario.</summary>
    public ICommand ActionCommand => IsPlayAction ? PlayCommand : InstallCommand;

    private string _coverAsset = string.Empty;

    public string CoverAsset
    {
        get => _coverAsset;
        set
        {
            _coverAsset = value;
            OnPropertyChanged();
        }
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set
        {
            _progressPercent = value;
            OnPropertyChanged();
        }
    }

    public string Title => Entry.Title;

    public string Meta => $"{Entry.FileCount} archivos";

    public string SizeText => FormatBytes(Entry.TotalSizeBytes);

    public string BadgeText => State switch
    {
        GameCardState.Installed => "Instalado",
        GameCardState.UpdateAvailable => $"Actualizable a v{Entry.Version}",
        GameCardState.Downloading => "Descargando",
        _ => "Disponible",
    };

    public string BadgeColor => State switch
    {
        GameCardState.Installed => "#4ADE80",
        GameCardState.Downloading => "#38BDF8",
        GameCardState.UpdateAvailable => "#FBBF24",
        _ => "#38BDF8",
    };

    public bool IsInstalledState => State is GameCardState.Installed;

    public bool IsDownloading => State is GameCardState.Downloading;

    public bool IsActionVisible => State is not GameCardState.Downloading;

    public string ActionText => State switch
    {
        GameCardState.Installed => "Jugar",
        GameCardState.UpdateAvailable => "Actualizar",
        _ => "Descargar",
    };

    public bool IsPlayAction => State is GameCardState.Installed;

    public static string FormatBytes(long bytes)
    {
        const string format = "0.0";
        if (bytes >= 1L << 30)
        {
            return $"{(bytes / (double)(1L << 30)).ToString(format, System.Globalization.CultureInfo.InvariantCulture)} GB";
        }

        if (bytes >= 1L << 20)
        {
            return $"{(bytes / (double)(1L << 20)).ToString(format, System.Globalization.CultureInfo.InvariantCulture)} MB";
        }

        return $"{(bytes / (double)(1L << 10)).ToString(format, System.Globalization.CultureInfo.InvariantCulture)} KB";
    }

    public void SetDownloading(bool downloading)
    {
        State = downloading ? GameCardState.Downloading : GameCardState.Available;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(BadgeColor));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsActionVisible));
        OnPropertyChanged(nameof(IsPlayAction));
        OnPropertyChanged(nameof(ActionCommand));
    }

    public void SetInstalled()
    {
        State = GameCardState.Installed;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(BadgeColor));
        OnPropertyChanged(nameof(IsInstalledState));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsActionVisible));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsPlayAction));
        OnPropertyChanged(nameof(ActionCommand));
    }

    public void SetUninstalled()
    {
        State = GameCardState.Available;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(BadgeColor));
        OnPropertyChanged(nameof(IsInstalledState));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsActionVisible));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsPlayAction));
        OnPropertyChanged(nameof(ActionCommand));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
