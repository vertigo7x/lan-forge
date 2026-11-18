using System.Text.Json;
using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Shared;
using LanForge.Shared.Logging;
using LanForge.Shared.Models;
using LanForge.Shared.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LanForge.Server.Http;

/// <summary>Endpoints HTTP v1: hello, catálogo, manifiestos y descarga por archivo.</summary>
public static class ApiEndpoints
{
    public const string ClientIdHeader = "X-LanForge-Client-Id";
    public const string OperationIdHeader = "X-LanForge-Operation-Id";

    /// <summary>Clave en HttpContext.Items donde se cachea el identificador de operacion resuelto.</summary>
    private const string OperationIdItemKey = "LanForge.OperationId";

    public static void MapLanForgeApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/hello", async (HttpContext context, CatalogService catalog, IServerIdentity identity, IServerLog log) =>
        {
            await catalog.RecordSeenClientForHeaderAsync(GetClientId(context), context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
            await LogAsync(log, context, "server.hello", LogSeverity.Debug, "Handshake recibido");
            return Results.Json(new HelloResponse(
                ProtocolVersion.Current,
                identity.Fingerprint.ToString(),
                identity.ServerName,
                identity.ServerId));
        });

        api.MapGet("/catalog", async (HttpContext context, CatalogService catalog, IServerLog log) =>
        {
            await catalog.RecordSeenClientForHeaderAsync(GetClientId(context), context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
            var entries = await catalog.ListCatalogAsync(context.RequestAborted);
            await catalog.RecordOperationAsync(new ServerOperation(
                GetOrCreateOperationId(context), GetClientId(context) ?? "anonymous", "catalog.sync",
                OperationStatus.Completed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, $"{entries.Count} entradas"), context.RequestAborted);
            await LogAsync(log, context, "catalog.served", LogSeverity.Info, $"Catálogo servido: {entries.Count} entradas");
            return Results.Json(entries);
        });

        api.MapGet("/manifest/{packageId}", async (string packageId, HttpContext context, CatalogService catalog, IServerLog log, CancellationToken cancellationToken) =>
        {
            var manifest = await catalog.GetManifestAsync(packageId, cancellationToken);
            if (manifest is null)
            {
                await LogAsync(log, context, "manifest.missing", LogSeverity.Warning, $"Manifiesto no encontrado: {packageId}");
                return Results.NotFound();
            }

            await LogAsync(log, context, "manifest.served", LogSeverity.Debug, $"Manifiesto servido: {packageId}");
            return Results.Bytes(manifest.ToJsonBytes(), contentType: "application/json");
        });

        api.MapGet("/covers/{fileName}", async (string fileName, ICoverStore covers, CancellationToken cancellationToken) =>
        {
            var stream = await covers.OpenReadAsync(fileName, cancellationToken);
            var contentType = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                : fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                : "image/jpeg";
            return stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
        });

        api.MapDelete("/games/{gameId}", async (string gameId, HttpContext context, CatalogService catalog, ICoverStore covers, IServerLog log, CancellationToken cancellationToken) =>
        {
            try
            {
                var deleted = await catalog.DeleteGameAsync(gameId, covers, cancellationToken);
                if (!deleted)
                {
                    await LogAsync(log, context, "game.delete.missing", LogSeverity.Warning, $"El juego '{gameId}' no existe en el catalogo.");
                    return Results.NotFound();
                }

                await catalog.RecordOperationAsync(new ServerOperation(
                    GetOrCreateOperationId(context), GetClientId(context) ?? "organizer", "game.delete",
                    OperationStatus.Completed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, gameId), cancellationToken);
                await LogAsync(log, context, "game.deleted", LogSeverity.Info, $"Juego eliminado del catalogo: {gameId}");
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                await LogAsync(log, context, "game.delete.invalid", LogSeverity.Warning, ex.Message);
                return Results.BadRequest();
            }
        });

        api.MapPost("/packages", async (HttpContext context, CatalogService catalog, CreatePackageUseCase createPackage, IServerLog log, CancellationToken cancellationToken) =>
        {
            var started = DateTimeOffset.UtcNow;
            var operationId = GetOrCreateOperationId(context);
            var clientId = GetClientId(context) ?? "organizer";

            CreatePackageRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<CreatePackageRequest>(SharedJson.Options, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "malformed_body", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("malformed_body", "El cuerpo de la solicitud no es un JSON válido.", []),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var created = await createPackage.CreateAsync(request, cancellationToken);

                await catalog.RecordOperationAsync(new ServerOperation(
                    operationId, clientId, "catalog.publish", OperationStatus.Completed, started, DateTimeOffset.UtcNow,
                    $"{created.GameId} v{created.Version}"), cancellationToken);
                await LogAsync(log, context, "package.created", LogSeverity.Info, $"Paquete publicado: {created.GameId} v{created.Version} ({created.FileCount} archivos)");
                return Results.Json(created, SharedJson.Options, statusCode: StatusCodes.Status201Created);
            }
            catch (PackageValidationException ex)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "validation_failed", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("validation_failed", ex.Message, ex.Errors),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (PackageVersionConflictException ex)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "version_conflict", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("version_conflict", ex.Message, []),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (EmptySourceDirectoryException ex)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "empty_directory", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("empty_directory", ex.Message, [new ApiFieldError("sourceDirectory", "empty_directory", ex.Message)]),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (PackageSourceNotFoundException ex)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "not_found", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("not_found", ex.Message, [new ApiFieldError("sourceDirectory", "not_found", ex.Message)]),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (PackageEntryPointNotFoundException ex)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "invalid_argument", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("invalid_argument", ex.Message, [new ApiFieldError(ex.Field, "not_found", ex.Message)]),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "conflict", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("conflict", ex.Message, []),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (ArgumentException ex)
            {
                await RecordPublishFailureAsync(catalog, log, context, operationId, clientId, "invalid_argument", ex.Message, started, cancellationToken);
                return Results.Json(
                    new ApiErrorResponse("invalid_argument", ex.Message, [new ApiFieldError(FieldForArgument(ex), "invalid_format", ex.Message)]),
                    SharedJson.Options,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        api.MapGet("/download/{packageId}", async (string packageId, HttpContext context, CatalogService catalog, IServerLog log, ICatalogStore store, CancellationToken cancellationToken) =>
        {
            var operationId = GetOrCreateOperationId(context);
            var clientId = GetClientId(context) ?? "anonymous";
            var detail = packageId;
            var started = DateTimeOffset.UtcNow;

            var archive = await catalog.OpenArchiveDownloadAsync(packageId, cancellationToken);
            if (archive is null)
            {
                await catalog.RecordOperationAsync(new ServerOperation(
                    operationId, clientId, "package.download", OperationStatus.Failed, started, DateTimeOffset.UtcNow, detail), CancellationToken.None);
                await LogAsync(log, context, "download.missing", LogSeverity.Warning, $"Descarga rechazada: {detail}");
                return Results.NotFound();
            }

            await catalog.RecordOperationAsync(new ServerOperation(
                operationId, clientId, "package.download", OperationStatus.Started, started, null, detail), CancellationToken.None);

            var reporter = new DownloadCompletionReporter(store, log);
            var transfer = new DownloadTransferTracker(context);
            var completion = new NotifyStream(archive.Stream, () =>
                reporter.Report(operationId, clientId, detail, started, IsAborted(transfer)));

            return Results.Stream(completion, contentType: "application/zip", fileDownloadName: $"{packageId}.zip", enableRangeProcessing: true);
        });

        api.MapGet("/download/{packageId}/{**path}", async (string packageId, string path, HttpContext context, CatalogService catalog, IServerLog log, ICatalogStore store, CancellationToken cancellationToken) =>
        {
            var operationId = GetOrCreateOperationId(context);
            var clientId = GetClientId(context) ?? "anonymous";
            var detail = $"{packageId}/{path}";
            var started = DateTimeOffset.UtcNow;
            var file = await catalog.OpenDownloadAsync(packageId, path, cancellationToken);
            if (file is null)
            {
                await catalog.RecordOperationAsync(new ServerOperation(
                    operationId, clientId, "package.download", OperationStatus.Failed, started, DateTimeOffset.UtcNow, detail), CancellationToken.None);
                await LogAsync(log, context, "download.missing", LogSeverity.Warning, $"Descarga rechazada: {detail}");
                return Results.NotFound();
            }

            await catalog.RecordOperationAsync(new ServerOperation(
                operationId, clientId, "package.download", OperationStatus.Started, started, null, detail), CancellationToken.None);

            var reporter = new DownloadCompletionReporter(store, log);
            var transfer = new DownloadTransferTracker(context);
            var completion = new NotifyStream(file.Stream, () =>
                reporter.Report(operationId, clientId, detail, started, IsAborted(transfer)));

            return Results.Stream(completion, contentType: "application/octet-stream", fileDownloadName: Path.GetFileName(file.Entry.Path), enableRangeProcessing: true);
        });
    }

    /// <summary>
    /// Indica si el cliente corto la transferencia. La senal es la cuenta de bytes: la
    /// respuesta declara cuantos bytes lleva y el servidor sabe cuantos salieron. Si no
    /// coinciden, la transferencia se corto a mitad.
    ///
    /// Antes se miraba <see cref="HttpContext.RequestAborted"/>: ese token se cancela en
    /// desorden respecto al fallo de escritura, asi que una descarga completa se podia
    /// registrar como cancelada y una cortada como completada. La cuenta de bytes no tiene
    /// esa ambiguedad y ademas respeta el Range (la respuesta de un rango declara la
    /// longitud del rango, no la del archivo).
    /// </summary>
    private static bool IsAborted(DownloadTransferTracker transfer) => !transfer.IsComplete;

    private static string? GetClientId(HttpContext context) =>
        context.Request.Headers.TryGetValue(ClientIdHeader, out var values) && !string.IsNullOrWhiteSpace(values.FirstOrDefault())
            ? values.First()
            : null;

    /// <summary>
    /// Resuelve el identificador de operacion de la peticion. Se resuelve una sola vez por
    /// peticion y se guarda en <see cref="HttpContext.Items"/>: la operacion registrada, la
    /// linea de log y la cabecera de respuesta llevan SIEMPRE el mismo identificador, sea el
    /// que propose el cliente o uno generado por el servidor.
    /// </summary>
    private static string GetOrCreateOperationId(HttpContext context)
    {
        if (context.Items.TryGetValue(OperationIdItemKey, out var cached) && cached is string cachedId)
        {
            return cachedId;
        }

        string operationId;
        if (context.Request.Headers.TryGetValue(OperationIdHeader, out var values) &&
            !string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            operationId = values.First()!;
        }
        else
        {
            operationId = Guid.NewGuid().ToString("n");
        }

        context.Items[OperationIdItemKey] = operationId;

        // La respuesta devuelve el identificador efectivo, para que el cliente pueda correlacionar
        // su peticion con la operacion y con el log, incluso si el id lo puso el cliente.
        if (!context.Response.HasStarted)
        {
            context.Response.Headers[OperationIdHeader] = operationId;
        }

        return operationId;
    }

    /// <summary>Registra el fallo de una publicación en las operaciones y en el log.</summary>
    private static async Task RecordPublishFailureAsync(
        CatalogService catalog,
        IServerLog log,
        HttpContext context,
        string operationId,
        string clientId,
        string code,
        string message,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        await catalog.RecordOperationAsync(new ServerOperation(
            operationId, clientId, "catalog.publish", OperationStatus.Failed, started, DateTimeOffset.UtcNow, code),
            cancellationToken);
        await LogAsync(log, context, $"package.create.{code}", LogSeverity.Warning, message);
    }

    /// <summary>Traduce el parametro de la excepcion al campo del formulario.</summary>
    private static string FieldForArgument(ArgumentException exception) => exception.ParamName switch
    {
        "entryPoint" => "entryPoint",
        "gameId" => "gameId",
        "version" => "version",
        "gameTitle" => "title",
        "files" => "sourceDirectory",
        _ => "request",
    };

    private static Task LogAsync(IServerLog log, HttpContext context, string eventName, LogSeverity severity, string message)
    {
        var clientId = GetClientId(context) ?? "anonymous";
        var operationId = GetOrCreateOperationId(context);
        return log.LogAsync(new LogEntry(DateTimeOffset.UtcNow, clientId, operationId, severity, eventName, message));
    }
}
