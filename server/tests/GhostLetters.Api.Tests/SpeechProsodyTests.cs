using GhostLetters.Infrastructure.Speech;

namespace GhostLetters.Api.Tests;

public sealed class SpeechProsodyTests
{
    [Fact]
    public void CardName_GetsLogicalStress_OncePerSentence()
    {
        SpeechProsody.Mark("Думаю, мотив — птичья клетка. А место — кошка, и кошка точно.", ["птичья клетка", "кошка"])
            .Should().Be("Думаю, мотив — птичья **клетка**. А место — **кошка**, и кошка точно.");
        SpeechProsody.Mark("Без карт.", ["клетка"]).Should().Be("Без карт.");
    }

    [Fact]
    public void Mood_PicksRole_WhenTheVoiceCan()
    {
        SpeechProsody.MoodOf("Подозреваю, что Аня из чёрных.").Should().Be(SpeechProsody.Mood.Stern);
        SpeechProsody.MoodOf("Ура, получилось!").Should().Be(SpeechProsody.Mood.Warm);
        SpeechProsody.MoodOf("Думаю, мотив 2.").Should().Be(SpeechProsody.Mood.Calm);
        SpeechProsody.RoleFor("kirill", "neutral", SpeechProsody.Mood.Stern).Should().Be("strict");
        SpeechProsody.RoleFor("lera", "neutral", SpeechProsody.Mood.Warm).Should().Be("friendly");
        SpeechProsody.RoleFor("lera", "neutral", SpeechProsody.Mood.Stern).Should().Be("neutral", "строгого у голоса нет");
        SpeechProsody.RoleFor("jane", "evil", SpeechProsody.Mood.Warm).Should().Be("evil", "амплуа из кабинета — характер");
        SpeechProsody.SpeedOf(SpokenStyle.Manner.Lively).Should().BeGreaterThan(1);
    }
}
