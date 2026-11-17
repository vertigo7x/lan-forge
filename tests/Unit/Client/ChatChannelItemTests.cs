using LanForge.Client.ViewModels;
using LanForge.Shared.Chat;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Client;

public sealed class ChatChannelItemTests
{
    private static readonly CatalogEntry NeonDrift =
        new("neon-drift", "Neon Drift", "neon-drift", "1.0.0", 1024, 12);

    [Fact]
    public void El_canal_general_mantiene_su_nombre()
    {
        var item = ChatChannelItem.From(new ChatChannelInfo(ChatHubContract.GeneralChannel, 3), [NeonDrift]);
        Assert.Equal("general", item.DisplayName);
        Assert.Equal(3, item.Members);
    }

    [Fact]
    public void La_sala_de_un_juego_muestra_el_titulo_del_juego()
    {
        var item = ChatChannelItem.From(new ChatChannelInfo("neon-drift", 2), [NeonDrift]);
        Assert.Equal("Neon Drift", item.DisplayName);
        Assert.Equal("neon-drift", item.Name);
    }

    [Fact]
    public void Un_canal_desconocido_cae_en_su_identificador()
    {
        var item = ChatChannelItem.From(new ChatChannelInfo("desconocido", 1), [NeonDrift]);
        Assert.Equal("desconocido", item.DisplayName);
    }
}
