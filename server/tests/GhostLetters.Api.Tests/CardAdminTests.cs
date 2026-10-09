using System.Net;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class CardAdminTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();
    private static CardAnnotation Annotation(double weight = .4) => new(["red", "shape-round"],
        [new("flower", 1, "Цветок"), new("memory", weight, "Воспоминание")], [new("ribbon", .2, "Лента")]);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task BotSecondaryMeaningPreferencePersists(double value)
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Админ");
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = admin.Id.ToString();
        var created = await admin.PostAsync("/api/v1/admin/bots", new { nickname = "Образы", spectra = BotSpectra.From(new BotPersonality { SecondaryMeanings = value }) });
        var id = created.Id("id");
        var list = await admin.GetAsync("/api/v1/admin/bots");
        list.EnumerateArray().Single(b => b.Id("id") == id).GetProperty("spectra").GetProperty("secondaryMeanings").GetDouble().Should().Be(value);
    }

    [Fact]
    public async Task OnlyAdminCanReadAndChangeAnnotations()
    {
        var player = await TestPlayer.LoginAsync(_factory, "Игрок");
        (await _factory.CreateClient().GetAsync("/api/v1/admin/cards")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await player.GetAsync("/api/v1/admin/cards", HttpStatusCode.Forbidden)).Code().Should().Be("FORBIDDEN");
        await player.PutAsync($"/api/v1/admin/cards/{Guid.NewGuid()}", new SaveCardRequest(null, "original", true, 0, Annotation()), HttpStatusCode.Forbidden);
        var catalog = await player.GetAsync("/api/v1/cards/sets");
        catalog.GetRawText().Should().NotContain("annotation").And.NotContain("meanings");
    }

    [Fact]
    public async Task EditPersists_ChangesNewDecks_RejectsStaleSave_AndReloadsBotMeanings()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Админ");
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = admin.Id.ToString();
        var card = new Card { Id = Guid.NewGuid(), ImageKey = "admin-" + Guid.NewGuid().ToString("N"), SetId = CatalogSeed.OriginalSetId };
        await _factory.WithDbAsync(async db => { db.Cards.Add(card); return await db.SaveChangesAsync(); });
        try
        {
            var request = new SaveCardRequest("Роза", "mirror", false, 0, Annotation());
            var saved = await admin.PutAsync($"/api/v1/admin/cards/{card.Id}", request);
            saved.Str("setCode").Should().Be("mirror");
            saved.GetProperty("version").GetInt32().Should().Be(1);
            (await admin.PutAsync($"/api/v1/admin/cards/{card.Id}", request, HttpStatusCode.Conflict)).Code().Should().Be("CARD_CHANGED");
            var list = await admin.GetAsync($"/api/v1/admin/cards?query={card.ImageKey}&setCode=mirror&active=false");
            list.GetProperty("total").GetInt32().Should().Be(1);
            list.GetProperty("cards")[0].Str("title").Should().Be("Роза");
            (await admin.GetAsync("/api/v1/cards/sets")).GetRawText().Should().NotContain(card.ImageKey);
            (await _factory.WithServiceAsync<CardCatalog, List<string>>(c => c.DeckAsync(["mirror"], default))).Should().NotContain(card.ImageKey);
            var tags = await _factory.WithServiceAsync<CardTagStore, CardTags>(s => s.LoadAsync(default));
            tags.AnnotationOf(card.ImageKey).Meanings.Should().ContainSingle(m => m.Tag == "memory" && m.Weight == .4);
            _factory.Services.GetRequiredService<CardTags>().Of(card.ImageKey).Should().BeEmpty("singleton baseline must stay immutable");
            await admin.PutAsync($"/api/v1/admin/cards/{card.Id}", request with { Version = 1, IsActive = true, Annotation = Annotation(.8) });
            (await _factory.WithServiceAsync<CardCatalog, List<string>>(c => c.DeckAsync(["mirror"], default))).Should().Contain(card.ImageKey);
            (await _factory.WithServiceAsync<CardTagStore, CardTags>(s => s.LoadAsync(default)))
                .AnnotationOf(card.ImageKey).Meanings.Should().ContainSingle(m => m.Tag == "memory" && m.Weight == .8);
            (await _factory.WithDbAsync(db => db.Cards.AsNoTracking().SingleAsync(c => c.Id == card.Id))).SetManuallyAssigned.Should().BeTrue();
        }
        finally { await _factory.WithDbAsync(db => db.Cards.Where(c => c.Id == card.Id).ExecuteDeleteAsync()); }
    }

    [Fact]
    public async Task InvalidWeightsKeysAndSetsAreRejectedBeforeUpdating()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Админ");
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = admin.Id.ToString();
        var url = $"/api/v1/admin/cards/{Guid.NewGuid()}";
        foreach (var weight in new[] { 0, -1, 1.01 })
            await admin.PutAsync(url, new SaveCardRequest(null, "original", true, 0, Annotation(weight)), HttpStatusCode.BadRequest);
        await admin.PutAsync(url, new SaveCardRequest(null, "unknown", true, 0, Annotation()), HttpStatusCode.BadRequest);
        await admin.PutAsync(url, new SaveCardRequest(null, "original", true, 0,
            new CardAnnotation(["red"], [new("flower", 1, "Цветок")], [new("flower", .3, "Цветок")])), HttpStatusCode.BadRequest);
        await admin.GetAsync("/api/v1/admin/cards?page=-1", HttpStatusCode.BadRequest);
    }
}
