using System.Net.Http.Headers;
using System.Net.Http.Json;
using LanForge.Client.Application;
using LanForge.Shared;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Protocol;

namespace LanForge.Client.Infrastructure;

/// <summary>Acceso HTTP real al servidor con headers de correlación (clientId/operationId).</summary>
public sealed class HttpServerApi : IServerApi, IDisposable
{
    private readonly HttpClient _http;

    public string BaseUrl { get; }
    public string ClientId { get; }

    public HttpServerApi(string baseUrl, string clientId, HttpMessageHandler? handler = null, string? operationId = null)
    {
        BaseUrl = NormalizeBaseUrl(baseUrl);
        ClientId = clientId;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(BaseUrl);
        _http.Timeout = TimeSpan.FromMinutes(10); // paquetes grandes por LAN
        _http.DefaultRequestHeaders.Add("X-LanForge-Client-Id", clientId);
        if (operationId is not null)
        {
            _http.DefaultRequestHeaders.Add("X-LanForge-Operation-Id", operationId);
        }
    }

    public async Task<HelloResponse> GetHelloAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<HelloResponse>("/api/v1/hello", SharedJson.Options, cancellationToken)
        ?? throw new LanForgeClientException("Respuesta vacía de /hello.");

    public async Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<List<CatalogEntry>>("/api/v1/catalog", SharedJson.Options, cancellationToken)
        ?? throw new LanForgeClientException("Respuesta vacía de /catalog.");

    public async Task<SignedPackageManifest> GetManifestAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var bytes = await _http.GetByteArrayAsync($"/api/v1/manifest/{Uri.EscapeDataString(packageId)}", cancellationToken);
        return SignedPackageManifest.FromJson(bytes)
            ?? throw new LanForgeClientException($"Manifiesto inválido para '{packageId}'.");
    }

    public async Task<Stream> OpenDownloadAsync(
        string packageId, string relativePath, long resumeFromBytes, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/download/{Uri.EscapeDataString(packageId)}/{relativePath}");
        if (resumeFromBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeFromBytes, null);
        }

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task<Stream> OpenArchiveDownloadAsync(
        string packageId, long resumeFromBytes, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/download/{Uri.EscapeDataString(packageId)}");
        if (resumeFromBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeFromBytes, null);
        }

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task<byte[]?> DownloadCoverAsync(string fileName, CancellationToken cancellationToken = default)
    {
        // El nombre ya viene validado del catálogo (lo genera el servidor); restringimos igualmente.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            return null;
        }

        var response = await _http.GetAsync($"/api/v1/covers/{Uri.EscapeDataString(fileName)}", cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(cancellationToken) : null;
    }

    public void Dispose() => _http.Dispose();

    private static string NormalizeBaseUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var normalized = baseUrl.TrimEnd('/');
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        return normalized;
    }
}
