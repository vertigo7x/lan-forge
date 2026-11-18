using System.Windows.Input;
using LanForge.Client.Domain;

namespace LanForge.Client.ViewModels;

/// <summary>Elemento de la lista de juegos de Steam: datos + acción Jugar.</summary>
public sealed class SteamGameItem
{
    private readonly SteamGame _game;

    public SteamGameItem(SteamGame game, Func<SteamGameItem, Task> play)
    {
        _game = game;
        PlayCommand = new DelegateCommand(() => play(this));
    }

    public int AppId => _game.AppId;

    public string Name => _game.Name;

    public SteamGame Game => _game;

    public string Detail => $"AppID {_game.AppId} — {_game.InstallPath}";

    public ICommand PlayCommand { get; }
}
