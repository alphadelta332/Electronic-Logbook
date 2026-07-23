using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookEnrollmentTests
{
    [Fact]
    public void CreateSerializeDeserializeRoundTripsEnrolmentPayloadAndKey()
    {
        var key = PortableLogbookKey.Generate();
        var payload = PortableLogbookEnrollment.Create(
            new LogbookId("log_enrol"),
            new DeviceId("dev_excel"),
            key,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        var json = PortableLogbookEnrollment.Serialize(payload);
        var roundTripped = PortableLogbookEnrollment.Deserialize(json);
        var extractedKey = PortableLogbookEnrollment.ExtractKey(roundTripped);

        Assert.Equal(payload.LogbookId, roundTripped.LogbookId);
        Assert.Equal(payload.SourceDeviceId, roundTripped.SourceDeviceId);
        Assert.Equal(PortableLogbookEnrollment.Warning, roundTripped.Warning);
        Assert.Equal(key, extractedKey);
    }

    [Fact]
    public void DeserializeRejectsUnsupportedEnrollmentVersion()
    {
        var payload = PortableLogbookEnrollment.Create(
            new LogbookId("log_enrol"),
            new DeviceId("dev_excel"),
            PortableLogbookKey.Generate(),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z")) with
        {
            EnrollmentVersion = PortableLogbookEnrollment.CurrentEnrollmentVersion + 1
        };

        var exception = Assert.Throws<PortableLogbookEnrollmentException>(
            () => PortableLogbookEnrollment.Deserialize(PortableLogbookEnrollment.Serialize(payload)));

        Assert.Equal(PortableLogbookEnrollmentError.UnsupportedVersion, exception.Error);
    }

    [Fact]
    public void DeserializeRejectsInvalidRecoveryCode()
    {
        var payload = PortableLogbookEnrollment.Create(
            new LogbookId("log_enrol"),
            new DeviceId("dev_excel"),
            PortableLogbookKey.Generate(),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z")) with
        {
            RecoveryCode = "not-a-valid-key"
        };

        var exception = Assert.Throws<ArgumentException>(
            () => PortableLogbookEnrollment.Deserialize(PortableLogbookEnrollment.Serialize(payload)));

        Assert.Equal("recoveryCode", exception.ParamName);
    }
}
