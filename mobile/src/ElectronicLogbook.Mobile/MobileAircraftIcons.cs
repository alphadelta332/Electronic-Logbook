namespace ElectronicLogbook.Mobile;

public static class MobileAircraftIcons
{
    private const string Airframe = """
        <path d="M12 4.5c-.9 0-1.4 1-1.4 2.5v2.7L2 12v2l8.6-1.4v5.2L7.5 19v1.5l4.5-.8 4.5.8V19l-3.1-1.2v-5.2L22 14v-2l-8.6-2.3V7c0-1.5-.5-2.5-1.4-2.5Z"/>
        """;

    public const string SingleEngine = "<g transform=\"rotate(45 12 12)\">" + Airframe + """
        <line x1="8.8" y1="3.5" x2="15.2" y2="3.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        </g>
        """;

    public const string MultiEngine = "<g transform=\"rotate(45 12 12)\">" + Airframe + """
        <rect x="5.2" y="8.7" width="1.6" height="5.7" rx=".8"/>
        <rect x="17.2" y="8.7" width="1.6" height="5.7" rx=".8"/>
        <line x1="3.4" y1="7.8" x2="8.6" y2="7.8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        <line x1="15.4" y1="7.8" x2="20.6" y2="7.8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        </g>
        """;
}
