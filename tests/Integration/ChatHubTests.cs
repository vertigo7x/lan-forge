using LanForge.Server.Http;
using LanForge.Shared.Chat;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>Chat efímero sobre SignalR real: dos clientes, sala por juego, sin historial.</summary>
public sealed class ChatHubTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static HubConnection CreateClient(string baseAddress, string userName) =>
        new HubConnectionBuilder()
            .WithUrl($"{baseAddress}{ChatHubContract.Path}?user={Uri.EscapeDataString(userName)}")
            .Build();

    [Fact]
    public async Task TwoPlayers_ChatInGameRoom_RoomDisappearsWhenEmpty()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        await using var playerA = CreateClient(address, "AnaPC");
        await using var playerB = CreateClient(address, "RuliPC");
        await playerA.StartAsync();
        await playerB.StartAsync();

        // #general siempre existe.
        var initialChannels = await playerA.InvokeAsync<IReadOnlyList<ChatChannelInfo>>("ListChannels");
        Assert.Contains(initialChannels, c => c.Name == ChatHubContract.GeneralChannel);

        // Ambos entran en la sala del juego; B recibe el mensaje de A.
        var received = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        playerB.On<ChatMessage>(ChatHubContract.ReceiveMessageMethod, message => received.TrySetResult(message));

        await playerA.InvokeAsync("Join", "quake3");
        await playerB.InvokeAsync("Join", "quake3");
        await playerA.InvokeAsync("Send", "quake3", "¡Arrancamos la final!");

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("quake3", message.Channel);
        Assert.Equal("AnaPC", message.Sender);
        Assert.Equal("¡Arrancamos la final!", message.Text);

        var members = await playerA.InvokeAsync<IReadOnlyList<string>>("ListMembers", "quake3");
        Assert.Equal(2, members.Count);

        // Nadie fuera del canal recibe nada: C está en general.
        await using var playerC = CreateClient(address, "MikaPC");
        await playerC.StartAsync();
        var cReceived = false;
        playerC.On<ChatMessage>(ChatHubContract.ReceiveMessageMethod, _ => cReceived = true);
        await Task.Delay(300);
        Assert.False(cReceived);

        // Salen los dos: la sala efímera desaparece; general permanece.
        await playerA.InvokeAsync("Leave", "quake3");
        await playerB.InvokeAsync("Leave", "quake3");
        var finalChannels = await playerC.InvokeAsync<IReadOnlyList<ChatChannelInfo>>("ListChannels");
        Assert.DoesNotContain(finalChannels, c => c.Name == "quake3");
        Assert.Contains(finalChannels, c => c.Name == ChatHubContract.GeneralChannel);
    }

    [Fact]
    public async Task UnMiembroPorNombre_NoPuedeEscribirEnUnCanalEnElQueNoEsta()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        // Ana entra en la sala del juego.
        await using var ana = CreateClient(address, "AnaPC");
        await ana.StartAsync();
        await ana.InvokeAsync("Join", "quake3");

        // OTRO jugador con el MISMO nombre de usuario que Ana, pero que nunca entro.
        // Antes el servidor comparaba nombres: le dejaba escribir en la sala de Ana.
        await using var impostor = CreateClient(address, "AnaPC");
        await impostor.StartAsync();

        var anaReceived = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        ana.On<ChatMessage>(ChatHubContract.ReceiveMessageMethod, message => anaReceived.TrySetResult(message));

        await impostor.InvokeAsync("Send", "quake3", "mensaje inyectado");

        // El impostor no es miembro: nadie debe recibir su mensaje.
        await Task.Delay(500);
        Assert.False(anaReceived.Task.IsCompleted);

        // Y el canal sigue con un solo miembro.
        var members = await ana.InvokeAsync<IReadOnlyList<string>>("ListMembers", "quake3");
        Assert.Single(members);
        Assert.Equal("AnaPC", members[0]);
    }

    [Fact]
    public async Task LosMiembrosSeActualizanCuandoUnJugadorSeReconectaConOtroNombre()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        await using var playerB = CreateClient(address, "RuliPC");
        await playerB.StartAsync();
        await playerB.InvokeAsync("Join", ChatHubContract.GeneralChannel);

        TaskCompletionSource<IReadOnlyList<string>>? membersChanged = null;
        playerB.On<string, IReadOnlyList<string>>(ChatHubContract.ReceiveMembersMethod, (_, members) =>
        {
            if (members.Contains("AnaNueva", StringComparer.Ordinal))
            {
                membersChanged?.TrySetResult(members);
            }
        });

        await using (var playerA = CreateClient(address, "AnaPC"))
        {
            await playerA.StartAsync();
            await playerA.InvokeAsync("Join", ChatHubContract.GeneralChannel);
            await playerA.StopAsync();
        }

        membersChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // La desconexión ya se produjo: forzamos la observación del estado actual
        // con la reconexión, que debe anunciar el nombre nuevo.
        await using var renamedPlayer = CreateClient(address, "AnaNueva");
        await renamedPlayer.StartAsync();
        await renamedPlayer.InvokeAsync("Join", ChatHubContract.GeneralChannel);

        var members = await membersChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("AnaNueva", members);
        Assert.Contains("RuliPC", members);
        Assert.DoesNotContain("AnaPC", members);
    }

    [Fact]
    public async Task Registry_KeepsGeneral_AndRemovesEmptyRooms()
    {
        var registry = new ChatRoomRegistry();
        Assert.True(registry.Exists(ChatHubContract.GeneralChannel));

        registry.Join("conn-1", "Ana", "sala1");
        registry.Join("conn-2", "Ruli", "sala1");
        Assert.False(registry.Leave("conn-1", "sala1"));
        Assert.True(registry.Leave("conn-2", "sala1"));
        Assert.False(registry.Exists("sala1"));

        // General nunca se borra, aunque nadie esté dentro.
        registry.Leave("conn-x", ChatHubContract.GeneralChannel);
        Assert.True(registry.Exists(ChatHubContract.GeneralChannel));

        Assert.Throws<ArgumentException>(() => ChatRoomRegistry.ValidateChannel("../evil"));
    }
}
