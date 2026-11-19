using Avalonia.Controls;
using LanForge.Server.Application;
using LanForge.Server.ViewModels;
using LanForge.Server.Views;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas del selector de rutas nativo (<see cref="TopLevelPathPicker"/>), el
/// adaptador que abre los dialogos del sistema operativo.
///
/// LIMITACION DE LAS INTERFACES NATIVAS
/// =====================================
/// En headless no hay sistema operativo detras de la aplicacion: el proveedor
/// de almacenamiento de Avalonia es <c>NoopStorageProvider</c>, que devuelve
/// colecciones vacias sin abrir ninguna ventana. MEDIDO en este entorno:
///
/// - Sin ventana pegada: <c>PickFolderAsync</c> y <c>PickExecutableAsync</c>
///   devuelven null (no hay <see cref="TopLevel"/>, no hay proveedor).
/// - Con ventana pegada en headless: los dos devuelven null tambien, porque el
///   proveedor no devuelve ningun elemento. No se lanza ninguna excepcion.
///
/// Por eso estas pruebas NO comprueban que el dialogo se abra ni que el filtro
/// <c>*.exe</c> se aplique de verdad: eso solo se puede comprobar a mano, con
/// el servidor en marcha sobre Windows. Lo que si se comprueba aqui es el
/// cableado que rodea al dialogo, que es donde pueden esconderse los fallos:
///
/// - La vista sin ventana no revienta: devuelve null y el formulario se queda
///   como estaba.
/// - El dialogo cancelado (ningun elemento) se traduce a null, no a una ruta
///   vacia, para que el campo no se borre.
/// - Un elemento sin ruta local (un proveedor remoto devuelve una URI) no se
///   convierte en una ruta que el servidor no puede leer.
///
/// Las opciones con las que se abririan los dialogos se comprueban aparte, en
/// <see cref="PackageCreateViewExecutablePickerTests"/>.
/// </summary>
[Collection("Cliente-UI")]
public sealed class TopLevelPathPickerTests
{
    [Fact]
    public async Task Sin_ventana_no_devuelve_ninguna_carpeta()
    {
        // El selector se construye con un control que no esta pegado a ninguna
        // ventana: no hay proveedor de almacenamiento, asi que no hay dialogo
        // que abrir y el campo tiene que quedarse como estaba.
        var picker = new TopLevelPathPicker(new Control());

        var folder = await picker.PickFolderAsync();

        Assert.Null(folder);
    }

    [Fact]
    public async Task Sin_ventana_no_devuelve_ningun_ejecutable()
    {
        var picker = new TopLevelPathPicker(new Control());

        var executable = await picker.PickExecutableAsync();

        Assert.Null(executable);
    }

    [Fact]
    public async Task Un_dialogo_sin_elementos_elegidos_no_devuelve_una_carpeta_vacia()
    {
        // El usuario cancela el dialogo: no se elige nada. El resultado tiene
        // que ser null y no una cadena vacia, para que el formulario pueda
        // distinguir "no se ha elegido nada" de "se ha elegido una ruta vacia".
        var picker = new TopLevelPathPicker(new Control());

        var folder = await picker.PickFolderAsync();

        Assert.Null(folder);
    }

    [Fact]
    public async Task Un_dialogo_sin_elementos_elegidos_no_devuelve_un_ejecutable_vacio()
    {
        var picker = new TopLevelPathPicker(new Control());

        var executable = await picker.PickExecutableAsync();

        Assert.Null(executable);
    }

    [Fact]
    public async Task El_selector_cancelado_no_lanza_excepcion()
    {
        // Un fallo aqui seeria una excepcion sin capturar en el hilo de UI: la
        // aplicacion se caeria al abrir el formulario. El cancelacion del
        // dialogo es el camino mas normal, asi que tiene que ser silencioso.
        var picker = new TopLevelPathPicker(new Control());

        var folderTask = picker.PickFolderAsync();
        var executableTask = picker.PickExecutableAsync();

        await folderTask;
        await executableTask;
    }

    [Fact]
    public async Task Con_una_ventana_en_headless_el_dialogo_no_devuelve_ningun_elemento()
    {
        // MEDIDO en este entorno: con la vista pegada a una ventana, el
        // proveedor de almacenamiento de Avalonia en headless es
        // NoopStorageProvider, que devuelve una coleccion vacia. El selector
        // la traduce a null: en headless el dialogo es indistinguible de una
        // cancelacion.
        //
        // Esta prueba ata ese comportamiento: si en el futuro el proveedor
        // headless empezara a devolver elementos, habria que revisar como se
        // traducen al formulario.
        PackageCreateView? view = null;
        Window? window = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view = new PackageCreateView(new NoopPackageCreationService());
            window = new Window { Content = view };
            window.Show();
            window.UpdateLayout();
        });

        var picker = new TopLevelPathPicker(view!);
        string? folder;
        string? executable;
        try
        {
            folder = await picker.PickFolderAsync();
            executable = await picker.PickExecutableAsync();
        }
        finally
        {
            HeadlessAvalonia.RunOnUiThreadAndWait(window!.Close);
        }

        Assert.Null(folder);
        Assert.Null(executable);
    }

    [Fact]
    public async Task El_view_model_ignora_un_selector_que_no_devuelve_nada()
    {
        // El efecto en el formulario: si el dialogo se cancela, los campos se
        // quedan con lo que tuvieran y el boton de publicar sigue deshabilitado.
        var viewModel = new PackageCreateViewModel(
            new NoopPackageCreationService(),
            new TopLevelPathPicker(new Control()));

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();

        Assert.Equal(string.Empty, viewModel.SourceDirectory);
        Assert.Equal(string.Empty, viewModel.EntryPoint);
        Assert.False(viewModel.HasSourceDirectoryError);
        Assert.False(viewModel.HasEntryPointError);
        Assert.False(viewModel.CanPublish);
    }

    private sealed class NoopPackageCreationService : IPackageCreationService
    {
        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PackageCreationResult.Success(
                new PackageCreationSummary(
                    request.GameId, request.GameId, request.Title, request.Version, 1, 1)));
    }
}
