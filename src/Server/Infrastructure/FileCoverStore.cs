using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LanForge.Server.Application;

namespace LanForge.Server.Infrastructure;

/// <summary>
/// Portadas en disco: {root}/{gameId}-{sha256}{ext}. El hash cambia cuando cambia
/// la imagen, de modo que los clientes no reutilizan una portada antigua de caché.
/// Los nombres servidos por HTTP se validan estrictamente.
/// </summary>
public sealed class FileCoverStore : ICoverStore
{
    private static readonly Regex FileNamePattern = new(
        "^[a-zA-Z0-9_-]+\\.(png|jpg|jpeg|webp)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _root;

    public FileCoverStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public Task<string> SaveAsync(string gameId, string sourceImagePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(sourceImagePath);
        if (!CoverImageRules.IsAllowedExtension(extension))
        {
            throw new ArgumentException(
                $"Formato de imagen no soportado: '{extension}'. Usa png, jpg, jpeg o webp.",
                nameof(sourceImagePath));
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceImagePath))).ToLowerInvariant();
        var fileName = $"{gameId}-{hash}{extension.ToLowerInvariant()}";

        // El nombre que se guarda tiene que ser el mismo que la lectura y el borrado aceptan:
        // nada de rutas, nada de traversal, nada de nombres que el servidor no podra releer.
        if (!FileNamePattern.IsMatch(fileName))
        {
            throw new ArgumentException(
                $"El nombre de portada '{fileName}' no es valido: usa el identificador del juego sin rutas.",
                nameof(gameId));
        }

        File.Copy(sourceImagePath, Path.Combine(_root, fileName), overwrite: true);
        return Task.FromResult(fileName);
    }

    public Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        // Solo nombres planos permitidos: sin rutas, sin traversal.
        if (!FileNamePattern.IsMatch(fileName))
        {
            return Task.FromResult<Stream?>(null);
        }

        var path = Path.Combine(_root, fileName);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        // Mismo nombre plano validado que en la lectura: nada de rutas ni traversal.
        if (!FileNamePattern.IsMatch(fileName))
        {
            return Task.FromResult(false);
        }

        var path = Path.Combine(_root, fileName);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }
}
