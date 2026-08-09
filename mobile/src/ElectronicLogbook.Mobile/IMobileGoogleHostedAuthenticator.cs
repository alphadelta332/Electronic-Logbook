using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public interface IMobileGoogleHostedAuthenticator
{
    ValueTask<HostedSyncSession> SignInWithGoogleAsync(CancellationToken cancellationToken = default);

    ValueTask<HostedSyncSession> LinkGoogleIdentityAsync(CancellationToken cancellationToken = default);
}
