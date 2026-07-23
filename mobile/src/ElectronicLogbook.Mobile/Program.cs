using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ElectronicLogbook.Mobile;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserLogbookStore>();
builder.Services.AddScoped<BrowserPackageKeyStore>();
builder.Services.AddScoped<BrowserFileStore>();
builder.Services.AddScoped<BrowserUiPreferencesStore>();
builder.Services.AddScoped<MobileLogbookSession>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
