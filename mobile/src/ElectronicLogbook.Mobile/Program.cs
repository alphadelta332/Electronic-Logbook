using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserLogbookStore>();
builder.Services.AddScoped<BrowserPackageKeyStore>();
builder.Services.AddScoped<BrowserHostedCredentialStore>();
builder.Services.AddScoped<BrowserGoogleCredentialProvider>();
builder.Services.AddScoped<BrowserFileStore>();
builder.Services.AddScoped<BrowserUiPreferencesStore>();
builder.Services.AddScoped<MobileUiPreferenceState>();
builder.Services.AddScoped<ISyncClock>(_ => SystemSyncClock.Instance);
builder.Services.AddScoped<BrowserNetworkStatus>();
builder.Services.AddScoped<INetworkStatus>(sp => sp.GetRequiredService<BrowserNetworkStatus>());
builder.Services.AddScoped<MobileSupabaseHostedSyncClient>();
builder.Services.AddScoped<IHostedLogbookAuthenticator>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IMobileGoogleHostedAuthenticator>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IHostedLogbookLedger>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IMobileHostedRecoveryClient>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<MobileConnectionRecoveryWorkflow>();
builder.Services.AddScoped<MobileLogbookSession>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
