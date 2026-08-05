using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileDashboardExperienceSummary(
    decimal CommandHours,
    decimal IcusHours,
    decimal DualHours,
    decimal CopilotHours,
    decimal SingleEngineHours,
    decimal MultiEngineHours)
{
    public decimal AuthorityHours =>
        CommandHours + IcusHours + DualHours + CopilotHours;

    public decimal ClassifiedEngineHours =>
        SingleEngineHours + MultiEngineHours;

    public static MobileDashboardExperienceSummary Create(
        IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        decimal commandHours = 0;
        decimal icusHours = 0;
        decimal dualHours = 0;
        decimal copilotHours = 0;
        decimal singleEngineHours = 0;
        decimal multiEngineHours = 0;

        foreach (var entry in entries)
        {
            var seCommand = Hours(entry.SeCommandDay) + Hours(entry.SeCommandNight);
            var meCommand = Hours(entry.MeCommandDay) + Hours(entry.MeCommandNight);
            var seIcus = Hours(entry.SeIcusDay) + Hours(entry.SeIcusNight);
            var meIcus = Hours(entry.MeIcusDay) + Hours(entry.MeIcusNight);
            var seDual = Hours(entry.SeDualDay) + Hours(entry.SeDualNight);
            var meDual = Hours(entry.MeDualDay) + Hours(entry.MeDualNight);

            commandHours += seCommand + meCommand;
            icusHours += seIcus + meIcus;
            dualHours += seDual + meDual;
            copilotHours += Hours(entry.CopilotDay) + Hours(entry.CopilotNight);
            singleEngineHours += seCommand + seIcus + seDual;
            multiEngineHours += meCommand + meIcus + meDual;
        }

        return new MobileDashboardExperienceSummary(
            commandHours,
            icusHours,
            dualHours,
            copilotHours,
            singleEngineHours,
            multiEngineHours);
    }

    private static decimal Hours(decimal? value) =>
        Math.Max(value.GetValueOrDefault(), 0);
}
