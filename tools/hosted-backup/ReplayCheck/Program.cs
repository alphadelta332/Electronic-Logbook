using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Portable;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    if (args is ["seed"])
    {
        var logbookGuid = Guid.NewGuid();
        var logbook = new LogbookId("log_" + logbookGuid.ToString("N"));
        var device = DeviceId.New();
        var field = new CustomFieldDefinition(CustomFieldId.New(), "Synthetic backup field", 1);
        var key = PortableLogbookKey.Generate();
        var kek = RandomNumberGenerator.GetBytes(32);
        var entryA = EntryId.New();
        var entryB = EntryId.New();
        var revisions = Enumerable.Range(0, 6).Select(_ => RevisionId.New()).ToArray();
        var date = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026, Month = 10, Day = 17, Type = "C172", Reg = "VH-TST", From = "YSBK", To = "YSCN",
            SeCommandDay = 1m, CustomFields = new Dictionary<CustomFieldId, string?> { [field.Id] = "2.0" }
        };
        PortableLogbookOperationV2[] operations = [
            PortableLogbookOperationV2.Create(logbook, entryA, revisions[0], device, date, entry),
            PortableLogbookOperationV2.Correct(logbook, entryA, revisions[1], [revisions[0]], device, date.AddMinutes(1), entry with { SeCommandDay = 2.25m }),
            PortableLogbookOperationV2.Correct(logbook, entryA, revisions[2], [revisions[0]], device, date.AddMinutes(2), entry with { SeCommandDay = 3.5m }),
            PortableLogbookOperationV2.ResolveConflict(logbook, entryA, revisions[3], [revisions[1], revisions[2]], device, date.AddMinutes(3), entry with { SeCommandDay = 2.75m }),
            PortableLogbookOperationV2.Create(logbook, entryB, revisions[4], device, date.AddMinutes(4), entry with { SeCommandDay = 4m }),
            PortableLogbookOperationV2.Delete(logbook, entryB, revisions[5], [revisions[4]], device, date.AddMinutes(5), "Synthetic deletion")
        ];
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, [field],
            new PortableLogbookCurrencyOverrideDates(new DateOnly(2026, 10, 1), null, null), operations);
        var envelopes = operations.Select((operation, index) =>
        {
            var upload = HostedOperationCipher.Encrypt(operation, key);
            return new HostedOperationEnvelope(index + 1, upload.RevisionId, upload.EntryId, upload.DeviceId,
                upload.CreatedAt, upload.SchemaVersion, upload.PayloadCiphertext, upload.PayloadNonce,
                upload.PayloadTag, upload.PayloadHash, upload.ParentRevisionIds);
        }).ToArray();
        var config = HostedConfigurationRevisionCipher.Encrypt(
            PortableHostedConfigurationRevision.Create(document, RevisionId.New(), device, date.AddMinutes(6)), key);
        var configuration = new HostedConfigurationRevisionEnvelope(1, config.RevisionId, config.DeviceId, config.CreatedAt,
            config.SchemaVersion, config.PayloadCiphertext, config.PayloadNonce, config.PayloadTag, config.PayloadHash);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var keyBytes = key.ToBytes();
        var encryptedKey = new byte[32];
        var tag = new byte[16];
        using (var aes = new AesGcm(kek, 16))
            aes.Encrypt(nonce, keyBytes, encryptedKey, tag, AdditionalData(logbookGuid));
        CryptographicOperations.ZeroMemory(keyBytes);
        // Synthetic key material travels only over captured process pipes, never into an archive or evidence.
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            logbookGuid, kek = Convert.ToBase64String(kek), expectedSha = DocumentHash(document), operations = envelopes, configuration,
            keyEnvelope = new { nonce = Convert.ToBase64String(nonce), ciphertext = Convert.ToBase64String(encryptedKey.Concat(tag).ToArray()) }
        }, json));
        CryptographicOperations.ZeroMemory(kek);
    }
    else if (args is ["verify-real"])
    {
        using var input = JsonDocument.Parse(await Console.In.ReadToEndAsync());
        var root = input.RootElement;
        var kek = Convert.FromBase64String(root.GetProperty("kek").GetString()!);
        try
        {
            var results = new List<object>();
            foreach (var stored in root.GetProperty("logbooks").EnumerateArray())
            {
                var guid = stored.GetProperty("logbookGuid").GetGuid();
                var logbook = new LogbookId("log_" + guid.ToString("N"));
                Require(stored.GetProperty("identityCount").GetInt32() > 0, "Original owner identity is unavailable.");
                var envelope = stored.GetProperty("keyEnvelope");
                Require(envelope.GetProperty("wrapping_algorithm").GetString() == "AES-256-GCM" &&
                    envelope.GetProperty("key_version_id").GetString() == root.GetProperty("keyVersionId").GetString(),
                    "Managed recovery key version is unavailable.");
                var wrapped = Convert.FromBase64String(envelope.GetProperty("ciphertext").GetString()!);
                var bytes = new byte[32];
                try
                {
                    Require(wrapped.Length == 48, "Invalid managed recovery envelope.");
                    using var aes = new AesGcm(kek, 16);
                    aes.Decrypt(Convert.FromBase64String(envelope.GetProperty("nonce").GetString()!), wrapped[..32], wrapped[32..], bytes,
                        Encoding.UTF8.GetBytes($"electronic-logbook|managed-service-v1|{guid}|{root.GetProperty("keyVersionId").GetString()}"));
                    var key = PortableLogbookKey.FromBytes(bytes);
                    var envelopes = stored.GetProperty("operations").Deserialize<HostedOperationEnvelope[]>(json)!;
                    Require(envelopes.Length > 0 && envelopes.Select(e => e.HostedRevision)
                        .SequenceEqual(Enumerable.Range(1, envelopes.Length).Select(i => (long)i)), "Incomplete operation history.");
                    var operations = envelopes.Select(e => HostedOperationCipher.Decrypt(e, key)).ToArray();
                    Require(operations.All(o => o.LogbookId == logbook), "Operations belong to another logbook.");
                    var configurations = stored.GetProperty("configurations").Deserialize<HostedConfigurationRevisionEnvelope[]>(json)!;
                    Require(configurations.Length > 0 && configurations.Select(e => e.HostedRevision)
                        .SequenceEqual(Enumerable.Range(1, configurations.Length).Select(i => (long)i)), "Incomplete configuration history.");
                    var settings = configurations.Select(e => HostedConfigurationRevisionCipher.Decrypt(logbook, e, key)).Last();
                    var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, settings.CustomFieldDefinitions,
                        settings.CurrencyOverrideDates, operations);
                    Require(PortableLogbookValidatorV2.Validate(document).IsValid, "Recovered document failed application validation.");
                    var projection = PortableLogbookWorkbookProjection.MergeV2(document.Operations);
                    // Only counts leave this process; flight records, identifiers and document fingerprints stay private.
                    results.Add(new { operations = operations.Length, configurations = configurations.Length,
                        activeEntries = projection.Entries.Values.Count(e => !e.IsDeleted),
                        deletedEntries = projection.Entries.Values.Count(e => e.IsDeleted), conflicts = projection.Conflicts.Count,
                        customFields = settings.CustomFieldDefinitions.Count });
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            Require(results.Count > 0, "No populated logbook was replayed.");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "application-replayed", logbooks = results }, json));
        }
        finally { CryptographicOperations.ZeroMemory(kek); }
    }
    else if (args is ["verify"])
    {
        using var input = JsonDocument.Parse(await Console.In.ReadToEndAsync());
        var root = input.RootElement;
        Require(root.GetProperty("identityCount").GetInt32() == 1, "Original identity is unavailable.");
        var logbookGuid = root.GetProperty("logbookGuid").GetGuid();
        var kek = Convert.FromBase64String(root.GetProperty("kek").GetString()!);
        var stored = root.GetProperty("keyEnvelope");
        var wrapped = Convert.FromBase64String(stored.GetProperty("ciphertext").GetString()!);
        var bytes = new byte[32];
        try
        {
            using var aes = new AesGcm(kek, 16);
            aes.Decrypt(Convert.FromBase64String(stored.GetProperty("nonce").GetString()!), wrapped[..32], wrapped[32..], bytes,
                AdditionalData(logbookGuid));
            var key = PortableLogbookKey.FromBytes(bytes);
            var envelopes = root.GetProperty("operations").Deserialize<HostedOperationEnvelope[]>(json)!;
            Require(envelopes.Length == 6 && envelopes.Select(e => e.HostedRevision).SequenceEqual(Enumerable.Range(1, 6).Select(i => (long)i)),
                "Restored revision history is incomplete.");
            var operations = envelopes.Select(e => HostedOperationCipher.Decrypt(e, key)).ToArray();
            var logbook = new LogbookId("log_" + logbookGuid.ToString("N"));
            Require(operations.All(o => o.LogbookId == logbook), "Restored operations belong to another logbook.");
            var configuration = HostedConfigurationRevisionCipher.Decrypt(logbook,
                root.GetProperty("configuration").Deserialize<HostedConfigurationRevisionEnvelope>(json)!, key);
            var document = PortableLogbookDocumentV2.CreateAustraliaFirst(logbook, configuration.CustomFieldDefinitions,
                configuration.CurrencyOverrideDates, operations);
            Require(DocumentHash(document) == root.GetProperty("expectedSha").GetString(), "Restored document differs from the original.");
            var projection = PortableLogbookWorkbookProjection.MergeV2(document.Operations);
            var active = projection.Entries.Values.Where(e => !e.IsDeleted).ToArray();
            Require(active.Length == 1 && active[0].Entry?.SeCommandDay == 2.75m && projection.Conflicts.Count == 0,
                "Restored entries, total or conflict resolution differs.");
            Require(configuration.CustomFieldDefinitions.Count == 1 &&
                active[0].Entry!.CustomFields[configuration.CustomFieldDefinitions[0].Id] == "2.0" &&
                configuration.CurrencyOverrideDates.FlightReview == new DateOnly(2026, 10, 1), "Restored settings differ.");
            Console.WriteLine("{\"status\":\"verified\",\"operations\":6,\"activeEntries\":1,\"deletedEntries\":1,\"commandHours\":2.75,\"settingsMatch\":true}");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(kek); }
    }
    else throw new InvalidOperationException("Unknown replay check mode.");
    return 0;
}
catch
{
    Console.Error.WriteLine("Restored application replay failed; private fixture data and key material omitted.");
    return 1;
}

static byte[] AdditionalData(Guid logbook) => Encoding.UTF8.GetBytes($"electronic-logbook|managed-service-v1|{logbook}|backup-fixture-v1");
static string DocumentHash(PortableLogbookDocumentV2 document) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PortableLogbookJson.SerializeV2(document))));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
