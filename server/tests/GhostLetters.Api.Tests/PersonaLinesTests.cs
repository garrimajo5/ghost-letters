using System.Text.RegularExpressions;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Speech;

namespace GhostLetters.Api.Tests;

public sealed class PersonaLinesTests
{
    private static readonly string[] Personas = ["Пуаро", "Марпл", "Коломбо", "Ватсон", "Фандорин", "Жеглов"];

    private static readonly string[] Situations = typeof(PersonaLines.Situations).GetFields()
        .Select(f => (string)f.GetValue(null)!).ToArray();

    [Fact]
    public void EveryDetective_HasLines_ForEverySituation()
    {
        foreach (var name in Personas)
        {
            var key = PersonaLines.KeyOf("Бот " + name);
            key.Should().NotBeNull(name);
            foreach (var situation in Situations)
                PersonaLines.Lines(key, situation).Count.Should().BeGreaterThanOrEqualTo(10, $"{name}: {situation}");
        }
        PersonaLines.KeyOf("Бот Незнакомец").Should().BeNull();
        PersonaLines.Lines(null, PersonaLines.Situations.Version).Should().BeEmpty();
    }

    [Fact]
    public void Lines_AreFilled_AndKeepWhatTheGameReads()
    {
        var names = new Dictionary<Guid, string> { [Guid.NewGuid()] = "Аня", [Guid.NewGuid()] = "Бот Пуаро" };
        var ann = names.First(x => x.Value == "Аня").Key;
        foreach (var key in Personas.Select(n => PersonaLines.KeyOf(n)!))
        {
            foreach (var situation in Situations)
            {
                foreach (var line in PersonaLines.Lines(key, situation))
                {
                    var filled = PersonaLines.Pick(key, situation, new Random(line.GetHashCode()), "тайна 2", "Аня", "мотив 1 и тайну 3",
                        "там тоже море", "Подсказка р.1 №2", "тайна 2");
                    filled.Should().NotBeNull().And.NotContain("{").And.NotContain("}");
                }
            }

            foreach (var line in PersonaLines.Lines(key, PersonaLines.Situations.Accuse))
                AccusationReader.Read(Guid.NewGuid(), line.Replace("{who}", "Аня"), names).Should()
                    .Contain(a => a.Target == ann, $"{key}: обвинение читается ботами — «{line}»");
            foreach (var situation in new[] { PersonaLines.Situations.Defense, PersonaLines.Situations.Agree, PersonaLines.Situations.Disagree })
                foreach (var line in PersonaLines.Lines(key, situation))
                {
                    AccusationReader.Read(Guid.NewGuid(), line.Replace("{who}", "Аня"), names).Should().BeEmpty($"{key}: «{line}» — не обвинение");
                    line.Should().NotContain("?", "вопрос с именем другие боты приняли бы за вопрос к себе");
                }
            foreach (var line in PersonaLines.Lines(key, PersonaLines.Situations.ClaimLost))
                line.Should().Contain("вот эту", "по этим словам клиент показывает карту письма");
            foreach (var situation in new[] { PersonaLines.Situations.Version, PersonaLines.Situations.Exclude })
                foreach (var line in PersonaLines.Lines(key, situation))
                    Regex.IsMatch(line, @"\{card(?:_acc)?\}").Should().BeTrue(line);
        }
    }

    [Fact]
    public void Detective_SpeaksInCharacter_KeepingCardRefs()
    {
        const string text = "Отправлял вот эту — исчезла.\nВерсия: По-моему, Место: 4. Кто против?";
        var poirot = Enumerable.Range(0, 60).Select(seed => ChatStyle.Humanize(text, new Random(seed), SpokenStyle.Manner.Formal, true, persona: "poirot")).ToList();
        var plain = Enumerable.Range(0, 60).Select(seed => ChatStyle.Humanize(text, new Random(seed), SpokenStyle.Manner.Formal, true)).ToList();
        var own = PersonaLines.Lines("poirot", PersonaLines.Situations.Version).Concat(PersonaLines.Lines("poirot", PersonaLines.Situations.VersionSure))
            .Select(l => l.Replace("{card_acc}", "место 4").Replace("{card}", "место 4")).ToHashSet();
        poirot.Should().Contain(s => own.Any(s.Contains), "чаще всего Пуаро говорит своими словами");
        plain.Should().NotContain(s => own.Any(s.Contains));
        poirot.Should().OnlyContain(s => s.Contains("место 4", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(s, "вот эт[оу]"));
    }

    [Fact]
    public void FrenchWords_AreSpokenInRussian()
    {
        SpeechText.For("Voilà, mon ami, всё ясно.", [], null, [], [], CardTags.Empty).Should().Be("вуаля, мон ами, всё ясно.");
    }
}
