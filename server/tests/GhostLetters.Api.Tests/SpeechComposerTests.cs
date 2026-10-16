using System.Text.RegularExpressions;
using GhostLetters.Infrastructure.Speech;

namespace GhostLetters.Api.Tests;

public sealed class SpeechComposerTests
{
    private static readonly TableSituation Table = new(2, 4, 3, 0, 0);

    private static SpeechIntent Intent(SpeechMood mood, params Thought[] thoughts) => new(mood, thoughts, Table);

    private static List<string> Many(SpeechIntent intent, SpokenStyle.Manner manner = SpokenStyle.Manner.Calm, bool male = true, string? persona = null,
        IReadOnlyDictionary<string, bool>? genders = null, int count = 60) =>
        Enumerable.Range(0, count).Select(seed => SpeechComposer.Compose(intent, new SpeechComposer.Voice(new Random(seed), manner, male, persona, Genders: genders))!).ToList();

    [Fact]
    public void Thoughts_BecomeWords_KeepingWhatTheTableReads()
    {
        var intent = Intent(SpeechMood.Calm,
            new Thought(ThoughtKind.ClaimLost),
            new Thought(ThoughtKind.Check, Cards: ["место 1"], Why: "по смыслу: золото"),
            new Thought(ThoughtKind.Version, "место 4"),
            new Thought(ThoughtKind.Link, "место 4", Why: "по смыслу: море", Hint: "Зацепка"),
            new Thought(ThoughtKind.Accuse, Who: "Ватсон", Strength: .6));
        foreach (var persona in new string?[] { null, "poirot", "zheglov" })
        {
            var said = Many(intent, persona: persona);
            said.Distinct().Count().Should().BeGreaterThan(20, "каждый раз по-разному");
            foreach (var s in said)
            {
                s.Should().MatchRegex("вот эт[оу]", "письмо бота — ссылкой, клиент покажет карту");
                ChatStyle.Refs(s).Should().Contain(["место 1", "место 4"]);
                s.Should().ContainEquivalentOf("зацепка");
                s.Should().Contain("Ватсон").And.MatchRegex("(?i)подозр|ч[её]рн|присмотр|убийц");
                s.Should().NotContain("{").And.NotContain("Бот ");
            }
        }
    }

    [Fact]
    public void Mood_ChoosesTheWords()
    {
        var version = new Thought(ThoughtKind.Version, "мотив 2");
        int Bangs(SpeechMood mood) => Many(Intent(mood, version), SpokenStyle.Manner.Lively, count: 120).Count(s => s.Contains('!'));
        int Doubts(SpeechMood mood) => Many(Intent(mood, version), count: 120).Count(s => Regex.IsMatch(s, "(?i)хм|не знаю|…|\\?"));
        Bangs(SpeechMood.Excited).Should().BeGreaterThan(Bangs(SpeechMood.Doubtful), "обрадованный говорит с восклицанием");
        Doubts(SpeechMood.Doubtful).Should().BeGreaterThan(Doubts(SpeechMood.Confident), "сомневающийся — с «хм» и вопросом");
        Many(Intent(SpeechMood.Calm, version)).Should().OnlyContain(s => s.Contains("мотив 2"));
    }

    [Fact]
    public void Defense_GoesFirst_AndOldDisputesNameTheRound()
    {
        var said = Many(Intent(SpeechMood.Defensive,
            new Thought(ThoughtKind.Version, "мотив 2"), new Thought(ThoughtKind.Defense, Who: "Аня")), persona: "columbo");
        said.Should().OnlyContain(s => s.IndexOf("Аня", StringComparison.Ordinal) >= 0 &&
            s.IndexOf("Аня", StringComparison.Ordinal) < s.IndexOf("мотив 2", StringComparison.Ordinal));
        said.Should().NotContain(s => Regex.IsMatch(Regex.Replace(s, @"(?i)не\s+(?:из\s+)?ч[её]рн\w*", ""), "(?i)подозр|ч[её]рн|убийц"),
            "защита — без слов-обвинений, иначе боты прочтут её как обвинение");

        var old = Many(Intent(SpeechMood.Calm, new Thought(ThoughtKind.Disagree, "мотив 1", "Боб", Round: 1), new Thought(ThoughtKind.Version, "мотив 2")));
        old.Should().OnlyContain(s => s.Contains("в прошлом раунде") && s.Contains("Боб"));
    }

    [Fact]
    public void Gender_OfSpeakerAndAddressee()
    {
        var said = Many(Intent(SpeechMood.Confident, new Thought(ThoughtKind.VersionSure, "мотив 2"), new Thought(ThoughtKind.Agree, "мотив 2", "Аня")),
            male: false, genders: new Dictionary<string, bool> { ["Аня"] = true });
        said.Should().NotContain(s => Regex.IsMatch(s, @"(?<![а-яё])уверен(?![а-яё])"), "женщина говорит «уверена»");
        said.Should().NotContain(s => Regex.IsMatch(s, @"(?<![а-яё])согласен(?![а-яё])"));
    }
}
