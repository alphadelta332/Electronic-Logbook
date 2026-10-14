namespace ElectronicLogbook.Mobile;

public sealed record MobileAnalyticsPlotSeries(string Name, decimal[] Values, string Color);
public sealed record MobileAnalyticsPlotData(string Kind, string[] Labels, MobileAnalyticsPlotSeries[] Series, bool Counts = false);
