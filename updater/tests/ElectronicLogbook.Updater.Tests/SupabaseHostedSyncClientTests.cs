using System.Net;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class SupabaseHostedSyncClientTests
{
    [Fact]
    public async Task ReadMissingOperationsUsesTheLastReturnedRevisionAsItsPageCursor()
    {
        using var client = new SupabaseHostedSyncClient(
            new Uri("https://pilot.supabase.co"),
            "anon-key",
            new HostedAccountId("acct_10000000000000000000000000000001"),
            new DeviceId("dev_40000000000000000000000000000001"),
            new PortableHostedCredential(
                "access-token",
                "refresh-token",
                DateTimeOffset.Parse("2030-01-01T00:00:00Z")),
            httpMessageHandler: new PageBoundaryHandler());

        var page = await client.ReadMissingOperationsAsync(
            new LogbookId("log_20000000000000000000000000000001"),
            afterHostedRevision: 0,
            pageSize: 200);

        Assert.Single(page.Operations);
        Assert.Equal(200, page.ThroughHostedRevision);
        Assert.True(page.HasMore);
    }

    private sealed class PageBoundaryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("/rest/v1/rpc/read_missing_operations", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [{
                      "revision": 200,
                      "operation_id": "60000000-0000-0000-0000-000000000200",
                      "portable_revision_id": "rev_50000000000000000000000000000200",
                      "entry_id": "ent_page_boundary",
                      "base_revision": null,
                      "parent_revision_ids": [],
                      "author_device_id": "40000000-0000-0000-0000-000000000001",
                      "operation_type": "operation",
                      "operation_format_version": 1,
                      "payload_ciphertext": "AQIDBA==",
                      "payload_nonce": "AAAAAAAAAAAAAAAA",
                      "payload_tag": "AAAAAAAAAAAAAAAAAAAAAA==",
                      "payload_hash": "0000000000000000000000000000000000000000000000000000000000000000",
                      "client_created_at": "2026-08-22T00:00:00Z",
                      "received_at": "2026-08-22T00:00:01Z",
                      "highest_revision": 344,
                      "has_more": true
                    }]
                    """)
            });
        }
    }
}
