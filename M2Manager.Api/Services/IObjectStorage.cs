namespace M2Manager.Api.Services;

/// <summary>Abstrakcja nad miejscem przechowywania zdjęć faktur.</summary>
public interface IObjectStorage
{
    /// <summary>Czy działamy na zdalnym storage (R2), czy na lokalnym dysku (tryb dev).</summary>
    bool IsRemote { get; }

    Task UploadAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default);

    /// <summary>Adres do podglądu zdjęcia. Dla R2 jest to presigned URL o ograniczonej ważności.</summary>
    Task<string?> GetViewUrlAsync(string objectKey, CancellationToken ct = default);

    Task DeleteAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Pobranie zawartości — używane przez OCR oraz lokalny endpoint podglądu.</summary>
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Buduje unikalny klucz obiektu, np. „invoices/2026/07/guid.jpg”.</summary>
    static string BuildObjectKey(string originalFileName, DateTime utcNow)
    {
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10)
        {
            extension = ".jpg";
        }

        return $"invoices/{utcNow:yyyy}/{utcNow:MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
    }
}
