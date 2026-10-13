using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Speech;

namespace GhostLetters.Api.Tests;

public sealed class PhraseBookTests
{
    private static readonly CardTags Tags = new CardTags(new Dictionary<string, HashSet<string>> { ["cage"] = [], ["ear"] = [], ["cat"] = [] })
        .WithNames("""{"cage": ["клетка"], "ear": ["ухо"], "cat": ["кошка"]}""");

    private static readonly List<BoardRow> Board =
    [
        new() { Category = Category.Motive, Cards = ["cat", "cage"] },
        new() { Category = Category.Place, Cards = ["ear"] },
    ];

    private static IReadOnlyList<LearnedPhrase> Learn(string text) => PhraseBook.Learn(text, Board, [], ["Аня", "Бот Пуаро"], Tags);

    [Fact]
    public void Sentence_IsSplitIntoThoughtEmotionAndManner()
    {
        var phrase = Learn("Ну короче, клетка — это оно, сто процентов!").Should().ContainSingle().Subject;
        phrase.Intent.Should().Be(PhraseBook.Intents.Version);
        phrase.Emotion.Should().Be("excited");
        phrase.Manner.Should().Be(SpokenStyle.Manner.Lively);
        phrase.Template.Should().Be("Ну короче, {card} — это оно, сто процентов!");

        var formal = Learn("Полагаю, коллеги, мотив два вероятнее прочих, думаю так.").Should().ContainSingle().Subject;
        formal.Manner.Should().Be(SpokenStyle.Manner.Formal);
        formal.Template.Should().Contain("коллеги, {card} вероятнее");
        Learn("Ставлю на мотив два, уверен!").Should().BeEmpty("после предлога падеж карты не угадать");
        Learn("Не согласен про Пуаро, он темнит.").Should().BeEmpty("«про Пуаро» — бот скажет «про Ватсон»");
        PhraseBook.Fill("Не согласен про {player}.", null, "Ватсон").Should().BeEmpty();
        PhraseBook.Fill("Ну короче, мотив — {card} — это оно!", "тайна 2", null).Should().Be("Ну короче, тайна 2 — это оно!");

        var doubt = Learn("Кажется, Пуаро темнит").Should().ContainSingle().Subject;
        doubt.Intent.Should().Be(PhraseBook.Intents.Accuse);
        doubt.Emotion.Should().Be("doubtful");
        doubt.Template.Should().Be("Кажется, {player} темнит");

        Learn("Я кидала кошка, а она исчезла.").Should().ContainSingle().Which.Intent.Should().Be(PhraseBook.Intents.ClaimLost);
        Learn("Точно не ухо, вычеркиваем.").Should().ContainSingle().Which.Intent.Should().Be(PhraseBook.Intents.Exclude);
    }

    [Fact]
    public void Unusable_IsNotLearned()
    {
        Learn("Привет всем, как дела?").Should().BeEmpty("нет мысли об игре");
        Learn("Ставлю на клетку, уверен.").Should().BeEmpty("карта не в именительном — бот скажет неграмотно");
        Learn("Со стола: думаю, эта клетка").Should().BeEmpty("собрано кнопками");
        Learn("Пуаро, ты идиот, убийца").Should().BeEmpty("грубость");
        Learn("Думаю, клетка, смотри www.site.ru").Should().BeEmpty("ссылки");
        Learn("Думаю, клетка или ухо, по-моему").Should().BeEmpty("две карты в одной фразе");
    }

    [Fact]
    public void SameThought_GivesSameHash_AndTemplateIsFilled()
    {
        Learn("Думаю, клетка!")[0].Hash.Should().Be(Learn("думаю,  кошка!")[0].Hash);
        PhraseBook.Fill("Ну, {card} — это оно!", "ухо", null).Should().Be("Ну, ухо — это оно!");
        PhraseBook.Fill("{player} темнит", null, "Бот Пуаро").Should().Be("Пуаро темнит");
        PhraseBook.Fill("{player} темнит", null, null).Should().BeEmpty("имени нет — фраза не годится");
    }

    [Fact]
    public void Bot_SpeaksLearnedPhrases_OfItsEmotion()
    {
        List<(string Intent, string Emotion, string Template)> learned =
        [
            (PhraseBook.Intents.Version, "excited", "Ну короче, {card} — это оно!"),
            (PhraseBook.Intents.Version, "doubtful", "Может, {card}, не знаю"),
        ];
        var said = Enumerable.Range(0, 60)
            .Select(seed => SpokenStyle.Rephrase("Версия: скорее всего, мотив — клетка.", new Random(seed), SpokenStyle.Manner.Lively, true, learned, ["excited"]))
            .ToList();
        said.Should().Contain(s => s.Contains("Ну короче, клетка — это оно!"), "часть реплик — фразами людей");
        said.Should().Contain(s => !s.Contains("Ну короче"), "но не все");
        said.Should().NotContain(s => s.Contains("не знаю"), "сомнение не подходит этому боту");
        said.Should().OnlyContain(s => s.Contains("клетка", StringComparison.OrdinalIgnoreCase));
    }
}
