using Bunit;
using ElectronicLogbook.Mobile.Pages;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class AnalyticsStatisticsComponentTests : BunitContext
{
    [Fact]
    public void All24ResultsShowTheirOwnValuesAndSupportingDetails()
    {
        var date = new DateOnly(2024, 1, 10);
        var sydney = new MobileAirport("YSSY", "Sydney", "SYD", null, 0, 0);
        var melbourne = new MobileAirport("YMML", "Melbourne", "MEL", null, 0, 0);
        var stats = new MobileAnalyticsStatistics(
            new(1.1m, date, date), new(2.2m, date, date.AddDays(6)),
            new(3.3m, new(2024, 1, 1), new(2024, 1, 31)), new(4.4m, new(2024, 1, 1), new(2024, 12, 31)),
            new(5, date, date), new(6, date, date), new(7, new(2024, 1, 1), new(2024, 1, 31)),
            new(8.8m, date, "YSSY → YMML", "TYPE", "VH-LONG"), new(9.9m, date.AddDays(1), "YMML → YSSY", "TYPE", "VH-SHORT"),
            10.1m, 11.1m, 12.1m, 13.1m, 14.1m, new("Another PIC", 15.1m), 16,
            new(sydney, 17, date), new(melbourne, 18, date), new(melbourne, 19, date.AddDays(2)), 20,
            new(sydney, melbourne, date, 21.1), new(melbourne, sydney, date.AddDays(1), 22.2), 23.3m, 24.4m);
        var analytics = new MobileLogbookAnalytics([], [], [], [], [], [], stats, 0, 0, 0, [], true);
        var page = Render<AnalyticsStatistics>(parameters => parameters.Add(component => component.Analytics, analytics));

        Assert.Equal(new[]
        {
            "Most hours in a day", "Most hours in seven consecutive days", "Most hours in a month", "Most hours in a year",
            "Most landings in a day", "Most approaches in a day", "Most flying days in a month", "Longest flight", "Shortest flight",
            "Average hours per flight", "Average flying days per active month · Last 365 days", "Average flying days per active month · All time",
            "IFR percentage · Last 365 days", "IFR percentage · All time", "Most hours with another PIC", "Distinct crew flown with",
            "Most visited airport", "Most visited airport excluding bases", "Most recent newly visited airport", "Number of airports visited",
            "Longest leg", "Shortest leg", "Approximate distance flown", "Percentage of the distance to the Moon"
        }, page.FindAll("dt").Select(element => element.TextContent));
        Assert.Equal(new[]
        {
            "1.1 h", "2.2 h", "3.3 h", "4.4 h", "5", "6", "7", "8.8 h", "9.9 h", "10.1 h", "11.1", "12.1",
            "13.1%", "14.1%", "15.1 h", "16", "YSSY · Sydney", "YMML · Melbourne", "YMML · Melbourne", "20", "21.1 nm", "22.2 nm", "23.3 nm", "24.4%"
        }, page.FindAll("dd strong").Select(element => element.TextContent));
        Assert.Equal(new[]
        {
            "10 Jan 2024", "10 Jan 2024 – 16 Jan 2024", "January 2024", "2024", "10 Jan 2024", "10 Jan 2024", "January 2024",
            "10 Jan 2024 · YSSY → YMML · TYPE · VH-LONG", "11 Jan 2024 · YMML → YSSY · TYPE · VH-SHORT",
            "Another PIC", "17 visits", "18 visits", "12 Jan 2024",
            "YSSY · Sydney → YMML · Melbourne · 10 Jan 2024", "YMML · Melbourne → YSSY · Sydney · 11 Jan 2024"
        }, page.FindAll("dd span").Select(element => element.TextContent));
        Assert.Equal(5, page.FindAll("section").Count);
    }
}
