using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanForge.Client;

namespace LanForge.UnitTests.Client;

/// <summary>
/// Tests del arranque headless compartido. Comprueban que la aplicacion Avalonia
/// que se registra en el proceso es la del cliente: su <c>Theme.axaml</c> trae los
/// recursos que usa la UI del cliente (la fuente de iconos <c>FaSolid</c>).
///
/// Antes de unificar el arranque, el fixture del cliente y el del servidor
/// registraban cada uno su propia <c>Application</c>. En Windows ganaba la del
/// servidor, el <c>Theme.axaml</c> del cliente no se cargaba y los tests de UI del
/// cliente fallaban con "Static resource 'FaSolid' not found".
///
/// Comparte la coleccion "Cliente-UI": la aplicacion Avalonia es un singleton por
/// proceso.
/// </summary>
[Collection("Cliente-UI")]
public sealed class HeadlessAvaloniaStartupTests
{
    [Fact]
    public void La_aplicacion_registrada_es_la_del_cliente()
    {
        // El arranque compartido registra siempre la aplicacion del cliente: el
        // servidor comparte ese arranque en lugar de registrar la suya.
        Assert.IsType<LanForge.Client.App>(Application.Current);
    }

    [Fact]
    public void Los_recursos_del_tema_del_cliente_estan_disponibles()
    {
        // El recurso que define la UI del cliente: sin el, la ventana del cliente
        // revienta al construirse con "Static resource 'FaSolid' not found".
        Assert.True(
            Application.Current!.TryFindResource("FaSolid", out var font),
            "El recurso 'FaSolid' del tema del cliente no esta cargado.");
        Assert.IsAssignableFrom<FontFamily>(font);
    }

    [Fact]
    public void El_hilo_de_ui_ve_la_aplicacion_del_cliente()
    {
        // La sesion headless crea su propio hilo de UI, y las ventanas se construyen
        // ahi. Si ese hilo no ve la aplicacion del cliente, su Theme.axaml no esta
        // cargado y las ventanas del cliente revientan al construirse.
        Avalonia.Application? appOnUiThread = null;
        bool? hasFontOnUiThread = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            appOnUiThread = Application.Current;
            hasFontOnUiThread = Application.Current!.TryFindResource("FaSolid", out _);
        });

        Assert.IsType<LanForge.Client.App>(appOnUiThread);
        Assert.True(hasFontOnUiThread);
    }
}
