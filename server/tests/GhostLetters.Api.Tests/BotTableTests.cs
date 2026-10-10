using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Бот у доски улик (D02): шаги «смотрю → нить», письма и проверки, согласие, булавки — без базы.</summary>
public sealed class BotTableTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ghost = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();

    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["knife"] = ["knife", "metal", "sharp", "weapon"],
        ["rose"] = ["rose", "flower", "red", "love"],
        ["boat"] = ["boat", "sea", "water", "travel"],
        ["cat"] = ["cat", "animal", "fur"],
        ["sword"] = ["sword", "metal", "sharp", "weapon"],
        ["dog"] = ["dog", "animal", "fur"],
    });

    // Ряд 0: knife | rose; ряд 1: boat | cat. Версия бота — knife и cat.
    private static readonly IReadOnlyList<int> Plan = [0, 1];

    private static PlayerView View(TableView table, Role role = Role.Detective, IReadOnlyList<MyLetterView>? letters = null) => new(
        Guid.NewGuid(), 1, Phase.Discussion, 2, 3, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["knife", "rose"]), new BoardRowView(Category.Place, ["boat", "cat"])],
        [new HintGroupView(0, []), new HintGroupView(1, ["sword"])], 0,
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, role, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
        ],
        new MeView(Me, role, [], letters ?? []),
        null, 0, null, null, null, null, [], [], null, Table: table);

    private static TableView Table(IReadOnlyList<TableThreadView>? threads = null, IReadOnlyList<TablePinView>? pins = null,
        IReadOnlyList<TableCheckView>? checks = null, IReadOnlyList<LetterClaimView>? claims = null, bool pinsOnly = false) =>
        new(threads ?? [], pins ?? [], checks ?? [], claims ?? [], true, pinsOnly);

    private static TableThreadView Thread(int id, Guid author, string target, IReadOnlyList<Guid>? endorsed = null) =>
        new(id, author, 1, TableSourceKind.Hint, "sword", target, TableStance.For, "предмет", endorsed ?? [], []);

    private static BotThought Thought(params string[] notes) =>
        new(DateTimeOffset.UnixEpoch, notes.Select((_, i) => $"c{i}").ToList(), notes);

    [Fact]
    public void Looks_At_Hint_Then_Links_It_To_Own_Version()
    {
        var look = BotTable.Next(View(Table()), Tags, null, Plan, [])!;
        look.Ops.Should().BeEmpty();
        look.Cards.Should().Equal("sword", "knife");
        look.Notes.Should().Contain("смотрю");
        look.Thought.Should().Contain("р.1").And.Contain("мотив 1");

        var said = new BotThought(DateTimeOffset.UnixEpoch, look.Cards, look.Notes);
        var link = BotTable.Next(View(Table()), Tags, null, Plan, [said])!;
        var op = link.Ops.Should().ContainSingle().Subject;
        op.Should().Be(new TableOp(TableOpKind.Link, TableSourceKind.Hint, "sword", "knife", TableStance.For, "предмет"));
        link.Thought.Should().Contain("Тяну нить");
    }

    [Fact]
    public void Honest_Bot_Claims_Vanished_Letter_Then_Checks_Similar_Card()
    {
        var mine = Thread(1, Me, "knife");
        var letters = new[] { new MyLetterView(1, "dog", false) };
        var claim = BotTable.Next(View(Table([mine]), letters: letters), Tags, null, Plan, [Thought("улика", "нить")])!;
        claim.Ops.Should().ContainSingle().Which.Should().Be(new TableOp(TableOpKind.Claim, Source: "dog", Round: 1));

        var claimed = Table([mine], claims: [new LetterClaimView(Me, 1, "dog")]);
        var check = BotTable.Next(View(claimed, letters: letters), Tags, null, Plan, [Thought("улика", "нить"), Thought("письмо")])!;
        check.Ops.Should().ContainSingle().Which.Should().Be(new TableOp(TableOpKind.Check, Target: "cat"));
        check.Thought.Should().Contain("Вычёркиваю");
    }

    [Fact]
    public void Killer_Team_Does_Not_Reveal_Own_Letters()
    {
        var letters = new[] { new MyLetterView(1, "dog", false) };
        var step = BotTable.Next(View(Table([Thread(1, Me, "knife")]), Role.Accomplice, letters), Tags, null, Plan, [Thought("улика", "нить")]);
        step!.Ops.Should().NotContain(o => o.Kind == TableOpKind.Claim || o.Kind == TableOpKind.Check);
    }

    [Fact]
    public void Endorses_Matching_Thread_Of_Another_Player_Once()
    {
        var table = Table([Thread(1, Me, "knife"), Thread(2, Ann, "knife")]);
        var step = BotTable.Next(View(table), Tags, null, Plan, [Thought("улика", "нить")])!;
        step.Ops.Should().ContainSingle().Which.Should().Be(new TableOp(TableOpKind.Endorse, Thread: 2));

        var agreed = Table([Thread(1, Me, "knife"), Thread(2, Ann, "knife", [Me])]);
        BotTable.Next(View(agreed), Tags, null, Plan, [Thought("улика", "нить"), Thought("согласен")])!
            .Ops.Should().NotContain(o => o.Kind == TableOpKind.Endorse);
    }

    [Fact]
    public void Pins_Whole_Version_When_Nothing_Else_To_Do_And_Only_Pins_In_Voting()
    {
        var pins = BotTable.Next(View(Table([Thread(1, Me, "knife")])), Tags, null, Plan, [Thought("улика", "нить")])!;
        pins.Ops.Should().Equal(new TableOp(TableOpKind.Pin, Target: "knife"), new TableOp(TableOpKind.Pin, Target: "cat"));
        pins.Thought.Should().Contain("мотив 1").And.Contain("место 2");

        var voting = BotTable.Next(View(Table(pinsOnly: true)), Tags, null, Plan, [])!;
        voting.Ops.Should().OnlyContain(o => o.Kind == TableOpKind.Pin);

        var done = Table([Thread(1, Me, "knife")], [new TablePinView(Me, 0, 0), new TablePinView(Me, 1, 1)]);
        BotTable.Next(View(done), Tags, null, Plan, [Thought("улика", "нить")]).Should().BeNull();
    }

    [Fact]
    public void Ghost_Closed_Board_And_Step_Limit_Stop_The_Bot()
    {
        BotTable.Next(View(Table(), Role.Ghost), Tags, null, Plan, []).Should().BeNull();
        BotTable.Next(View(Table() with { CanPost = false }), Tags, null, Plan, []).Should().BeNull();
        var many = Enumerable.Range(0, BotTable.MaxStepsPerRound).Select(_ => Thought("x")).ToList();
        BotTable.Next(View(Table()), Tags, null, Plan, many).Should().BeNull();
        var almost = many.Skip(1).ToList();
        BotTable.Next(View(Table()), Tags, null, Plan, almost)!.Ops.Should().OnlyContain(o => o.Kind == TableOpKind.Pin,
            "последний шаг раунда — булавки версии");
        BotTable.Next(View(Table(pinsOnly: true)), Tags, null, Plan, many)!.Ops.Should().OnlyContain(o => o.Kind == TableOpKind.Pin);
        BotTable.Due([new BotThought(DateTimeOffset.UnixEpoch, [], [])], DateTimeOffset.UnixEpoch.AddSeconds(3)).Should().BeFalse();
    }
}
