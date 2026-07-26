namespace ElectronicLogbook.Updater.Tests;

using ElectronicLogbook.Portable;

public sealed class PortableLogbookCurrencyQualificationTests
{
    [Fact]
    public void Classify_WithSingleEngineBucket_QualifiesForSingleEngineOnly()
    {
        var entry = PortableLogbookWorkbookEntry.Empty with { SeCommandNight = 1.2m };

        var qualification = PortableLogbookCurrencyQualification.Classify(entry);

        Assert.True(qualification.IsSingleEngineQualified);
        Assert.False(qualification.IsMultiEngineQualified);
    }

    [Fact]
    public void Classify_WithMultiEngineBucket_QualifiesForBothWorkbookCategories()
    {
        var entry = PortableLogbookWorkbookEntry.Empty with { MeDualDay = 0.8m };

        var qualification = PortableLogbookCurrencyQualification.Classify(entry);

        Assert.True(qualification.IsSingleEngineQualified);
        Assert.True(qualification.IsMultiEngineQualified);
    }

    [Fact]
    public void Classify_WithOnlyNonQualifyingLoggedTime_DoesNotQualifyForEitherCategory()
    {
        var entry = PortableLogbookWorkbookEntry.Empty with
        {
            CopilotDay = 1.1m,
            IfrIf = 0.4m,
            IfrSim = 0.3m
        };

        var qualification = PortableLogbookCurrencyQualification.Classify(entry);

        Assert.False(qualification.IsSingleEngineQualified);
        Assert.False(qualification.IsMultiEngineQualified);
    }
}
