using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using LanForge.Server.Application;
using LanForge.Server.Views;
using LanForge.Shared;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas end-to-end del caso de fallo de validación del formulario de creación
/// de paquetes.
///
/// El recorrido que se prueba es el completo: el usuario rellena el formulario →
/// el formulario envía la solicitud por HTTP a POST /api/v1/packages → el
/// endpoint responde 400 con un error por campo → cada mensaje se pinta junto a
/// SU campo.
///
/// El endpoint se sustituye por un stub HTTP que responde exactamente como lo
/// hace el servidor real (`validation_failed` + un error por campo), de modo que
/// el test cubre la frontera de red (la petición se envía y recibe el error) y la
/// frontera de UI (la correspondencia mensaje↔campo) sin depender de un DOM: la
/// UI de LAN Forge es XAML de Avalonia, no HTML, así que no existe `page.route()`
/// ni selector CSS.
///
/// La asociación mensaje↔campo no se comprueba "por la página": cada mensaje se
/// localiza por el nombre del control de error del campo correspondiente
/// (`GameIdErrorText`, `TitleErrorText`, …), que es el `data-testid` equivalente
/// en Avalonia.
/// </summary>
[Collection("Cliente-UI")]
public sealed class PackageCreateValidationFailureE2ETests
{
    /// <summary>
    /// Error de validación que devuelve el endpoint: tres violaciones distintas
    /// (campo obligatorio vacío, formato incorrecto y ruta fuera de rango) y un
    /// mensaje por campo, con la misma forma que produce el servidor real.
    /// </summary>
    private static readonly ApiErrorResponse StubValidationError = new(
        "validation_failed",
        "La solicitud no cumple las validaciones del paquete.",
        [
            new ApiFieldError(
                PackageCreationFields.Title,
                "required",
                "El título del juego es obligatorio."),
            new ApiFieldError(
                PackageCreationFields.Version,
                "invalid_format",
                "La versión del paquete debe cumplir el formato N[.N[.N[.N]]][-pre-release]."),
            new ApiFieldError(
                PackageCreationFields.SourceDirectory,
                "invalid_path",
                "El directorio origen debe ser una ruta absoluta del servidor."),
        ]);

    /// <summary>
    /// Campo del formulario ↔ control que muestra su mensaje de error. La clave es
    /// la clave de campo del contrato HTTP y el valor el nombre del control XAML:
    /// es la asociación explícita que el test verifica.
    /// </summary>
    private static readonly (string Field, string ErrorTextName)[] FieldErrorControls =
    [
        (PackageCreationFields.GameId, "GameIdErrorText"),
        (PackageCreationFields.Title, "TitleErrorText"),
        (PackageCreationFields.Version, "VersionErrorText"),
        (PackageCreationFields.SourceDirectory, "SourceDirectoryErrorText"),
        (PackageCreationFields.EntryPoint, "EntryPointErrorText"),
        (PackageCreationFields.Arguments, "ArgumentsErrorText"),
    ];

