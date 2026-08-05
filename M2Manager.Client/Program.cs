using System.Globalization;
using M2Manager.Client;
using M2Manager.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Cały interfejs jest po polsku — liczby i daty też mają się tak formatować.
var polish = new CultureInfo("pl-PL");
CultureInfo.DefaultThreadCurrentCulture = polish;
CultureInfo.DefaultThreadCurrentUICulture = polish;

// API jest serwowane spod tego samego adresu, więc cookie sesji jedzie automatycznie.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(3) // upload zdjęcia + OCR potrafi chwilę potrwać
});

builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AppState>();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CookieAuthenticationStateProvider>());

await builder.Build().RunAsync();
