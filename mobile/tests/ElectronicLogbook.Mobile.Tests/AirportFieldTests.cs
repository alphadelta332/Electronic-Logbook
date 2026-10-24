using Bunit;
using ElectronicLogbook.Mobile.Pages;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class AirportFieldTests : BunitContext, IAsyncLifetime
{
    public AirportFieldTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    [Theory]
    [InlineData("From")]
    [InlineData("To")]
    public async Task EmptyQueryUsesRankedDefaultsAndTypingSearchesTheWholeCatalogue(string label)
    {
        var field = Render<AirportField>(parameters => parameters.Add(component => component.Label, label)
            .Add(component => component.LoadSuggestions, text => Task.FromResult<IReadOnlyList<string>>(
                string.IsNullOrWhiteSpace(text) ? ["YSSY", "YSBK"] :
                    MobileAirportSuggestions.Ranked([], [], new(2026, 10, 18), MobileAirportCatalog.Default, text))));
        var autocomplete = field.FindComponent<MudAutocomplete<string>>().Instance;
        Assert.Equal(new[] { "YSSY", "YSBK" }, await autocomplete.SearchFunc!("", CancellationToken.None)!);
        var codes = (await autocomplete.SearchFunc!("ymm", CancellationToken.None)!).ToArray();
        Assert.Contains("YMML", codes);
        Assert.Contains("YMMB", codes);
        Assert.All(codes, code => Assert.StartsWith("YMM", code));
        Assert.Contains("YMML", await autocomplete.SearchFunc!("Melbourne", CancellationToken.None)!);
        Assert.True(autocomplete.CoerceValue);
        Assert.False(autocomplete.CoerceText);
        Assert.True(autocomplete.Immediate);
        Assert.True(autocomplete.ResetValueOnEmptyText);
        Assert.True(autocomplete.OpenOnFocus);
        Assert.Equal(0, autocomplete.MinCharacters);
        Assert.Equal(100, autocomplete.MaxItems);
        Assert.Equal(100, (await autocomplete.SearchFunc!("a", CancellationToken.None)!).Count());
        Assert.False(autocomplete.Required);
    }

    [Fact]
    public async Task UnknownTextSurvivesBlurAndClearingWithoutBeingBlocked()
    {
        string? saved = null;
        var edits = 0;
        var field = Render<AirportField>(parameters => parameters.Add(component => component.Label, "To")
            .Add(component => component.LoadSuggestions, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(component => component.ValueChanged, value => saved = value)
            .Add(component => component.Edited, () => edits++));
        field.Find("input").Input("PRIVATE FARM");
        field.Find("input").Blur();
        field.WaitForAssertion(() => Assert.Equal("PRIVATE FARM", saved));
        Assert.Single(field.FindAll("[role='status']"));
        Assert.Equal("PRIVATE FARM", field.Find("input").GetAttribute("value"));
        var autocomplete = field.FindComponent<MudAutocomplete<string>>();
        await field.InvokeAsync(() => autocomplete.Instance.ClearAsync());
        Assert.True(string.IsNullOrEmpty(saved));
        Assert.Empty(field.FindAll("[role='status']"));
        Assert.True(edits >= 2);
    }

    [Fact]
    public async Task SelectingNameSearchResultSavesOnlyIcaoAndMarksDraftEdited()
    {
        string? saved = null;
        var edits = 0;
        var field = Render<AirportField>(parameters => parameters.Add(component => component.Label, "From")
            .Add(component => component.LoadSuggestions, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(component => component.ValueChanged, value => saved = value)
            .Add(component => component.Edited, () => edits++));
        var autocomplete = field.FindComponent<MudAutocomplete<string>>();
        await field.InvokeAsync(() => autocomplete.Instance.SelectOptionAsync("YMML"));
        field.Find("input").Blur();
        Assert.Equal("YMML", saved);
        Assert.Equal("YMML", field.Find("input").GetAttribute("value"));
        Assert.True(edits > 0);
        Assert.Empty(field.FindAll("[role='status']"));
    }

    [Theory]
    [InlineData("YWOL-YSCN-YWOL", "YWOL", "YSCN", "YWOL")]
    [InlineData(" YWOL - PRIVATE FARM - YSCN ", "YWOL", "PRIVATE FARM", "YSCN")]
    [InlineData("BK CN", "BK", "CN")]
    [InlineData("YWOL, YSCN / YWOL", "YWOL", "YSCN", "YWOL")]
    [InlineData("YWOL - PRIVATE-FARM - YSCN", "YWOL", "PRIVATE-FARM", "YSCN")]
    [InlineData("PRIVATE FARM / NORTH", "PRIVATE FARM / NORTH")]
    [InlineData("YWOL-ZZZZ-YSCN", "YWOL", "ZZZZ", "YSCN")]
    [InlineData("YWOL-PRIVATE FARM-YSCN", "YWOL", "PRIVATE FARM", "YSCN")]
    public void ExistingRoutesOpenAsOrderedChipsWithoutRewritingStoredText(string route, params string[] expected)
    {
        var edits = 0;
        var field = Render<AirportField>(parameters => parameters.Add(component => component.Label, "Via")
            .Add(component => component.Multiple, true).Add(component => component.Value, route)
            .Add(component => component.LoadSuggestions, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(component => component.Edited, () => edits++));

        Assert.Equal(expected, field.FindAll(".airport-chip-label").Select(chip => chip.TextContent));
        Assert.Equal(route, field.Instance.Value);
        Assert.Equal(0, edits);
        Assert.Equal(expected.Any(code => !MobileAirportCatalog.Default.TryFind(code, out _)),
            field.FindAll("[role='status']").Count > 0);
    }

    [Fact]
    public async Task ViaUsesSharedLookupAndAddsSelectionsIncludingRepeatAirports()
    {
        string? saved = null;
        var field = Render<AirportField>(parameters => parameters.Add(component => component.Label, "Via")
            .Add(component => component.Multiple, true)
            .Add(component => component.LoadSuggestions, query => Task.FromResult<IReadOnlyList<string>>(
                MobileAirportSuggestions.Ranked([], [], new(2026, 10, 25), MobileAirportCatalog.Default, query)))
            .Add(component => component.ValueChanged, value => saved = value));
        var autocomplete = field.FindComponent<MudAutocomplete<string>>();
        Assert.Contains("YMML", await autocomplete.Instance.SearchFunc!("Melbourne", CancellationToken.None)!);

        field.Find("input").Input("Melbourne");
        field.Find("input").Blur();
        await field.InvokeAsync(() => autocomplete.Instance.SelectOptionAsync("YMML"));
        await field.InvokeAsync(() => autocomplete.Instance.SelectOptionAsync("YWOL"));
        await field.InvokeAsync(() => autocomplete.Instance.SelectOptionAsync("YMML"));
        field.Find("input").Blur();

        Assert.Equal("YMML - YWOL - YMML", saved);
        Assert.Equal(new[] { "YMML", "YWOL", "YMML" }, field.FindAll(".airport-chip-label").Select(chip => chip.TextContent));
        Assert.True(string.IsNullOrEmpty(field.Find("input").GetAttribute("value")));
        Assert.Empty(field.FindAll("[role='status']"));
    }

    [Fact]
    public void ManualAirportIsSavedWhileTypingAndCanBeAddedAsAChipWithWarning()
    {
        string? saved = null;
        var field = Render<AirportField>(parameters => parameters.Add(component => component.Label, "Via")
            .Add(component => component.Multiple, true).Add(component => component.Value, "YWOL")
            .Add(component => component.LoadSuggestions, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(component => component.ValueChanged, value => saved = value));

        field.Find("input").Input("PRIVATE FARM");
        field.WaitForAssertion(() => Assert.Equal("YWOL - PRIVATE FARM", saved));
        field.Find("input").Blur();
        Assert.Contains("Airport not recognised: PRIVATE FARM.", field.Find("[role='status']").TextContent);
        field.Find("button[aria-label='Add PRIVATE FARM']").Click();
        field.WaitForAssertion(() => Assert.Equal(new[] { "YWOL", "PRIVATE FARM" },
            field.FindAll(".airport-chip-label").Select(chip => chip.TextContent)));
        Assert.Contains("Airport not recognised: PRIVATE FARM.", field.Find("[role='status']").TextContent);
        Assert.Equal("YWOL - PRIVATE FARM", saved);
    }

    [Fact]
    public void ChipsMoveBothDirectionsAndRemoveOnlyTheChosenOccurrence()
    {
        string? saved = null;
        var field = Render<AirportField>(parameters => parameters.Add(component => component.Label, "Via")
            .Add(component => component.Multiple, true).Add(component => component.Value, "YWOL-YSCN-YWOL")
            .Add(component => component.LoadSuggestions, _ => Task.FromResult<IReadOnlyList<string>>([]))
            .Add(component => component.ValueChanged, value => saved = value));

        field.FindAll(".airport-chip-label")[1].Click();
        field.Find("button[aria-label='Move YSCN earlier']").Click();
        Assert.Equal("YSCN - YWOL - YWOL", saved);
        Assert.True(field.Find("button[aria-label='Move YSCN earlier']").HasAttribute("disabled"));
        field.Find("button[aria-label='Move YSCN later']").Click();
        Assert.Equal("YWOL - YSCN - YWOL", saved);
        field.Find("button[aria-label='Remove YWOL, stop 3']").Click();
        Assert.Equal("YWOL - YSCN", saved);
        field.Find("button[aria-label='Remove YSCN, stop 2']").Click();
        field.Find("button[aria-label='Remove YWOL, stop 1']").Click();
        Assert.Equal("", saved);
        Assert.Empty(field.FindAll(".airport-chip"));
    }
}