    /// <summary>
    /// Caso e2e del fallo de validación devuelto por el endpoint: la petición sale
    /// (los datos son válidos para la validación local, pero el servidor los
    /// rechaza), el endpoint responde con error de validación y CADA mensaje
    /// aparece junto a su campo. El usuario no ve confirmación de éxito ni es
    /// redirigido.
    /// </summary>
    [Fact]
    public void Un_formulario_rechazado_por_el_endpoint_muestra_cada_error_en_su_campo_sin_confirmar_exito()
    {
        var handler = new StubValidationHandler(StubValidationError);
        using var service = new HttpPackageCreationService("http://127.0.0.1:1", handler);
        var (view, window) = ShowForm(service);

        // Datos que la validación local acepta pero que el servidor rechaza:
        //  - carpeta origen relativa: el servidor exige ruta absoluta,
        //  - la carpeta y la versión que el endpoint va a marcar como inválidas
        //    mediante su respuesta de validación.
        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.0.0",
            "carpeta/relativa",
            "game.exe",
            "--fullscreen"));

        Submit(view);

        // (a) El endpoint fue llamado una vez y devolvio el error de validacion.
        Assert.Equal(1, handler.RequestCount);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/packages", request.RequestUri?.AbsolutePath);
        Assert.Equal("neon-drift", request.GetValue("gameId"));
        Assert.Equal("Neon Drift", request.GetValue("title"));
        Assert.Equal("1.0.0", request.GetValue("version"));
        Assert.Equal("carpeta/relativa", request.GetValue("sourceDirectory"));
        Assert.Equal("game.exe", request.GetValue("entryPoint"));
        Assert.Equal("validation_failed", handler.Responses[0].Code);
        Assert.Equal(3, handler.Responses[0].Errors.Count);

        // (b) CADA mensaje aparece junto a SU campo. Se comprueba el control de
        // error del campo concreto, no la presencia del texto en la pagina.
        AssertFieldErrors(window, StubValidationError);

        // El mensaje de un campo no se ha copiado al control de otro campo.
        var errorTexts = ErrorTexts(window);
        Assert.NotEqual(errorTexts["GameIdErrorText"], errorTexts["TitleErrorText"]);
        Assert.NotEqual(errorTexts["TitleErrorText"], errorTexts["VersionErrorText"]);
        Assert.NotEqual(errorTexts["VersionErrorText"], errorTexts["SourceDirectoryErrorText"]);

        // El usuario NO ve confirmacion de exito ni es redirigido: el formulario
        // sigue en su sitio, con los valores intactos y el estado en error.
        var statusText = StatusText(window);
        Assert.False(
            statusText.Contains("Paquete creado", StringComparison.OrdinalIgnoreCase),
            $"Se mostro una confirmacion de exito inesperada: '{statusText}'.");
        Assert.False(
            statusText.Contains("creado", StringComparison.OrdinalIgnoreCase),
            $"El estado del formulario anuncia exito: '{statusText}'.");
        Assert.True(view.ViewModel.StatusIsError);
        Assert.Equal(
            "La solicitud no cumple las validaciones del paquete.",
            view.ViewModel.StatusMessage);

        // No hay redireccion: la vista probada sigue abierta y visible.
        Assert.True(IsVisible(window));

        // Los datos del formulario no se borran: el usuario puede corregirlos.
        var values = FormValues(window);
        Assert.Equal("neon-drift", values["GameIdBox"]);
        Assert.Equal("Neon Drift", values["TitleBox"]);
        Assert.Equal("1.0.0", values["VersionBox"]);
        Assert.Equal("carpeta/relativa", values["SourceDirectoryBox"]);
        Assert.Equal("game.exe", values["EntryPointBox"]);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    /// <summary>
    /// Caso e2e de datos inválidos en el propio formulario: campos requeridos
    /// vacíos y formatos incorrectos. Cada mensaje aparece junto a SU campo y el
    /// endpoint NO se llama (no tiene sentido una petición con datos que el
    /// formulario ya sabe inválidos), así que tampoco hay redirección ni
    /// confirmación de éxito.
    /// </summary>
    [Fact]
    public void Un_formulario_con_campos_vacios_o_formato_incorrecto_muestra_cada_error_en_su_campo_sin_llamar_al_endpoint()
    {
        var handler = new StubValidationHandler(StubValidationError);
        using var service = new HttpPackageCreationService("http://127.0.0.1:1", handler);
        var (view, window) = ShowForm(service);

        // Campos requeridos vacios + formato incorrecto + valor fuera de rango
        // (argumentos por encima del limite).
        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            string.Empty,
            string.Empty,
            "no-es-una-version",
            string.Empty,
            string.Empty,
            new string('x', 1025)));

        Submit(view);

        // La validación local corta el envio: el endpoint no llega a llamarse.
        Assert.Equal(0, handler.RequestCount);

        var errorTexts = ErrorTexts(window);
        Assert.Equal("El id del juego es obligatorio.", errorTexts["GameIdErrorText"]);
        Assert.Equal("El título es obligatorio.", errorTexts["TitleErrorText"]);
        Assert.Equal(
            "Versión inválida: usa el formato 1.0.0 (hasta 64 caracteres).",
            errorTexts["VersionErrorText"]);
        Assert.Equal("Indica la carpeta del juego portable.", errorTexts["SourceDirectoryErrorText"]);
        Assert.Equal("Indica el ejecutable del juego.", errorTexts["EntryPointErrorText"]);
        Assert.Equal(
            "Los argumentos no pueden pasar de 1024 caracteres.",
            errorTexts["ArgumentsErrorText"]);

        // Los mensajes no se han mezclado entre campos.
        Assert.NotEqual(errorTexts["GameIdErrorText"], errorTexts["TitleErrorText"]);
        Assert.NotEqual(errorTexts["TitleErrorText"], errorTexts["VersionErrorText"]);

        // Sin exito y sin redireccion.
        var statusText = StatusText(window);
        Assert.False(
            statusText.Contains("creado", StringComparison.OrdinalIgnoreCase),
            $"El estado del formulario anuncia exito: '{statusText}'.");
        Assert.True(view.ViewModel.StatusIsError);
        Assert.True(IsVisible(window));

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    /// <summary>
    /// Comprueba que cada error del endpoint aparece en el control de error de SU
    /// campo, y que los campos sin error del endpoint no muestran mensaje.
    /// </summary>
    private static void AssertFieldErrors(Window window, ApiErrorResponse errorResponse)
    {
        var errorTexts = ErrorTexts(window);
        var expectedByField = errorResponse.Errors.ToDictionary(
            error => error.Field,
            error => error.Message,
            StringComparer.Ordinal);

        foreach (var (field, errorTextName) in FieldErrorControls)
        {
            if (!expectedByField.TryGetValue(field, out var expectedMessage))
            {
                // Campo sin error del servidor: su control de error debe estar vacio.
                Assert.True(
                    string.IsNullOrEmpty(errorTexts[errorTextName]),
                    $"El campo '{field}' no tiene error, pero su control '{errorTextName}' muestra: '{errorTexts[errorTextName]}'.");
                continue;
            }

            Assert.Equal(expectedMessage, errorTexts[errorTextName]);
        }
    }

    /// <summary>
    /// Envia el formulario y espera a que termine.
    ///
    /// Todo ocurre en el hilo de UI: al cambiar el estado del view model se disparan
    /// los bindings, que tocan controles del hilo de UI, y RunJobs solo se puede
    /// ejecutar desde ese hilo. El stub responde de inmediato, asi que el envio no se
    /// suspende y termina en el mismo hilo.
    /// </summary>
    private static void Submit(PackageCreateView view)
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view.SubmitAsync().GetAwaiter().GetResult();
            HeadlessAvalonia.DrainUiQueue();
        });
    }

    /// <summary>
    /// Monta el formulario real en una ventana. La vista y su view model nacen en
    /// el hilo de UI: los controles de Avalonia pertenecen al hilo que los crea.
    /// </summary>
    private static (PackageCreateView View, Window Window) ShowForm(IPackageCreationService service)
    {
        PackageCreateView? view = null;
        Window? window = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view = new PackageCreateView(service);
            window = new Window
            {
                Width = 640,
                Height = 700,
                Content = view,
            };
            window.Show();
            window.UpdateLayout();
            HeadlessAvalonia.DrainUiQueue();
            window.UpdateLayout();
        });
        return (view!, window!);
    }

    /// <summary>
    /// True si la ventana sigue visible. La propiedad pertenece al hilo de UI:
    /// se lee desde ese hilo, no desde el hilo del test.
    /// </summary>
    private static bool IsVisible(Window window)
    {
        var visible = false;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => visible = window.IsVisible);
        return visible;
    }

    /// <summary>Texto de cada control de error del formulario, indexado por su nombre.</summary>
    private static Dictionary<string, string?> ErrorTexts(Window window)
    {
        var errors = new Dictionary<string, string?>();
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>())
            {
                if (!string.IsNullOrEmpty(text.Name))
                {
                    errors[text.Name] = text.Text;
                }
            }
        });
        return errors;
    }

    /// <summary>Texto del bloque de estado general del formulario.</summary>
    private static string StatusText(Window window)
    {
        var status = string.Empty;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            // El bloque de estado es el TextBlock sin nombre que no es un error de
            // campo: se localiza por su contenido enlazado a StatusMessage.
            foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>())
            {
                if (!string.IsNullOrEmpty(text.Name))
                {
                    continue;
                }

                if (text.Text is not null &&
                    text.Text.Contains("validaciones", StringComparison.OrdinalIgnoreCase))
                {
                    status = text.Text;
                    return;
                }
            }

            // Si el mensaje general no contiene la palabra "validaciones", se
            // recoge igualmente el ultimo TextBlock sin nombre con texto.
            foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>())
            {
                if (string.IsNullOrEmpty(text.Name) && !string.IsNullOrEmpty(text.Text))
                {
                    status = text.Text;
                }
            }
        });
        return status;
    }

    /// <summary>Valor actual de cada campo de entrada del formulario.</summary>
    private static Dictionary<string, string?> FormValues(Window window)
    {
        var values = new Dictionary<string, string?>();
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            foreach (var box in window.GetLogicalDescendants().OfType<TextBox>())
            {
                if (!string.IsNullOrEmpty(box.Name))
                {
                    values[box.Name] = box.Text;
                }
            }
        });
        return values;
    }

    /// <summary>
    /// Stub del endpoint de creación de paquetes: registra las peticiones que
    /// llegan y responde con el error de validación preparado, igual que hace el
    /// servidor real (400 + <c>ApiErrorResponse</c>).
    /// </summary>
    private sealed class StubValidationHandler(ApiErrorResponse errorResponse) : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests => _requests;

        public int RequestCount => _requests.Count;

        /// <summary>Respuestas devueltas al cliente, en orden.</summary>
        public List<ApiErrorResponse> Responses { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));
            Responses.Add(errorResponse);

            var json = JsonSerializer.Serialize(errorResponse, SharedJson.Options);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Petición registrada por el stub.</summary>
    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string Body)
    {
        /// <summary>Valor de una propiedad del cuerpo JSON de la petición.</summary>
        public string? GetValue(string property)
        {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.TryGetProperty(property, out var value)
                ? value.GetString()
                : null;
        }
    }
}
