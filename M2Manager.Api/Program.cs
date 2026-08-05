using M2Manager.Api.Configuration;
using M2Manager.Api.Data;
using M2Manager.Api.Endpoints;
using M2Manager.Api.Services;
using M2Manager.Shared;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

// Narzędzie pomocnicze: dotnet run --project M2Manager.Api -- hash-password "moje-haslo"
if (args.Length >= 2 && args[0].Equals("hash-password", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(PasswordHasher.Hash(args[1]));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Render (i większość PaaS-ów) podaje port w zmiennej PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

QuestPDF.Settings.License = LicenseType.Community;

// ---------------------------------------------------------------- konfiguracja
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<R2Options>(builder.Configuration.GetSection(R2Options.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection(UploadOptions.SectionName));

builder.Services.ConfigureHttpJsonOptions(options => AppJson.Configure(options.SerializerOptions));

// ---------------------------------------------------------------- baza danych
var connectionString = DatabaseConnection.Resolve(builder.Configuration);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Brak połączenia z bazą. Ustaw ConnectionStrings__DefaultConnection albo DATABASE_URL " +
        "(connection string z Neon). Szczegóły w README.md.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        // Darmowy Neon usypia bazę, więc ponawiamy — ale krótko.
        // Domyślne 6 prób z narastającym opóźnieniem potrafi zawiesić żądanie na kilka minut,
        // gdy baza jest naprawdę niedostępna, a użytkownik ma wtedy zobaczyć błąd, nie kręcące się kółko.
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

// ---------------------------------------------------------------- uwierzytelnianie
var authSection = builder.Configuration.GetSection(AuthOptions.SectionName);
var sessionDays = Math.Max(1, authSection.GetValue("SessionDays", 30));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "m2manager.auth";
        options.Cookie.HttpOnly = true;

        // SameSite=Lax sprawia, że żądania cross-site nie niosą sesji — to nasza ochrona przed CSRF.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(sessionDays);
        options.SlidingExpiration = true;

        // API nigdy nie przekierowuje na stronę logowania — od nawigacji jest Blazor.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------- serwisy aplikacyjne
var r2Options = builder.Configuration.GetSection(R2Options.SectionName).Get<R2Options>() ?? new R2Options();

if (r2Options.IsConfigured)
{
    builder.Services.AddSingleton<IObjectStorage, R2ObjectStorage>();
}
else
{
    builder.Services.AddSingleton<IObjectStorage, LocalObjectStorage>();
}

var anthropicOptions = builder.Configuration.GetSection(AnthropicOptions.SectionName).Get<AnthropicOptions>()
                       ?? new AnthropicOptions();

if (anthropicOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IOcrService, ClaudeOcrService>((serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<AnthropicOptions>>().Value;

        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(Math.Max(10, options.TimeoutSeconds));
        client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", options.ApiVersion);
    });
}
else
{
    builder.Services.AddSingleton<IOcrService, DisabledOcrService>();
}

builder.Services.AddScoped<ShoppingImportService>();
builder.Services.AddSingleton<ExcelExportService>();
builder.Services.AddSingleton<PdfExportService>();

// Render terminuje TLS na proxy — bez tego aplikacja widzi „http” i psuje ciasteczka Secure.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// ---------------------------------------------------------------- migracje i dane startowe
if (builder.Configuration.GetValue("Database:AutoMigrate", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
        logger.LogInformation("Migracje i dane startowe gotowe.");
    }
    catch (Exception ex)
    {
        // Aplikacja startuje mimo błędu, żeby dało się zobaczyć logi i poprawić konfigurację.
        logger.LogError(ex, "Nie udało się przygotować bazy danych. Sprawdź connection string.");
    }
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.Map("/error", () => Results.Problem("Wystąpił nieoczekiwany błąd serwera."));

app.MapAuthEndpoints();
app.MapPropertyEndpoints();
app.MapInvoiceEndpoints();
app.MapShoppingEndpoints();
app.MapReportEndpoints();

// Wszystko, co nie jest API ani plikiem statycznym, obsługuje Blazor (routing po stronie klienta).
app.MapFallbackToFile("index.html");

app.Run();
