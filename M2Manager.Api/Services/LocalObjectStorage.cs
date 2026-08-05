using M2Manager.Api.Configuration;
using Microsoft.Extensions.Options;

namespace M2Manager.Api.Services;

/// <summary>
/// Zapis na lokalny dysk — używany, gdy R2 nie jest skonfigurowane.
/// Dzięki temu aplikacja daje się uruchomić i przetestować bez konta w Cloudflare.
/// Nie nadaje się na Render (dysk kontenera jest ulotny) — to tryb wyłącznie deweloperski.
/// </summary>
public sealed class LocalObjectStorage : IObjectStorage
{
    private readonly string _root;
    private readonly ILogger<LocalObjectStorage> _logger;

    public LocalObjectStorage(
        IOptions<UploadOptions> options,
        IHostEnvironment environment,
        ILogger<LocalObjectStorage> logger)
    {
        _logger = logger;

        var configured = options.Value.LocalStoragePath;
        _root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);

        Directory.CreateDirectory(_root);
        _logger.LogWarning(
            "R2 nie jest skonfigurowane — zdjęcia faktur trafiają na dysk lokalny: {Path}. " +
            "Na produkcji ustaw zmienne R2__*.", _root);
    }

    public bool IsRemote => false;

    public async Task UploadAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default)
    {
        var path = ResolvePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    /// <summary>Lokalnie serwujemy plik własnym endpointem — presigned URL nie ma tu sensu.</summary>
    public Task<string?> GetViewUrlAsync(string objectKey, CancellationToken ct = default) =>
        Task.FromResult<string?>($"/api/files/{Uri.EscapeDataString(objectKey)}");

    public Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        var path = ResolvePath(objectKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default)
    {
        var path = ResolvePath(objectKey);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    /// <summary>Blokuje wyjście poza katalog uploadów (np. „../../appsettings.json”).</summary>
    private string ResolvePath(string objectKey)
    {
        var normalized = objectKey.Replace('\\', '/').TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(_root, normalized));
        var rootFull = Path.GetFullPath(_root);

        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Nieprawidłowy klucz obiektu: {objectKey}");
        }

        return combined;
    }
}
