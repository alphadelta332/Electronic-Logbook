using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed class MobileWorkbookEntryDraft
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Type { get; set; } = string.Empty;
    public string Reg { get; set; } = string.Empty;
    public string FlightId { get; set; } = string.Empty;
    public string Pic { get; set; } = "Self";
    public string OtherPilotOrCrew { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Via { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public bool FlightReview { get; set; }
    public bool InstrumentProficiencyCheck { get; set; }
    public bool OperatorProficiencyCheck { get; set; }
    public Dictionary<CustomFieldId, string> CustomValues { get; } = [];
    public decimal? SeIcusDay { get; set; }
    public decimal? SeIcusNight { get; set; }
    public decimal? SeDualDay { get; set; }
    public decimal? SeDualNight { get; set; }
    public decimal? SeCommandDay { get; set; }
    public decimal? SeCommandNight { get; set; }
    public decimal? MeIcusDay { get; set; }
    public decimal? MeIcusNight { get; set; }
    public decimal? MeDualDay { get; set; }
    public decimal? MeDualNight { get; set; }
    public decimal? MeCommandDay { get; set; }
    public decimal? MeCommandNight { get; set; }
    public decimal? CopilotDay { get; set; }
    public decimal? CopilotNight { get; set; }
    public decimal? IfrIf { get; set; }
    public decimal? IfrSim { get; set; }
    public int? LandingsDay { get; set; }
    public int? LandingsNight { get; set; }
    public int? Ils { get; set; }
    public int? Vor { get; set; }
    public int? Rnp { get; set; }
    public int? Ndb { get; set; }
    public int? DgaCdi { get; set; }
    public int? DgaAzi { get; set; }
    public int? Circling { get; set; }

    public decimal TotalHours =>
        SeIcusDay.GetValueOrDefault() +
        SeIcusNight.GetValueOrDefault() +
        SeDualDay.GetValueOrDefault() +
        SeDualNight.GetValueOrDefault() +
        SeCommandDay.GetValueOrDefault() +
        SeCommandNight.GetValueOrDefault() +
        MeIcusDay.GetValueOrDefault() +
        MeIcusNight.GetValueOrDefault() +
        MeDualDay.GetValueOrDefault() +
        MeDualNight.GetValueOrDefault() +
        MeCommandDay.GetValueOrDefault() +
        MeCommandNight.GetValueOrDefault() +
        CopilotDay.GetValueOrDefault() +
        CopilotNight.GetValueOrDefault();

    public int TotalApproaches =>
        Ils.GetValueOrDefault() +
        Vor.GetValueOrDefault() +
        Rnp.GetValueOrDefault() +
        Ndb.GetValueOrDefault() +
        DgaCdi.GetValueOrDefault() +
        DgaAzi.GetValueOrDefault() +
        Circling.GetValueOrDefault();

    public int TotalLandings => LandingsDay.GetValueOrDefault() + LandingsNight.GetValueOrDefault();

    public static MobileWorkbookEntryDraft Create(IEnumerable<CustomFieldDefinition>? customFields = null)
    {
        var draft = new MobileWorkbookEntryDraft();
        foreach (var field in OrderedWorkbookCustomFields(customFields))
        {
            draft.CustomValues[field.Id] = string.Empty;
        }

        return draft;
    }

    public static MobileWorkbookEntryDraft FromEntry(
        PortableLogbookWorkbookEntry entry,
        IEnumerable<CustomFieldDefinition>? customFields,
        bool preserveDate)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var draft = Create(customFields);
        draft.Date = preserveDate && entry.Date is not null
            ? entry.Date.Value
            : DateOnly.FromDateTime(DateTime.Today);
        draft.Type = entry.Type ?? string.Empty;
        draft.Reg = entry.Reg ?? string.Empty;
        draft.FlightId = entry.FlightId ?? string.Empty;
        draft.Pic = entry.Pic ?? string.Empty;
        draft.OtherPilotOrCrew = entry.OtherPilotOrCrew ?? string.Empty;
        draft.From = entry.From ?? string.Empty;
        draft.To = entry.To ?? string.Empty;
        draft.Via = entry.Via ?? string.Empty;
        draft.Remarks = entry.Remarks ?? string.Empty;
        draft.FlightReview = entry.FlightReview == true;
        draft.InstrumentProficiencyCheck = entry.InstrumentProficiencyCheck == true;
        draft.OperatorProficiencyCheck = entry.OperatorProficiencyCheck == true;
        draft.SeIcusDay = entry.SeIcusDay;
        draft.SeIcusNight = entry.SeIcusNight;
        draft.SeDualDay = entry.SeDualDay;
        draft.SeDualNight = entry.SeDualNight;
        draft.SeCommandDay = entry.SeCommandDay;
        draft.SeCommandNight = entry.SeCommandNight;
        draft.MeIcusDay = entry.MeIcusDay;
        draft.MeIcusNight = entry.MeIcusNight;
        draft.MeDualDay = entry.MeDualDay;
        draft.MeDualNight = entry.MeDualNight;
        draft.MeCommandDay = entry.MeCommandDay;
        draft.MeCommandNight = entry.MeCommandNight;
        draft.CopilotDay = entry.CopilotDay;
        draft.CopilotNight = entry.CopilotNight;
        draft.IfrIf = entry.IfrIf;
        draft.IfrSim = entry.IfrSim;
        draft.LandingsDay = entry.LandingsDay;
        draft.LandingsNight = entry.LandingsNight;
        draft.Ils = entry.Ils;
        draft.Vor = entry.Vor;
        draft.Rnp = entry.Rnp;
        draft.Ndb = entry.Ndb;
        draft.DgaCdi = entry.DgaCdi;
        draft.DgaAzi = entry.DgaAzi;
        draft.Circling = entry.Circling;
        foreach (var customField in entry.CustomFields)
        {
            draft.CustomValues[customField.Key] = customField.Value ?? string.Empty;
        }

        return draft;
    }

    public PortableLogbookWorkbookEntry ToEntry(IEnumerable<CustomFieldDefinition>? customFields)
    {
        IReadOnlyDictionary<CustomFieldId, string?> customValues = OrderedWorkbookCustomFields(customFields)
            .Select(field => new KeyValuePair<CustomFieldId, string>(field.Id, CustomValues.GetValueOrDefault(field.Id, string.Empty)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Trim());

        return PortableLogbookWorkbookEntry.Empty with
        {
            Year = Date.Year,
            Month = Date.Month,
            Day = Date.Day,
            Type = Trim(Type),
            Reg = Trim(Reg),
            FlightId = Trim(FlightId),
            Pic = Trim(Pic),
            OtherPilotOrCrew = Trim(OtherPilotOrCrew),
            From = Trim(From),
            To = Trim(To),
            Via = Trim(Via),
            Remarks = Trim(Remarks),
            FlightReview = FlightReview,
            InstrumentProficiencyCheck = InstrumentProficiencyCheck,
            OperatorProficiencyCheck = OperatorProficiencyCheck,
            CustomFields = customValues,
            SeIcusDay = SeIcusDay,
            SeIcusNight = SeIcusNight,
            SeDualDay = SeDualDay,
            SeDualNight = SeDualNight,
            SeCommandDay = SeCommandDay,
            SeCommandNight = SeCommandNight,
            MeIcusDay = MeIcusDay,
            MeIcusNight = MeIcusNight,
            MeDualDay = MeDualDay,
            MeDualNight = MeDualNight,
            MeCommandDay = MeCommandDay,
            MeCommandNight = MeCommandNight,
            CopilotDay = CopilotDay,
            CopilotNight = CopilotNight,
            IfrIf = IfrIf,
            IfrSim = IfrSim,
            LandingsDay = LandingsDay,
            LandingsNight = LandingsNight,
            Ils = Ils,
            Vor = Vor,
            Rnp = Rnp,
            Ndb = Ndb,
            DgaCdi = DgaCdi,
            DgaAzi = DgaAzi,
            Circling = Circling
        };
    }

    private static IEnumerable<CustomFieldDefinition> OrderedWorkbookCustomFields(IEnumerable<CustomFieldDefinition>? customFields) =>
        (customFields ?? MobileLogbookSession.CustomFields)
            .Where(field => field.Order is >= 1 and <= PortableLogbookCustomFieldSet.WorkbookCustomFieldCount)
            .OrderBy(field => field.Order);

    private static string? Trim(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
