using System.Text.Json;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Puente entre el hilo de interfaz y las peticiones HTTP reales.
///
/// En el entorno headless de Avalonia el dispatcher no bombea el trabajo posteado
/// desde otros hilos: una continuacion publicada con <c>Dispatcher.UIThread.Post</c>
/// no llega a ejecutarse nunca. Por eso una peticion HTTP real awaited desde el
/// hilo de UI se quedaria colgada para siempre.
///
/// Este manejador resuelve el problema sin tocar el codigo de produccion: ejecuta
/// la peticion HTTP de verdad en un hilo de fondo y **entrega la respuesta ya
/// resuelta** al hilo que llama. El <c>await</c> del view model continua de forma
/// sincrona en el hilo de interfaz, el binding se actualiza ahi mismo y la UI
/// muestra el resultado, que es justo lo que se quiere comprobar.
///
/// La peticion no se sustituye por un doble: sale por la red, llega al servidor
/// real y la responde el endpoint real. Lo unico que hace este manejador es
/// cambiar el momento en que se entrega la respuesta.
/// </summary>
public sealed class SynchronousHttpHandler : DelegatingHandler
{
    public SynchronousHttpHandler()
        : base(new SocketsHttpHandler())
    {
    }

    /// <summary>
    /// Peticiones que salieron hacia la red, con lo que se vio en la frontera.
    /// Es el equivalente de <c>page.route()</c> / <c>cy.intercept()</c> para una
    /// app sin navegador: no intercepta para responder con un doble, sino para
    /// observar lo que de verdad se envia.
    /// </summary>
    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Numero de peticiones que han salido hacia la red.</summary>
    public int RequestCount
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    private readonly List<CapturedRequest> _requests = [];
    private readonly object _gate = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        Capture(request, body);

        // La peticion sale de verdad, por la red, hacia el servidor real.
        var pending = Task.Run(
            async () =>
            {
                using var client = new HttpClient();

                // La conexion se cierra al terminar la peticion. Si se deja abierta
                // (keep-alive), el servidor sigue con la conexion viva y al parar
                // el host se queda esperando a que expire el tiempo de inactividad.
                client.DefaultRequestHeaders.ConnectionClose = true;

                using var copy = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
                using var response = await client.SendAsync(copy, cancellationToken).ConfigureAwait(false);

                // El cuerpo se copia a memoria: leerlo despues tiene que poder
                // completarse de forma sincrona, si no la lectura del JSON se
                // lleva la continuacion a otro hilo y la UI deja de actualizarse.
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType;

                var buffered = new HttpResponseMessage(response.StatusCode)
                {
                    Content = new ByteArrayContent(bytes),
                    ReasonPhrase = response.ReasonPhrase,
                };
                buffered.Content.Headers.ContentType = contentType;
                return buffered;
            },
            CancellationToken.None);

        // El hilo que llama espera de forma sincrona: el await del view model
        // continua en ese mismo hilo, sin depender del dispatcher.
        return Task.FromResult(pending.GetAwaiter().GetResult());
    }

    private void Capture(HttpRequestMessage request, string body)
    {
        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.Headers.TryGetValues("X-LanForge-Client-Id", out var clientIds)
                ? clientIds.FirstOrDefault() ?? string.Empty
                : string.Empty,
            string.IsNullOrEmpty(body) ? null : JsonDocument.Parse(body).RootElement.Clone());

        lock (_gate)
        {
            _requests.Add(captured);
        }
    }

    /// <summary>
    /// Copia la peticion: un <see cref="HttpRequestMessage"/> no se puede reenviar.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content is not null)
        {
            copy.Content = new ByteArrayContent(
                await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            copy.Content.Headers.ContentType = request.Content.Headers.ContentType;
        }

        foreach (var header in request.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return copy;
    }
}

/// <summary>Peticion HTTP capturada en la frontera de red.</summary>
/// <param name="Method">Metodo HTTP.</param>
/// <param name="Path">Ruta de la peticion.</param>
/// <param name="ClientId">Cabecera de correlacion del cliente, si se envio.</param>
/// <param name="BodyJson">Cuerpo de la peticion, ya parseado.</param>
public sealed record CapturedRequest(
    HttpMethod Method,
    string Path,
    string ClientId,
    JsonElement? BodyJson);
