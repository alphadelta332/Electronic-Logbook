using System.Globalization;

namespace ElectronicLogbook.Portable;

// Edit entry validation and warning wording here. Rule logic belongs in the rule classes.
public static class PortableLogbookEntryMessages
{
    public const string InvalidDate = "Invalid Date.";
    public const string InvalidWorkbookDate = "Invalid Date. Ensure correct three-letter month is used, and the date is not in the future.";
    public const string MissingAircraftType = "Missing Aircraft Type.";
    public const string MissingType = "Missing Aircraft Type.";
    public const string MissingPic = "Missing PIC.";
    public const string MissingRegistration = "Missing Aircraft Registration.";
    public const string MissingWorkbookRegistration = "Missing Aircraft Registration.";
    public const string MissingDeparture = "Missing Departure Airport.";
    public const string MissingDestination = "Missing Destination Airport.";
    public const string MissingLoggedTime = "Missing Hours.";
    public const string MissingWorkbookLoggedTime = "Missing Hours.";
    public const string InstrumentTimeExceedsFlightTime = "In-flight instrument time is greater than the total flight time.";
    public const string InvalidNumericValue = "Hours, landings, and approaches must be numbers.";

    public const string FlightTimeWithoutDayOrNight = "This entry has flight time but no day or night time.";
    public const string DayNightTimeExceedsFlightTime = "Day and night time exceed the total flight time for this entry.";
    public const string FlightTimeWithoutLanding = "No Landings recorded.";
    public const string DayTimeWithoutDayLanding = "No Day Landings recorded.";
    public const string DayLandingWithoutDayTime = "No Day Hours recorded (at least one day landing was recorded).";
    public const string NightTimeWithoutNightLanding = "No Night Landings recorded.";
    public const string NightLandingWithoutNightTime = "No Night Hours recorded (at least one night landing was recorded).";
    public const string ApproachWithoutInstrumentTime = "No Instrument Time recorded (at least one instrument approach was recorded).";
    public const string InstrumentTimeWithoutApproach = "No Approaches recorded (instrument hours were recorded).";
    public const string HighLandingsForFlightTime = "The number of landings seems high compared with the total flight time.";
    public const string HighApproachesForFlightTime = "The number of approaches seems high compared with the total flight time.";

    public const string OpcWithoutIpc = "OPC is ticked and instrument time is logged, but IPC is not ticked.";
    public const string IpcWithoutFlightReview = "IPC is ticked, but Flight Review is not ticked.";
    public const string IpcWithoutCircling = "No circling approach was recorded on this IPC. You will not be recent for circling approaches until your next IPC.";
    public const string WorkbookFlightWithoutLanding = "No landings are recorded for this non-simulator entry.";
    public const string WorkbookDayTimeWithoutLanding = "Day hours are recorded, but no day landings are recorded.";
    public const string WorkbookDayLandingWithoutTime = "Day landings are recorded, but no day hours are recorded.";
    public const string WorkbookNightTimeWithoutLanding = "Night hours are recorded, but no night landings are recorded.";
    public const string WorkbookNightLandingWithoutTime = "Night landings are recorded, but no night hours are recorded.";
    public const string OpcWithoutInstrumentActivity = "OPC is ticked, but no instrument time or approaches are recorded.";
    public const string WorkbookApproachWithoutInstrumentTime = "Approaches are recorded, but no instrument time is recorded.";
    public const string WorkbookHighLandings = "The number of landings seems high compared with the total hours.";
    public const string WorkbookHighApproaches = "The number of approaches seems high compared with the total hours.";
    public const string CrewHoursWithoutCrew = "Dual, ICUS, or copilot hours are recorded, but no other pilot or crew is recorded.";
    public const string MixedEngineHours = "This entry records both single-engine and multi-engine hours.";
    public const string ExpectedMultiEngineHours = "This aircraft type has previously been logged with multi-engine hours, but this entry records single-engine hours.";
    public const string ExpectedSingleEngineHours = "This aircraft type has previously been logged with single-engine hours, but this entry records multi-engine hours.";
    public const string RegistrationTypeMismatch = "This registration has previously been logged with a different aircraft type.";
    public const string PossibleDuplicate = "An entry with the same date, type, registration, and remarks already exists.";
    public const string PossibleWorkbookDuplicate = "An entry with the same date, type, registration, and remarks already exists in the Logbook. This may be a duplicate.";
    public const string UnrecognisedDepartureAndDestination = "The Departure and Destination airport codes are not recognised.";
    public const string UnrecognisedDeparture = "The Departure airport code is not recognised.";
    public const string UnrecognisedDestination = "The Destination airport code is not recognised.";
    public const string UnrecognisedAirport = "An airport code is not recognised.";

    public static string NegativeValue(string label) => $"{label} cannot be negative.";

    public static string BeforeLatestEntry(DateOnly date) =>
        $"This entry is dated before the latest existing entry ({date:dd MMM yyyy}).";

    public static string BeforeLatestWorkbookEntry(DateOnly date) =>
        $"This entry is dated before the latest existing Logbook entry ({date:dd MMM yyyy}).";

    public static string RouteDistance(string from, string to, string distance) =>
        $"The route from {from} to {to} is about {distance} NM.";

    public static string RouteSpeed(string from, string to, string distance, decimal hours, string speed) =>
        $"The route from {from} to {to} is about {distance} NM. With {hours.ToString("0.0#", CultureInfo.InvariantCulture)} flight hours recorded, the implied average speed is {speed} knots.";

    public static string UnusualRouteDistances(IEnumerable<string> details) =>
        "One or more route airport distance checks look unusual.\n\n" + string.Join('\n', details);

    public static string AirportDistance(string field, string airport, string distance, string nearest) =>
        $"{field} {airport} is about {distance} NM from the nearest previously visited airport, {nearest}.";
}
