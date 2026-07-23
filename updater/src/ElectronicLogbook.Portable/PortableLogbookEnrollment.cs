using System.Text.Json;

namespace ElectronicLogbook.Portable;

public static class PortableLogbookEnrollment
{
    public const int CurrentEnrollmentVersion = 1;
    public const string Warning = "This enrolment payload contains the portable logbook encryption key. Share it only with devices you control.";

    public static PortableLogbookEnrollmentPayload Create(
        LogbookId logbookId,
        DeviceId sourceDeviceId,
        PortableLogbookKey key,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new PortableLogbookEnrollmentPayload(
            CurrentEnrollmentVersion,
            logbookId,
            sourceDeviceId,
            createdAt,
            key.ToRecoveryCode(),
            Warning);
    }

    public static string Serialize(PortableLogbookEnrollmentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, PortableLogbookJson.SerializerOptions);
    }

    public static PortableLogbookEnrollmentPayload Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var payload = JsonSerializer.Deserialize<PortableLogbookEnrollmentPayload>(json, PortableLogbookJson.SerializerOptions)
            ?? throw new ArgumentException("Enrolment payload is invalid.", nameof(json));
        if (payload.EnrollmentVersion != CurrentEnrollmentVersion)
        {
            throw new PortableLogbookEnrollmentException(
                PortableLogbookEnrollmentError.UnsupportedVersion,
                $"Enrolment version {payload.EnrollmentVersion} is not supported.");
        }

        _ = PortableLogbookKey.FromRecoveryCode(payload.RecoveryCode);
        return payload;
    }

    public static PortableLogbookKey ExtractKey(PortableLogbookEnrollmentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return PortableLogbookKey.FromRecoveryCode(payload.RecoveryCode);
    }
}

public sealed record PortableLogbookEnrollmentPayload(
    int EnrollmentVersion,
    LogbookId LogbookId,
    DeviceId SourceDeviceId,
    DateTimeOffset CreatedAt,
    string RecoveryCode,
    string Warning);

public sealed class PortableLogbookEnrollmentException : Exception
{
    public PortableLogbookEnrollmentException(PortableLogbookEnrollmentError error, string message)
        : base(message)
    {
        Error = error;
    }

    public PortableLogbookEnrollmentError Error { get; }
}

public enum PortableLogbookEnrollmentError
{
    UnsupportedVersion
}
