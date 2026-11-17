using LanForge.Client.ViewModels;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Client;

public sealed class GameCardItemTests
{
    private static CatalogEntry Entry() => new("quake3", "Quake III", "quake3", "1.32", 2048, 3);

    [Fact]
    public void ActionCommand_IsInstall_WhenAvailable()
    {
        var calls = new List<string>();
        var card = new GameCardItem(Entry(), installedVersion: null,
            install: _ => { calls.Add("install"); return Task.CompletedTask; },
            play: _ => { calls.Add("play"); return Task.CompletedTask; });

        Assert.Equal("Descargar", card.ActionText);
        card.ActionCommand.Execute(null);
        Assert.Equal(["install"], calls);
    }

    [Fact]
    public void ActionCommand_IsPlay_WhenInstalled()
    {
        var calls = new List<string>();
        var card = new GameCardItem(Entry(), installedVersion: "1.32",
            install: _ => { calls.Add("install"); return Task.CompletedTask; },
            play: _ => { calls.Add("play"); return Task.CompletedTask; });

        Assert.True(card.IsInstalledState);
        Assert.Equal("Jugar", card.ActionText);
        card.ActionCommand.Execute(null);
        Assert.Equal(["play"], calls);
    }

    [Fact]
    public void ActionCommand_IsInstall_WhenUpdateAvailable()
    {
        var calls = new List<string>();
        var card = new GameCardItem(Entry(), installedVersion: "1.0",
            install: _ => { calls.Add("install"); return Task.CompletedTask; },
            play: _ => { calls.Add("play"); return Task.CompletedTask; });

        Assert.Equal("Actualizar", card.ActionText);
        card.ActionCommand.Execute(null);
        Assert.Equal(["install"], calls);
    }

    [Fact]
    public void UninstallCommand_InvokesCallback_OnlyWhenProvided()
    {
        var calls = new List<string>();
        var card = new GameCardItem(Entry(), installedVersion: "1.32",
            install: _ => Task.CompletedTask,
            play: _ => Task.CompletedTask,
            uninstall: _ => { calls.Add("uninstall"); return Task.CompletedTask; });

        Assert.NotNull(card.UninstallCommand);
        card.UninstallCommand!.Execute(null);
        Assert.Equal(["uninstall"], calls);
    }

    [Fact]
    public void SetUninstalled_ResetsToAvailable()
    {
        var card = new GameCardItem(Entry(), installedVersion: "1.32",
            install: _ => Task.CompletedTask, play: _ => Task.CompletedTask, uninstall: _ => Task.CompletedTask);

        card.SetUninstalled();

        Assert.False(card.IsInstalledState);
        Assert.Equal("Disponible", card.BadgeText);
        Assert.Equal("Descargar", card.ActionText);
    }

    [Fact]
    public void SetInstalled_AfterDownload_SwitchesToPlay()
    {
        var card = new GameCardItem(Entry(), installedVersion: null,
            install: _ => Task.CompletedTask, play: _ => Task.CompletedTask);

        card.SetDownloading(true);
        Assert.True(card.IsDownloading);
        Assert.False(card.IsActionVisible);

        card.SetInstalled();
        Assert.True(card.IsInstalledState);
        Assert.Equal("Jugar", card.ActionText);
    }

    [Theory]
    [InlineData(38L * 1024 * 1024 * 1024 + (long)(0.4 * 1024 * 1024 * 1024), "38.4 GB")]
    [InlineData(12L * 1024 * 1024, "12.0 MB")]
    [InlineData(512, "0.5 KB")]
    public void FormatBytes_MatchesDesignUnits(long bytes, string expected)
    {
        Assert.Equal(expected, GameCardItem.FormatBytes(bytes));
    }
}
