using System.Net;
using GhostLetters.Infrastructure.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GhostLetters.Api.Tests;

public sealed class SpokenStyleTests
{
    private const string Line = "Отправлял вот эту — кошка — исчезла. Проверял: место — ухо и мотив — клетка — по смыслу: звук; по форме: острые выступы. " +
        "Раз исчезло — это ослабляет эти версии, но не исключает: другие улики могут перевесить. Версия: скорее всего, мотив — клетка. " +
        "Целиком думаю так: мотив — клетка, место — ухо. Подозреваю, что Бот Пуаро из чёрных: его версия хуже объясняет открытые подсказки, чем соседние карты.";

    [Fact]
    public void SameLine_SoundsDifferent_ButKeepsEveryCard()
    {
        var variants = Enumerable.Range(0, 40)
            .Select(seed => SpokenStyle.Rephrase(Line, new Random(seed), SpokenStyle.Manner.Calm, male: true)).ToList();
        variants.Distinct().Count().Should().BeGreaterThan(20, "одна и та же реплика звучит каждый раз по-новому");
        foreach (var v in variants)
        {
            v.Should().ContainEquivalentOf("кошка").And.ContainEquivalentOf("ухо").And.ContainEquivalentOf("клетка").And.Contain("Пуаро");
            v.Should().NotContain("Бот Пуаро", "приставку «Бот» вслух не говорят");
            v.Should().NotContain("по смыслу:").And.NotContain("Целиком думаю так");
        }
    }

    [Fact]
    public void FemaleVoice_SpeaksInFeminine_GhostStaysMasculine()
    {
        var text = SpokenStyle.Feminine("Я отправлял кошку, проверял ухо. Согласен. Призрак его не открыл и оставил закрытым.");
        text.Should().Be("Я отправляла кошку, проверяла ухо. Согласна. Призрак его не открыл и оставил закрытым.");
    }

    [Fact]
    public void UnknownSentences_StayAsIs()
    {
        SpokenStyle.Rephrase("Сначала послушаю, кто какое письмо отправлял.", new Random(1), SpokenStyle.Manner.Formal, true)
            .Should().EndWith("Сначала послушаю, кто какое письмо отправлял.");
    }

    [Fact]
    public void Writer_AcceptsOnlyAnswersWithAllCards()
    {
        string[] cards = ["клетка", "птичья клетка", "ухо"];
        const string x = "Мотив — птичья клетка, место — ухо.";
        SpeechWriter.Accept("Ставлю на птичью клетку, а ухо — мимо.", x, cards, 200).Should().Be("Ставлю на птичью клетку, а ухо — мимо.");
        SpeechWriter.Accept("Ставлю на клетку.", x, cards, 200).Should().BeNull("пропало ухо");
        SpeechWriter.Accept("**Клетка** и ухо", x, cards, 200).Should().BeNull("разметка");
        SpeechWriter.Accept(new string('а', 300) + " клетка птичья ухо", x, cards, 200).Should().BeNull("слишком длинно");
        SpeechWriter.Check("Пуаро темнит, мутный он какой-то.", "Пуаро темнит, он мутный.", ["клетка"], 200).Text
            .Should().NotBeNull("карты нет и в исходной реплике — её не требуем");
        SpeechWriter.Check("Думаю, это оно.", "Думаю, мотив — клетка.", ["клетка"], 200).Why.Should().Contain("клетка");
    }

    [Fact]
    public async Task Writer_CallsYandexGpt_AndFallsBackOnBadAnswer()
    {
        var handler = new FakeGpt("Моё письмо, кошка, пропало — значит, ухо под вопросом.");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Speech:ApiKey"] = "key", ["Speech:FolderId"] = "b1g",
        }).Build();
        var writer = new SpeechWriter(config, NullLogger<SpeechWriter>.Instance, TimeProvider.System, handler);
        writer.Enabled.Should().BeTrue();
        var speaker = new Speaker("Марпл", "Помнит всех и всё.", Male: false);
        (await writer.RephraseAsync("Отправлял вот эту — кошка — исчезла. Проверял: ухо.", ["кошка", "ухо"], speaker, CancellationToken.None))
            .Should().Be("Моё письмо, кошка, пропало — значит, ухо под вопросом.");
        System.Text.RegularExpressions.Regex.Unescape(handler.Body!).Should().Contain("gpt://b1g/yandexgpt/latest").And.Contain("Марпл");
        handler.Project.Should().Be("b1g");

        handler.Answer = "Что-то совсем другое.";
        (await writer.RephraseAsync("Отправлял вот эту — кошка — исчезла.", ["кошка"], speaker, CancellationToken.None)).Should().BeNull();
        writer.AcceptedToday.Should().Be(1);
        writer.LastProblem.Should().Contain("не прошёл проверку", "в кабинете видно, почему пересказ не взят");

        var off = new SpeechWriter(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Speech:ApiKey"] = "key" }).Build(),
            NullLogger<SpeechWriter>.Instance, TimeProvider.System, handler);
        off.Enabled.Should().BeFalse("без каталога YandexGPT не вызывается");
    }

    private sealed class FakeGpt(string answer) : HttpMessageHandler
    {
        public string Answer = answer;
        public string? Body;
        public string? Project;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            Project = request.Headers.TryGetValues("OpenAI-Project", out var v) ? v.Single() : null;
            var json = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = Answer } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
