using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using M2Manager.Api.Configuration;
using Microsoft.Extensions.Options;

namespace M2Manager.Api.Services;

/// <summary>
/// Cloudflare R2 przez API zgodne z S3. Bucket jest prywatny — podgląd zdjęć
/// odbywa się wyłącznie przez presigned URL o ograniczonej ważności.
/// </summary>
public sealed class R2ObjectStorage : IObjectStorage, IDisposable
{
    private readonly R2Options _options;
    private readonly ILogger<R2ObjectStorage> _logger;
    private readonly AmazonS3Client _client;

    public R2ObjectStorage(IOptions<R2Options> options, ILogger<R2ObjectStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = _options.ResolveServiceUrl(),
            ForcePathStyle = true,

            // R2 nie obsługuje domyślnych sum kontrolnych AWS SDK v4 — bez tego upload wywala się na 400.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,

            // R2 ignoruje region, ale SDK musi jakiś dostać do podpisu.
            AuthenticationRegion = "auto"
        };

        _client = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey),
            config);
    }

    public bool IsRemote => true;

    public async Task UploadAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true
        };

        await _client.PutObjectAsync(request, ct);
        _logger.LogInformation("Zapisano obiekt {ObjectKey} w R2.", objectKey);
    }

    public Task<string?> GetViewUrlAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(Math.Max(1, _options.PresignedUrlMinutes))
            };

            return Task.FromResult<string?>(_client.GetPreSignedURL(request));
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się wygenerować presigned URL dla {ObjectKey}.", objectKey);
            return Task.FromResult<string?>(null);
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return;
        }

        try
        {
            await _client.DeleteObjectAsync(_options.BucketName, objectKey, ct);
        }
        catch (AmazonS3Exception ex)
        {
            // Brak pliku w R2 nie może blokować skasowania rekordu w bazie.
            _logger.LogWarning(ex, "Nie udało się usunąć obiektu {ObjectKey} z R2.", objectKey);
        }
    }

    public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_options.BucketName, objectKey, ct);

            // Kopiujemy do pamięci, żeby móc bezpiecznie zamknąć odpowiedź HTTP.
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się pobrać obiektu {ObjectKey} z R2.", objectKey);
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
}
