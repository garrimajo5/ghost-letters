using GhostLetters.Infrastructure.Speech;

namespace GhostLetters.Api.Tests;

public sealed class ChatStyleTests
{
    // Реплика бота из живой партии (13.10): шаблон рассуждения.
    private const string Template = "Отправлял вот эту — исчезла.\nПроверял: место 1 — по смыслу: золото.\nИсчезло — значит, скорее не они. Но одной проверки мало.\n" +
        "Версия: По-моему, Место: 4. Кто против?\nЗацепка → место 4: по смыслу: море; по форме: предмет расположен по диагонали. Подозреваю, что Бот Ватсон из чёрных.";

    [Theory]
    [InlineData(SpokenStyle.Manner.Calm)]
    [InlineData(SpokenStyle.Manner.Lively)]
    [InlineData(SpokenStyle.Manner.Formal)]
    public void Template_BecomesHumanText_KeepingCardsAndAccusation(SpokenStyle.Manner manner)
    {
        var said = Enumerable.Range(0, 40).Select(seed => ChatStyle.Humanize(Template, new Random(seed), manner, male: true)).ToList();
        said.Distinct().Count().Should().BeGreaterThan(10, "каждый раз по-разному");
        foreach (var s in said)
        {
            s.Should().NotContain("Версия:").And.NotContain("Проверял:").And.NotContain("по смыслу:").And.NotContain("\n").And.NotContain("Место: 4");
            s.Should().MatchRegex("вот эт[оу]", "письмо бота остаётся ссылкой — клиент покажет карту");
            ChatStyle.Refs(s).Should().Contain(["место 1", "место 4"]);
            s.Should().ContainEquivalentOf("зацепка");
            s.Should().Contain("Ватсон").And.NotContain("Бот Ватсон");
            s.Should().MatchRegex("(?i)подозр|ч[её]рн|присмотр", "по этим словам боты читают обвинения");
            s.Length.Should().BeLessThan(Template.Length);
        }
    }

    [Fact]
    public void FemaleBot_WritesInFeminine_AndLearnedPhrasesAreUsed()
    {
        ChatStyle.Humanize("Отправлял вот эту — исчезла.", new Random(3), SpokenStyle.Manner.Calm, male: false).Should().NotMatchRegex(@"(?i)\b(кидал|отправлял)\b");
        List<(string Intent, string Emotion, string Template)> learned = [(PhraseBook.Intents.Version, "excited", "Ну короче, {card} — это оно!")];
        var lines = Enumerable.Range(0, 60).Select(seed => ChatStyle.Humanize("Версия: Ставлю на место, карта 4.", new Random(seed), SpokenStyle.Manner.Lively, true, .8, learned, ["excited"])).ToList();
        lines.Should().Contain("Ну короче, место 4 — это оно!");
        lines.Should().OnlyContain(l => l.Contains("место 4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exclusion_AndAgreement_StayWhatTheyMean()
    {
        foreach (var seed in Enumerable.Range(0, 20))
        {
            var s = ChatStyle.Humanize("Аня, тут не соглашусь. Версия: Исключаю место 2: связь с подсказками случайная.", new Random(seed), SpokenStyle.Manner.Calm, true);
            s.Should().StartWith("Аня, ");
            s.Should().MatchRegex("(?i)не соглаш|не согласен|спорно");
            s.Should().MatchRegex("(?i)мимо|не верю|отпадает|исключаю");
            ChatStyle.Refs(s).Should().Equal("место 2");
        }

        ChatStyle.Humanize("Привет всем!\nКак дела?", new Random(1), SpokenStyle.Manner.Calm, true).Should().Be("Привет всем! Как дела?");
        SpokenStyle.Masculine("Я кидала кошку, уверена.").Should().Be("Я кидал кошку, уверен.");
    }

    [Fact]
    public void FinalSummary_StaysAList_AndKeepsWordsOtherBotsRead()
    {
        const string summary = "Собрал версию по всем открытым уликам за партию:\n• мотив 2 — связь с открытыми уликами слабая, пока предположение.\n" +
            "• Пересмотрел выбор: место 1 → место 3. место 3 — Подсказка р.1 №2: по смыслу: море.\nУчёл и свои исчезнувшие письма: их вес зависит от того, насколько я доверяю такой проверке." +
            " По игрокам пока недостаточно оснований для уверенного обвинения.";
        foreach (var seed in Enumerable.Range(0, 20))
        {
            var s = ChatStyle.Humanize(summary, new Random(seed), SpokenStyle.Manner.Calm, true);
            s.Should().Contain("\n• мотив 2 — ").And.Contain("\n• ").And.NotContain("связь с открытыми уликами слабая").And.NotContain("по смыслу:");
            ChatStyle.Refs(s).Should().Contain(["мотив 2", "место 1", "место 3"]);
            s.Should().Contain("Подсказка р.1 №2");
            s.Split('\n').Should().HaveCount(4, "после списка — новая строка");
        }

        var answer = ChatStyle.Humanize("Аня, отвечаю про голосование: здесь я пока не согласен. Сейчас выберу тайна 2. Остальные ряды моей версии — на прикреплённых картах.",
            new Random(2), SpokenStyle.Manner.Formal, true);
        answer.Should().StartWith("Аня, отвечаю").And.Contain("выберу тайну 2");
    }

    [Fact]
    public void Secret_IsDeclined_AfterPrepositions()
    {
        foreach (var seed in Enumerable.Range(0, 30))
        {
            var s = ChatStyle.Humanize("Проверял: тайна 5 и способ 2 — по смыслу: путешествия.\nВерсия: Ставлю на тайна, карта 3.", new Random(seed), SpokenStyle.Manner.Lively, true);
            s.Should().NotMatchRegex("(?i)\\b(в|на|про|за) тайна");
            ChatStyle.Refs(s).Should().Contain(["тайна 5", "способ 2", "тайна 3"]);
        }
    }

    [Fact]
    public void SameAccusation_IsNotRepeatedEveryMessage()
    {
        var earlier = new[] { "Ставлю на то, что Убийца — Коломбо." };
        var said = Enumerable.Range(0, 30).Select(seed => ChatStyle.Humanize("Версия: Ставлю на мотив, карта 5. Думаю, Убийца — Бот Коломбо: его голоса расходятся с моим чтением подсказок.",
            new Random(seed), SpokenStyle.Manner.Calm, true, earlier: earlier)).ToList();
        said.Count(s => s.Contains("Коломбо")).Should().BeLessThan(15);
        said.Should().OnlyContain(s => s.Contains("мотив 5"));
        ChatStyle.Humanize("Подозреваю, что Бот Жеглов из чёрных.", new Random(1), SpokenStyle.Manner.Calm, true, earlier: earlier).Should().Contain("Жеглов");
    }
}
