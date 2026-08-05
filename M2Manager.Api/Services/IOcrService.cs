using M2Manager.Shared.Dtos;

namespace M2Manager.Api.Services;

/// <summary>Odczyt danych z faktury/paragonu. Wynik jest zawsze propozycją do korekty przez człowieka.</summary>
public interface IOcrService
{
    bool IsEnabled { get; }

    Task<OcrExtractionResult> ExtractAsync(
        byte[] fileBytes,
        string contentType,
        OcrCategories categories,
        CancellationToken ct = default);
}

/// <summary>Zaślepka używana, gdy nie ustawiono klucza Gemini — upload nadal działa, tylko bez podpowiedzi.</summary>
public sealed class DisabledOcrService : IOcrService
{
    public bool IsEnabled => false;

    public Task<OcrExtractionResult> ExtractAsync(
        byte[] fileBytes,
        string contentType,
        OcrCategories categories,
        CancellationToken ct = default) =>
        Task.FromResult(OcrExtractionResult.Failed(
            "Automatyczny odczyt jest wyłączony (brak Gemini__ApiKey). Uzupełnij dane ręcznie."));
}
