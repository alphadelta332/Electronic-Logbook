using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using MudBlazor;
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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(MobileAirportCatalog.Default);
builder.Services.AddScoped<MobileAirportUpdates>();
builder.Services.AddScoped<BrowserNetworkStatus>();
builder.Services.AddScoped<BrowserPreviewUpdateState>();
builder.Services.AddScoped<INetworkStatus>(sp => sp.GetRequiredService<BrowserNetworkStatus>());
builder.Services.AddScoped<MobileSupabaseHostedSyncClient>();
builder.Services.AddScoped<IHostedLogbookAuthenticator>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IMobileGoogleHostedAuthenticator>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IHostedLogbookLedger>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IHostedConfigurationRevisionLedger>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IMobileHostedRecoveryClient>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IMobileRecoveryEnvelopeService>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<IMobileReplacementRecoveryClient>(sp => sp.GetRequiredService<MobileSupabaseHostedSyncClient>());
builder.Services.AddScoped<MobileReplacementRecoveryWorkflow>();
builder.Services.AddScoped<IMobileReplacementRecoveryWorkflow>(sp => sp.GetRequiredService<MobileReplacementRecoveryWorkflow>());
builder.Services.AddScoped<MobileConnectionRecoveryWorkflow>();
builder.Services.AddScoped<MobileLogbookSession>();
builder.Services.AddMudServices(configuration =>
{
    configuration.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
});

var host = builder.Build();
await host.Services.GetRequiredService<MobileAirportUpdates>().LoadCachedAsync();
await host.RunAsync();
