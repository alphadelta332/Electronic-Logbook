using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class BrowserNetworkStatus(IJSRuntime jsRuntime) : INetworkStatus
{
    public async ValueTask<NetworkAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var isOnline = await jsRuntime.InvokeAsync<bool>(
            "electronicLogbookNetwork.isOnline",
            cancellationToken);
        return new NetworkAvailability(isOnline);
    }
}
