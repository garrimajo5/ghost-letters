using GhostLetters.Infrastructure.Persistence.Entities;
using GhostLetters.Infrastructure.Speech;

namespace GhostLetters.Api.Tests;

public sealed class RussianGenderTests
{
    [Fact]
    public void Speaker_ChangesOnlyWhatTheBotSaysAboutItself()
    {
        RussianGender.Speaker("Кидал вот эту, я уверен.", true).Should().Be("Кидала вот эту, я уверена.");
        RussianGender.Speaker("Аня, ты прав, я согласен.", true).Should().Be("Аня, ты прав, я согласна.", "«ты прав» — о собеседнике");
        RussianGender.Speaker("Холмс бы сказал, что Призрак уверен.", true).Should().Be("Холмс бы сказал, что Призрак уверен.");
        RussianGender.Speaker("Я кидала вот эту, ты права.", false).Should().Be("Я кидал вот эту, ты права.");
    }

    [Fact]
    public void Addressee_FollowsTheirGender()
    {
        RussianGender.Addressee("Ты прав, ты ведь ошибся раньше, дорогой мой.", true).Should().Be("Ты права, ты ведь ошиблась раньше, дорогая моя.");
        RussianGender.Addressee("Ты говорила дело, голубушка.", false).Should().Be("Ты говорил дело, голубчик.");
        RussianGender.Addressee("Ты зря так поторопился.", true).Should().Be("Ты зря так поторопилась.");
        RussianGender.DependsOnAddressee("{player}, ты прав.").Should().BeTrue();
        RussianGender.DependsOnAddressee("{player}, согласен с тобой.").Should().BeFalse();
    }

    [Fact]
    public void People_AddressedAndNamedPlayers_GetTheirGender()
    {
        var genders = new Dictionary<string, bool> { ["Аня"] = true, ["Боб"] = false };
        RussianGender.People("Аня, ты ошибся. Ты зря поторопился.", genders).Should().Be("Аня, ты ошиблась. Ты зря поторопилась.",
            "обращение действует и на следующее предложение");
        RussianGender.People("Подозреваю, что Аня из чёрных: его версия плохо сходится с подсказками.", genders)
            .Should().Contain("её версия");
        RussianGender.People("Боб, ты права.", genders).Should().Be("Боб, ты прав.");
        RussianGender.People("Тестер, ты прав.", genders).Should().Be("Тестер, ты прав.", "пол не указан — не трогаем");
    }

    [Fact]
    public void Humanize_UsesTheBotsAndTheAddresseesGender()
    {
        var genders = new Dictionary<string, bool> { ["Бот Марпл"] = true, ["Аня"] = true };
        var text = ChatStyle.Humanize("Аня, ты ошибся. Отправлял вот эту — исчезла.", new Random(1), SpokenStyle.Manner.Calm, male: false, genders: genders);
        text.Should().Contain("ты ошиблась").And.NotMatchRegex(@"(?i)\b(кидал|отправлял)\b");

        // Выученная фраза с «ты прав» не говорится тому, чей пол неизвестен.
        List<(string Intent, string Emotion, string Template)> learned = [(PhraseBook.Intents.Agree, "calm", "{player}, ты прав, сто процентов!")];
        var lines = Enumerable.Range(0, 80).Select(seed => ChatStyle.Humanize("Тестер, согласен. Версия: Ставлю на место, карта 4.", new Random(seed),
            SpokenStyle.Manner.Calm, true, .5, learned, null, genders: genders)).ToList();
        lines.Should().NotContain(l => l.Contains("ты прав"));
        var toAnna = Enumerable.Range(0, 80).Select(seed => ChatStyle.Humanize("Аня, согласен. Версия: Ставлю на место, карта 4.", new Random(seed),
            SpokenStyle.Manner.Calm, true, .5, learned, null, genders: genders)).ToList();
        toAnna.Should().Contain(l => l.Contains("Аня, ты права"));
        toAnna.Should().NotContain(l => l.Contains("ты прав,"));
    }

    [Fact]
    public void Gender_IsNormalized()
    {
        Genders.Normalize("Female").Should().Be(Genders.Female);
        Genders.Normalize("ж").Should().Be(Genders.Female);
        Genders.Normalize("").Should().BeNull();
        Genders.IsFemale("male").Should().BeFalse();
        Genders.IsFemale(null).Should().BeNull();
        PersonaLines.IsFemale("Бот Марпл").Should().BeTrue();
        PersonaLines.IsFemale("Бот Пуаро").Should().BeFalse();
    }
}
