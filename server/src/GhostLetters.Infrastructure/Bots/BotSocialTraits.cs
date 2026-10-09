namespace GhostLetters.Infrastructure.Bots;

/// <summary>Социальные ожидания независимы от собственного стиля игры. Только для администратора.</summary>
public sealed record BotSocialTraits
{
    public double ExpectedActivity { get; init; } = .5;
    public double ExpectedAgreement { get; init; } = .5;
    public double Similarity { get; init; } = .5;
    public double SkillRespect { get; init; } = .75;
    public double Reciprocity { get; init; } = .5;
    public double Honesty { get; init; } = .7;
    public double Familiarity { get; init; } = .5;
    public double Forgiveness { get; init; } = .5;
    public double Influence { get; init; } = .35;

    public BotSocialTraits Clamped() => new()
    {
        ExpectedActivity = C(ExpectedActivity), ExpectedAgreement = C(ExpectedAgreement),
        Similarity = C(Similarity), SkillRespect = C(SkillRespect), Reciprocity = C(Reciprocity),
        Honesty = C(Honesty), Familiarity = C(Familiarity), Forgiveness = C(Forgiveness), Influence = C(Influence),
    };

    private static double C(double x) => double.IsFinite(x) ? Math.Clamp(x, 0, 1) : .5;

    // Stable across processes; old bots get individual social traits without changing their playing style.
    public static BotSocialTraits ForBot(Guid id)
    {
        var b = id.ToByteArray();
        double V(int i) => .1 + .8 * b[i] / 255.0;
        return new() { ExpectedActivity = V(0), ExpectedAgreement = V(1), Similarity = V(2),
            SkillRespect = V(3), Reciprocity = V(4), Honesty = V(5), Familiarity = V(6),
            Forgiveness = V(7), Influence = V(8) };
    }
}

public static class BotAffinity
{
    /// <summary>Each component is a bounded contribution to the displayed total, not a role probability.</summary>
    public static Dictionary<string, double> Observe(BotSocialTraits raw, double activity, double ownActivity,
        double? agreement, double skillDifference, bool thanked, double? honesty, int sharedGames)
    {
        var s = raw.Clamped();
        var expectations = 1 - 2 * Math.Abs(activity - s.ExpectedActivity);
        if (agreement is { } a) expectations = (expectations + 1 - 2 * Math.Abs(a - s.ExpectedAgreement)) / 2;
        return new()
        {
            ["expectations"] = 25 * expectations,
            ["similarity"] = 15 * (2 * s.Similarity - 1) * (1 - 2 * Math.Abs(activity - ownActivity)),
            ["skill"] = 20 * (2 * s.SkillRespect - 1) * Math.Clamp(skillDifference * 2, -1, 1),
            ["reciprocity"] = thanked ? 10 * s.Reciprocity : 0,
            ["cooperation"] = agreement is { } c ? 10 * (2 * c - 1) : 0,
            ["honesty"] = 15 * s.Honesty * (honesty ?? 0),
            ["familiarity"] = 5 * s.Familiarity * (1 - Math.Exp(-sharedGames / 5.0)),
        };
    }

    public static Dictionary<string, double> Update(IReadOnlyDictionary<string, double> previous,
        Dictionary<string, double> observation, double forgiveness) => observation.ToDictionary(x => x.Key, x =>
    {
        var old = previous.GetValueOrDefault(x.Key);
        // New impressions matter more; resentment fades faster for forgiving bots.
        var weight = old < 0 && x.Value > old ? .25 + .35 * forgiveness : .25;
        return Math.Clamp(old * (1 - weight) + x.Value * weight, -25, 25);
    });

    public static string Label(int score) => score switch
    {
        >= 40 => "Очень симпатизирует", >= 10 => "Симпатизирует", <= -40 => "Сильно недолюбливает",
        <= -10 => "Недолюбливает", _ => "Нейтрально",
    };
}
