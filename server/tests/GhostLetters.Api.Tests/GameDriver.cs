using System.Text.Json;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Играет партию живыми командами через REST: на каждом шаге первый по месту игрок,
/// у которого есть доступная команда, делает простой допустимый ход по своей проекции.
/// Истина — нулевые столбцы, поэтому голосование по рядам угадывает улики.
/// </summary>
public sealed class GameDriver(GameHarness game)
{
    public const string AwardCode = "steel_balls";

    private static readonly HashSet<string> Passive = ["Like", "RaiseHand", "GiveFloor", "TeamSuggest"];

    public int Steps { get; private set; }

    public async Task<JsonElement> ViewOfAsync(TestPlayer p) => await p.ViewAsync(game.GameId);

    /// <summary>Один ход. false — ходить некому (партия закончена).</summary>
    public async Task<bool> StepAsync()
    {
        foreach (var p in game.Players)
        {
            var view = await p.ViewAsync(game.GameId);
            if (view.Str("phase") == "Finished")
            {
                return false;
            }

            var allowed = view.GetProperty("allowedCommands").EnumerateArray().Select(x => x.GetString()!)
                .Where(c => !Passive.Contains(c)).ToList();
            if (allowed.Count == 0)
            {
                continue;
            }

            var type = allowed.Contains("Discard") ? "Discard" : allowed[0];
            await p.CommandAsync(game.GameId, type, Payload(p, view, type));
            Steps++;
            return true;
        }

        return false;
    }

    public async Task RunUntilAsync(Func<string, bool> phase, int maxSteps = 1000)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            var current = (await game.Host.ViewAsync(game.GameId)).Str("phase");
            if (phase(current))
            {
                return;
            }

            if (!await StepAsync())
            {
                break;
            }
        }

        var last = (await game.Host.ViewAsync(game.GameId)).Str("phase");
        phase(last).Should().BeTrue($"партия должна дойти до нужной фазы, а сейчас {last}");
    }

    private object Payload(TestPlayer p, JsonElement view, string type)
    {
        var me = view.GetProperty("me");
        var hand = me.GetProperty("hand").EnumerateArray().Select(c => c.GetString()!).ToList();
        var rows = view.GetProperty("board").GetArrayLength();
        var others = view.GetProperty("players").EnumerateArray()
            .Where(x => !x.GetProperty("isGhost").GetBoolean() && x.Id("id") != p.Id).Select(x => x.Id("id")).ToList();
        var finale = view.GetProperty("finale");

        switch (type)
        {
            case "ChooseTruth":
            case "NameTruth":
                return new { columns = Enumerable.Repeat(0, rows).ToArray() };
            case "GiveFirstClue":
                return new { cardId = hand[0] };
            case "SendLetter":
                return new { cardIds = hand.Take(game.Players.Count == 2 ? 2 : 1).ToArray() };
            case "RevealHints":
                return new { cardIds = new[] { view.GetProperty("mailboxForGhost")[0].GetString() } };
            case "Discard":
                return new { cardId = (string?)null };
            case "CastVote":
            {
                var stage = finale.GetProperty("currentStage");
                if (stage.Str("kind") == "Row")
                {
                    return new { column = stage.GetProperty("candidateColumns")[0].GetInt32() };
                }

                var suspect = stage.GetProperty("candidateSuspects").EnumerateArray().Select(x => x.GetGuid())
                    .Where(x => x != p.Id).Cast<Guid?>().FirstOrDefault();
                return new { suspect };
            }

            case "HuntPick":
                return new { target = others[0], guess = "Witness" };
            case "BlackmailerPick":
                return new { target = others[0] };
            case "Nominate":
                return p == game.Players[0] && game.Players.Count > 1
                    ? new { code = (string?)AwardCode, nominee = (Guid?)game.Players[1].Id }
                    : new { code = (string?)null, nominee = (Guid?)null };
            case "AwardVote":
            {
                var first = finale.GetProperty("awards")[0];
                var own = first.GetProperty("mineNomination").GetBoolean() || first.Id("nominee") == p.Id;
                return new { entry = own ? (int?)null : 0 };
            }

            default:
                return new { };
        }
    }
}
