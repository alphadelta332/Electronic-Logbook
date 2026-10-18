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
}
