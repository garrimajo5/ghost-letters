using System.Net;
using System.Text;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GhostLetters.Api.Tests;

public sealed class SpeechTests
{
    private static readonly CardTags Tags = new CardTags(new Dictionary<string, HashSet<string>>
    {
        ["cage"] = ["cage"], ["bell"] = ["bell"], ["cat"] = ["cat"], ["rose"] = ["rose"],
    }).WithNames("""{"cage": ["клетка", "птичья клетка"], "bell": ["колокол"], "cat": ["кошка"], "rose": [" роза ", "Роза", ""]}""");

    private static readonly List<BoardRow> Board =
    [
        new() { Category = Category.Motive, Cards = ["rose", "cage"] },
        new() { Category = Category.Place, Cards = ["cat"] },
    ];

    private static readonly List<HintGroup> Hints = [new() { Round = 0, Cards = ["cat"] }, new() { Round = 2, Cards = ["bell"] }];

    [Fact]
    public void Names_AreLoadedCleaned_AndUsedForSpeech()
    {
        Tags.NamesOf("cage").Should().Equal("клетка", "птичья клетка");
        Tags.NamesOf("rose").Should().Equal("роза");
        Tags.NameOf("unknown").Should().BeNull();

        var text = SpeechText.For("Со стола: Подсказка р.2 №1 → мотив 2: по смыслу: звук.\nВерсия: Место — карта 1. Зацепка подходит.",
            [], null, Board, Hints, Tags);
        text.Should().Be("подсказка второго раунда — колокол указывает на мотив — клетка: по смыслу: звук. Версия: Место — кошка. Зацепка — кошка подходит.");
    }

    [Fact]
    public void ClaimedLetter_IsNamed_UnknownRefsStayText()
    {
        SpeechText.For("Кидал вот эту — исчезла. Мотив 9 и тайна 1 — мимо.", ["cat"], ["кидал эту"], Board, Hints, Tags)
            .Should().Be("Кидал вот эту — кошка — исчезла. Мотив 9 и тайна 1 — мимо.");
    }

    [Fact]
    public void LongText_IsCutAtSentence_AndChunkedUnder250()
    {
        var sentence = string.Join(" ", Enumerable.Repeat("слово", 30)) + ". ";
        var cut = SpeechText.Cut(string.Concat(Enumerable.Repeat(sentence, 6)));
        cut.Length.Should().BeLessThanOrEqualTo(SpeechText.MaxLength);
        cut.Should().EndWith(".");
        var chunks = SpeechText.Chunks(cut);
        chunks.Should().OnlyContain(c => c.Length <= 240);
        string.Join(" ", chunks).Should().Be(cut);
        SpeechText.Chunks(new string('а', 500)).Should().OnlyContain(c => c.Length <= 240);
    }

    [Fact]
    public void Mp3Duration_CountsFrames_AndSkipsId3()
    {
        Mp3.DurationMs(Frames(10)).Should().Be(261);
        var id3 = new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 20 }.Concat(new byte[20]).Concat(Frames(10)).ToArray();
        Mp3.DurationMs(id3).Should().Be(261);
        Mp3.DurationMs([1, 2, 3, 4, 5]).Should().Be(0);
    }

    [Fact]
    public void ReadAudio_JoinsChunks_FromLinesOrConcatenatedObjects()
    {
        var a = Convert.ToBase64String([1, 2]);
        var b = Convert.ToBase64String([3]);
        var lines = Encoding.UTF8.GetBytes($"{{\"result\":{{\"audioChunk\":{{\"data\":\"{a}\"}},\"textChunk\":{{\"text\":\"{{ }}\"}}}}}}\n{{\"result\":{{\"audioChunk\":{{\"data\":\"{b}\"}}}}}}\n");
        SpeechKit.ReadAudio(lines).Should().Equal(1, 2, 3);
        var glued = Encoding.UTF8.GetBytes($"{{\"result\":{{\"audioChunk\":{{\"data\":\"{a}\"}}}}}}{{\"result\":{{\"audioChunk\":{{\"data\":\"{b}\"}}}}}}");
        SpeechKit.ReadAudio(glued).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Synthesize_SendsApiKeyAndVoice_CachesAndRespectsDailyLimit()
    {
        var handler = new FakeSpeechKit();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Speech:ApiKey"] = "secret", ["Speech:DailyCharLimit"] = "30",
        }).Build();
        var speech = new SpeechKit(config, NullLogger<SpeechKit>.Instance, TimeProvider.System, handler);
        speech.Enabled.Should().BeTrue();

        var audio = await speech.SynthesizeAsync("Мотив — клетка.", "kirill", "strict", CancellationToken.None);
        audio!.DurationMs.Should().Be(261);
        handler.Requests.Should().ContainSingle();
        handler.Authorization.Should().Be("Api-Key secret");
        handler.Bodies[0].Should().Contain("\"voice\":\"kirill\"").And.Contain("\"role\":\"strict\"").And.Contain("MP3");

        (await speech.SynthesizeAsync("Мотив — клетка.", "kirill", "strict", CancellationToken.None)).Should().BeSameAs(audio);
        handler.Requests.Should().ContainSingle("одинаковая реплика тем же голосом — из кэша");

        (await speech.SynthesizeAsync("Очень длинная реплика сверх лимита.", "julia", null, CancellationToken.None)).Should().BeNull();
        handler.Requests.Should().ContainSingle("сверх дневного лимита SpeechKit не вызывается");

        handler.Fail = true;
        (await speech.SynthesizeAsync("Ошибка.", "julia", null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WithoutKey_SpeechIsOff()
    {
        var speech = new SpeechKit(new ConfigurationBuilder().Build(), NullLogger<SpeechKit>.Instance, TimeProvider.System, new FakeSpeechKit());
        speech.Enabled.Should().BeFalse();
        (await speech.SynthesizeAsync("Привет.", "kirill", null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void BotVoice_ChosenOrStableByBot()
    {
        var bot = Guid.NewGuid();
        BotVoices.For(bot, "kirill", "strict").Should().Be(("kirill", "strict"));
        BotVoices.For(bot, "kirill", "evil").Should().Be(("kirill", "neutral"), "нет такого амплуа — основное");
        BotVoices.For(bot, "", "").Should().Be(BotVoices.For(bot, null, null), "без выбора — постоянный голос");
        BotVoices.Find(BotVoices.For(bot, null, null).Voice).Should().NotBeNull();
    }

    internal static byte[] Frames(int count)
    {
        var frame = new byte[417];
        frame[0] = 0xff; frame[1] = 0xfb; frame[2] = 0x90; frame[3] = 0x00;
        return Enumerable.Range(0, count).SelectMany(_ => frame).ToArray();
    }

    private sealed class FakeSpeechKit : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        public string? Authorization;
        public bool Fail;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            Authorization = request.Headers.Authorization?.ToString();
            if (Fail) return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"bad key\"}") };
            var data = Convert.ToBase64String(Frames(10));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"result\":{{\"audioChunk\":{{\"data\":\"{data}\"}}}}}}\n"),
            };
        }
    }
}
