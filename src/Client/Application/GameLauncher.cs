using System.Diagnostics;
using LanForge.Client.Domain;

namespace LanForge.Client.Application;

/// <summary>Abstracción de arranque de procesos (testeable).</summary>
public interface IProcessStarter
{
    void Start(string fileName, string? arguments, string workingDirectory);
}

public sealed class SystemProcessStarter : IProcessStarter
{
    public void Start(string fileName, string? arguments, string workingDirectory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        });
    }
}

public sealed class GameLauncher
{
    private readonly IProcessStarter _starter;

    public GameLauncher(IProcessStarter starter)
    {
        _starter = starter;
    }

    public void Launch(InstalledGame game)
    {
        var entryPoint = Path.Combine(game.InstallPath, game.EntryPoint.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(entryPoint))
        {
            throw new LanForgeClientException($"El ejecutable del juego no existe: '{entryPoint}'. Reinstala el juego.");
        }

        _starter.Start(entryPoint, game.Arguments, game.InstallPath);
    }
}
