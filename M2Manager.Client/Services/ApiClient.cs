using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;

namespace M2Manager.Client.Services;

/// <summary>Błąd zwrócony przez API — z komunikatem gotowym do pokazania użytkownikowi.</summary>
public sealed class ApiException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
}

/// <summary>
/// Jedyne miejsce, w którym klient rozmawia z API. Sesja jedzie w cookie HttpOnly,
/// więc przeglądarka dołącza ją sama do żądań same-origin.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = AppJson.Options;

    // ---------------------------------------------------------------- auth

    public Task<AuthUserDto?> GetCurrentUserAsync() =>
        GetOrDefaultAsync<AuthUserDto>("api/auth/me");

    public async Task<AuthUserDto> LoginAsync(LoginRequest request) =>
        await PostAsync<LoginRequest, AuthUserDto>("api/auth/login", request);

    public async Task LogoutAsync() =>
        await SendAsync(HttpMethod.Post, "api/auth/logout");

    // ---------------------------------------------------------------- mieszkania i pomieszczenia

    public Task<List<PropertyDto>> GetPropertiesAsync() =>
        GetAsync<List<PropertyDto>>("api/properties");

    public Task<PropertyDto> CreatePropertyAsync(PropertyUpsertDto dto) =>
        PostAsync<PropertyUpsertDto, PropertyDto>("api/properties", dto);

    public Task<PropertyDto> UpdatePropertyAsync(int id, PropertyUpsertDto dto) =>
        PutAsync<PropertyUpsertDto, PropertyDto>($"api/properties/{id}", dto);

    public Task DeletePropertyAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/properties/{id}");

    public Task<List<RoomDto>> GetRoomsAsync(int propertyId) =>
        GetAsync<List<RoomDto>>($"api/properties/{propertyId}/rooms");

    public Task<RoomDto> CreateRoomAsync(int propertyId, RoomUpsertDto dto) =>
        PostAsync<RoomUpsertDto, RoomDto>($"api/properties/{propertyId}/rooms", dto);

    public Task<RoomDto> UpdateRoomAsync(int propertyId, int roomId, RoomUpsertDto dto) =>
        PutAsync<RoomUpsertDto, RoomDto>($"api/properties/{propertyId}/rooms/{roomId}", dto);

    public Task DeleteRoomAsync(int propertyId, int roomId) =>
        SendAsync(HttpMethod.Delete, $"api/properties/{propertyId}/rooms/{roomId}");

    public Task<RoomOpeningDto> CreateOpeningAsync(int roomId, RoomOpeningUpsertDto dto) =>
        PostAsync<RoomOpeningUpsertDto, RoomOpeningDto>($"api/rooms/{roomId}/openings", dto);

    public Task<RoomOpeningDto> UpdateOpeningAsync(int roomId, int openingId, RoomOpeningUpsertDto dto) =>
        PutAsync<RoomOpeningUpsertDto, RoomOpeningDto>($"api/rooms/{roomId}/openings/{openingId}", dto);

    public Task DeleteOpeningAsync(int roomId, int openingId) =>
        SendAsync(HttpMethod.Delete, $"api/rooms/{roomId}/openings/{openingId}");

    public Task<PropertyAreasDto> GetAreasAsync(int propertyId) =>
        GetAsync<PropertyAreasDto>($"api/properties/{propertyId}/areas");

    public Task<PropertyAreasDto> SaveLayoutAsync(int propertyId, LayoutSaveRequest request) =>
        PutAsync<LayoutSaveRequest, PropertyAreasDto>($"api/properties/{propertyId}/layout", request);

    // ---------------------------------------------------------------- faktury

    public Task<List<LookupDto>> GetExpenseCategoriesAsync() =>
        GetAsync<List<LookupDto>>("api/expense-categories");

    public Task<LookupDto> CreateExpenseCategoryAsync(LookupUpsertDto dto) =>
        PostAsync<LookupUpsertDto, LookupDto>("api/expense-categories", dto);

    public Task<PagedResult<InvoiceDto>> GetInvoicesAsync(InvoiceQuery query)
    {
        var parameters = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };

        if (query.PropertyId.HasValue)
        {
            parameters.Add($"propertyId={query.PropertyId}");
        }

        if (query.RoomId.HasValue)
        {
            parameters.Add($"roomId={query.RoomId}");
        }

        if (query.CategoryId.HasValue)
        {
            parameters.Add($"categoryId={query.CategoryId}");
        }

        if (query.From.HasValue)
        {
            parameters.Add($"from={query.From:yyyy-MM-dd}");
        }

        if (query.To.HasValue)
        {
            parameters.Add($"to={query.To:yyyy-MM-dd}");
        }

        return GetAsync<PagedResult<InvoiceDto>>($"api/invoices?{string.Join('&', parameters)}");
    }

    public Task<InvoiceDto> GetInvoiceAsync(int id) =>
        GetAsync<InvoiceDto>($"api/invoices/{id}");

    /// <summary>Upload zdjęcia z telefonu. OCR dzieje się po stronie serwera w tym samym żądaniu.</summary>
    public async Task<InvoiceDto> UploadInvoiceAsync(
        Stream content,
        string fileName,
        string contentType,
        int propertyId,
        int? roomId,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(propertyId.ToString()), "propertyId");

        if (roomId.HasValue)
        {
            form.Add(new StringContent(roomId.Value.ToString()), "roomId");
        }

        using var response = await http.PostAsync("api/invoices/upload", form, ct);
        return await ReadAsync<InvoiceDto>(response);
    }

    public Task<InvoiceDto> UpdateInvoiceAsync(int id, InvoiceUpsertDto dto) =>
        PutAsync<InvoiceUpsertDto, InvoiceDto>($"api/invoices/{id}", dto);

    public Task DeleteInvoiceAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/invoices/{id}");

    /// <summary>Przenosi wybrane pozycje faktury na listę zakupów, wiążąc je z dokumentem.</summary>
    public Task<List<ShoppingItemDto>> CreateShoppingItemsFromInvoiceAsync(
        int invoiceId,
        CreateShoppingItemsFromInvoiceRequest request) =>
        PostAsync<CreateShoppingItemsFromInvoiceRequest, List<ShoppingItemDto>>(
            $"api/invoices/{invoiceId}/shopping-items", request);

    // ---------------------------------------------------------------- lista zakupów

    public Task<List<ShoppingItemDto>> GetShoppingItemsAsync(string queryString) =>
        GetAsync<List<ShoppingItemDto>>($"api/shopping{queryString}");

    public Task<ShoppingSummaryDto> GetShoppingSummaryAsync(int? propertyId) =>
        GetAsync<ShoppingSummaryDto>(propertyId.HasValue
            ? $"api/shopping/summary?propertyId={propertyId}"
            : "api/shopping/summary");

    public Task<ShoppingItemDto> CreateShoppingItemAsync(ShoppingItemUpsertDto dto) =>
        PostAsync<ShoppingItemUpsertDto, ShoppingItemDto>("api/shopping", dto);

    public Task<ShoppingItemDto> UpdateShoppingItemAsync(int id, ShoppingItemUpsertDto dto) =>
        PutAsync<ShoppingItemUpsertDto, ShoppingItemDto>($"api/shopping/{id}", dto);

    public Task DeleteShoppingItemAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/shopping/{id}");

    public Task<List<LookupDto>> GetShoppingCategoriesAsync() =>
        GetAsync<List<LookupDto>>("api/shopping-categories");

    public Task<LookupDto> CreateShoppingCategoryAsync(LookupUpsertDto dto) =>
        PostAsync<LookupUpsertDto, LookupDto>("api/shopping-categories", dto);

    public async Task<ShoppingImportResultDto> ImportShoppingAsync(
        Stream content,
        string fileName,
        int propertyId,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(propertyId.ToString()), "propertyId");

        using var response = await http.PostAsync("api/shopping/import", form, ct);
        return await ReadAsync<ShoppingImportResultDto>(response);
    }

    // ---------------------------------------------------------------- raporty

    public Task<DashboardDto> GetDashboardAsync() =>
        GetAsync<DashboardDto>("api/reports/dashboard");

    public Task<ReportSummaryDto> GetReportSummaryAsync(int propertyId, int year, int? month)
    {
        var url = $"api/reports/summary?propertyId={propertyId}&year={year}";
        if (month.HasValue)
        {
            url += $"&month={month}";
        }

        return GetAsync<ReportSummaryDto>(url);
    }

    // ---------------------------------------------------------------- warstwa transportowa

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await http.GetAsync(url);
        return await ReadAsync<T>(response);
    }

    /// <summary>Jak <see cref="GetAsync{T}"/>, ale brak sesji nie jest tu błędem (używane przez /api/auth/me).</summary>
    private async Task<T?> GetOrDefaultAsync<T>(string url)
    {
        try
        {
            using var response = await http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            // Gdy pod tym adresem nie stoi API (np. odpalono sam projekt Client albo proxy
            // zwróciło stronę HTML), traktujemy to jak brak sesji — użytkownik zobaczy logowanie,
            // a nie biały ekran z nieobsłużonym wyjątkiem.
            return default;
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(url, body, Json);
        return await ReadAsync<TResponse>(response);
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(string url, TRequest body)
    {
        using var response = await http.PutAsJsonAsync(url, body, Json);
        return await ReadAsync<TResponse>(response);
    }

    private async Task SendAsync(HttpMethod method, string url)
    {
        using var request = new HttpRequestMessage(method, url);
        using var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response);
        }

        T? value;
        try
        {
            value = await response.Content.ReadFromJsonAsync<T>(Json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Najczęstsza przyczyna: pod tym adresem nie ma API i dostaliśmy stronę HTML.
            throw new ApiException(
                "Serwer zwrócił odpowiedź, która nie jest JSON-em. Sprawdź, czy aplikacja działa pod adresem API.",
                response.StatusCode);
        }

        return value ?? throw new ApiException("Serwer zwrócił pustą odpowiedź.", response.StatusCode);
    }

    /// <summary>Wyciąga czytelny komunikat z ProblemDetails, walidacji albo prostego { message }.</summary>
    private static async Task<ApiException> BuildExceptionAsync(HttpResponseMessage response)
    {
        var status = response.StatusCode;

        if (status == HttpStatusCode.Unauthorized)
        {
            return new ApiException("Sesja wygasła — zaloguj się ponownie.", status);
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException)
        {
            // Brak treści błędu nie jest problemem — poniżej mamy komunikat zapasowy.
        }

        var message = ExtractMessage(body) ?? status switch
        {
            HttpStatusCode.NotFound => "Nie znaleziono zasobu.",
            HttpStatusCode.Conflict => "Taki wpis już istnieje.",
            HttpStatusCode.BadRequest => "Serwer odrzucił dane.",
            _ => $"Błąd serwera ({(int)status})."
        };

        return new ApiException(message, status);
    }

    private static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            // ValidationProblemDetails: { errors: { Pole: ["komunikat"] } }
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors
                    .EnumerateObject()
                    .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                        ? p.Value.EnumerateArray().Select(v => v.GetString())
                        : [p.Value.GetString()])
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }

            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                return title.GetString();
            }
        }
        catch (JsonException)
        {
            // Odpowiedź nie jest JSON-em — użyjemy komunikatu zapasowego.
        }

        return null;
    }
}
