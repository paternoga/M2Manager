namespace M2Manager.Api.Configuration;

/// <summary>Dane wspólnego konta. Hasło trzymamy jako hash PBKDF2 albo — awaryjnie — jako tekst z env.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = "dom";

    /// <summary>
    /// Hash hasła w formacie „pbkdf2$iteracje$saltBase64$hashBase64”.
    /// Wygenerujesz go poleceniem opisanym w README.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Hasło jawne — wyłącznie dla wygody przy pierwszym uruchomieniu lokalnym.
    /// Na produkcji ustaw <see cref="PasswordHash"/>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Ile dni ma żyć cookie sesji.</summary>
    public int SessionDays { get; set; } = 30;
}

/// <summary>Konfiguracja Cloudflare R2 (S3-compatible).</summary>
public sealed class R2Options
{
    public const string SectionName = "R2";

    public string? AccountId { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? BucketName { get; set; }

    /// <summary>Ile minut ma być ważny presigned URL do podglądu zdjęcia.</summary>
    public int PresignedUrlMinutes { get; set; } = 60;

    /// <summary>Pełny endpoint; domyślnie budowany z AccountId.</summary>
    public string? ServiceUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessKeyId) &&
        !string.IsNullOrWhiteSpace(SecretAccessKey) &&
        !string.IsNullOrWhiteSpace(BucketName) &&
        (!string.IsNullOrWhiteSpace(AccountId) || !string.IsNullOrWhiteSpace(ServiceUrl));

    public string ResolveServiceUrl() =>
        !string.IsNullOrWhiteSpace(ServiceUrl)
            ? ServiceUrl!
            : $"https://{AccountId}.r2.cloudflarestorage.com";
}

/// <summary>Konfiguracja Gemini API (Google AI) używanego do odczytu faktur.</summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Klucz z aistudio.google.com/apikey.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model z obsługą obrazu. Flash jest tani, szybki i w zupełności wystarcza do paragonów.
    /// Uwaga: starsze modele (np. gemini-2.5-flash) Google wycofuje dla nowych kont —
    /// widnieją wtedy na liście `/models`, ale `generateContent` zwraca 404.
    /// </summary>
    public string Model { get; set; } = "gemini-3.6-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string ApiVersion { get; set; } = "v1beta";
    public int MaxOutputTokens { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 90;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Ustawienia uploadu zdjęć faktur.</summary>
public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>Maksymalny rozmiar zdjęcia w megabajtach.</summary>
    public int MaxFileSizeMb { get; set; } = 15;

    public string[] AllowedContentTypes { get; set; } =
        ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif", "application/pdf"];

    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;

    /// <summary>Gdy R2 nie jest skonfigurowane, zapisujemy pliki lokalnie w tym katalogu (tryb dev).</summary>
    public string LocalStoragePath { get; set; } = "App_Data/uploads";
}
