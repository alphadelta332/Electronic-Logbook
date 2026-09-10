using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

internal static class WorkbookEntryPresentationProjection
{
    public static PortableLogbookEntry Create(PortableLogbookWorkbookEntry entry) => new(
        entry.Date,
        entry.Type,
        entry.Reg,
        entry.FlightId,
        entry.From,
        entry.To,
        entry.Via,
        entry.Remarks,
        // The legacy presentation schema has no ICUS field. Its MultiPilot bucket is
        // the established read-only projection for all workbook ICUS hours.
        Sum(entry.SeIcusDay, entry.SeIcusNight, entry.MeIcusDay, entry.MeIcusNight),
        Sum(entry.SeCommandDay, entry.SeCommandNight, entry.MeCommandDay, entry.MeCommandNight),
        Sum(entry.CopilotDay, entry.CopilotNight),
        Sum(entry.SeDualDay, entry.SeDualNight, entry.MeDualDay, entry.MeDualNight),
        null,
        Sum(
            entry.SeIcusDay,
            entry.SeDualDay,
            entry.SeCommandDay,
            entry.MeIcusDay,
            entry.MeDualDay,
            entry.MeCommandDay,
            entry.CopilotDay),
        Sum(
            entry.SeIcusNight,
            entry.SeDualNight,
            entry.SeCommandNight,
            entry.MeIcusNight,
            entry.MeDualNight,
            entry.MeCommandNight,
            entry.CopilotNight),
        entry.IfrIf,
        entry.IfrSim,
        null,
        null,
        entry.LandingsDay,
        entry.LandingsNight,
        Sum(entry.Ils, entry.Vor, entry.Rnp, entry.Ndb, entry.DgaCdi, entry.DgaAzi, entry.Circling),
        null,
        entry.Rnp,
        entry.Circling,
        entry.CustomFields);

    private static decimal? Sum(params decimal?[] values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Sum();
    }

    private static int? Sum(params int?[] values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Sum();
    }
}
