using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Shared.Logging;
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

        api.MapGet("/download/{packageId}", async (string packageId, HttpContext context, CatalogService catalog, IServerLog log, CancellationToken cancellationToken) =>
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

            var completion = new NotifyStream(archive.Stream, () =>
            {
                _ = Task.Run(async () =>
                {
                    await catalog.RecordOperationAsync(new ServerOperation(
                        operationId, clientId, "package.download", OperationStatus.Completed, started, DateTimeOffset.UtcNow, detail));
                    await log.LogAsync(new LogEntry(DateTimeOffset.UtcNow, clientId, operationId, LogSeverity.Info, "download.completed", detail));
                });
            });

            return Results.Stream(completion, contentType: "application/zip", fileDownloadName: $"{packageId}.zip", enableRangeProcessing: true);
        });

        api.MapGet("/download/{packageId}/{**path}", async (string packageId, string path, HttpContext context, CatalogService catalog, IServerLog log, CancellationToken cancellationToken) =>
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

            var completion = new NotifyStream(file.Stream, () =>
            {
                // Al cerrarse el stream (completado o cancelado) se finaliza la operación.
                _ = Task.Run(async () =>
                {
                    await catalog.RecordOperationAsync(new ServerOperation(
                        operationId, clientId, "package.download", OperationStatus.Completed, started, DateTimeOffset.UtcNow, detail));
                    await log.LogAsync(new LogEntry(DateTimeOffset.UtcNow, clientId, operationId, LogSeverity.Info, "download.completed", detail));
                });
            });

            return Results.Stream(completion, contentType: "application/octet-stream", fileDownloadName: Path.GetFileName(file.Entry.Path), enableRangeProcessing: true);
        });
    }

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

    private static Task LogAsync(IServerLog log, HttpContext context, string eventName, LogSeverity severity, string message)
    {
        var clientId = GetClientId(context) ?? "anonymous";
        var operationId = GetOrCreateOperationId(context);
        return log.LogAsync(new LogEntry(DateTimeOffset.UtcNow, clientId, operationId, severity, eventName, message));
    }
}
