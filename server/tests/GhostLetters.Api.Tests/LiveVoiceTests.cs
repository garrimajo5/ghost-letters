using System.Net;
using System.Text;
using System.Text.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Speech;
using GhostLetters.Infrastructure.Voice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GhostLetters.Api.Tests;

public sealed class LiveVoiceTests
{
    private static GameState Game()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var deck = Enumerable.Range(1, 300).Select(i => $"c{i:000}").ToList();
        var state = GameEngine.Create(Guid.NewGuid(), ids, new GameSettings(), deck, 3);
        state.Players[0].Role = Role.Ghost;
        foreach (var p in state.Players.Skip(1)) p.Role = Role.Detective;
        state.Round = 1;
        state.TotalRounds = 3;
        state.Phase = Phase.Discussion;
        state.SpeakingOrder = state.Players.Skip(1).Select(p => p.Id).ToList();
        state.SpeakerIndex = 1;
        return state;
    }

    [Fact]
    public void OnlyTheSpeakerTalks_InRadio_EveryoneButGhostInFreeTalk()
    {
        var state = Game();
        var ghost = state.Players[0].Id;
        var speaker = state.SpeakingOrder[1];
        var other = state.SpeakingOrder[0];
        VoiceRooms.MaySpeak(state, speaker).Should().BeTrue();
        VoiceRooms.MaySpeak(state, other).Should().BeFalse("рация не у него");
        state.FloorGrantedTo = other;
        VoiceRooms.MaySpeak(state, other).Should().BeTrue("ему дали слово");
        VoiceRooms.MaySpeak(state, ghost).Should().BeFalse();

        state.Settings = new GameSettings { Discussion = DiscussionMode.FreeChat };
        state.FloorGrantedTo = null;
        VoiceRooms.MaySpeak(state, other).Should().BeTrue("свободное обсуждение");
        VoiceRooms.MaySpeak(state, ghost).Should().BeFalse("Призрак молчит до итогов");

        state.Phase = Phase.Mailbox;
        VoiceRooms.MaySpeak(state, other).Should().BeFalse("письма пишут молча");
        state.Phase = Phase.VoteTie;
        VoiceRooms.MaySpeak(state, other).Should().BeTrue("ничья — обсуждают");
        state.Phase = Phase.Finished;
        VoiceRooms.MaySpeak(state, ghost).Should().BeTrue("после итогов говорят все");
        VoiceRooms.MaySpeak(state, Guid.NewGuid()).Should().BeFalse("чужой");
    }

    [Fact]
    public async Task Token_CarriesRights_AndSyncUpdatesOnlyChangedPlayers()
    {
        var handler = new FakeLiveKit();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LiveKit:ApiKey"] = "key", ["LiveKit:ApiSecret"] = new string('s', 40), ["LiveKit:PublicUrl"] = "wss://livekit.example",
        }).Build();
        var rooms = new VoiceRooms(config, NullLogger<VoiceRooms>.Instance, TimeProvider.System, handler);
        var state = Game();
        var speaker = state.SpeakingOrder[1];

        var access = rooms.Access(state, speaker, "Аня");
        access.CanSpeak.Should().BeTrue();
        access.Url.Should().Be("wss://livekit.example");
        var payload = JsonDocument.Parse(Base64Url(access.Token.Split('.')[1])).RootElement;
        payload.GetProperty("iss").GetString().Should().Be("key");
        payload.GetProperty("sub").GetString().Should().Be(speaker.ToString());
        payload.GetProperty("video").GetProperty("room").GetString().Should().Be(VoiceRooms.RoomOf(state.Id));
        payload.GetProperty("video").GetProperty("canPublish").GetBoolean().Should().BeTrue();

        await rooms.SyncAsync(state, CancellationToken.None);
        handler.Updates.Should().HaveCount(state.Players.Count, "первый раз — всем");
        handler.Updates.Clear();
        await rooms.SyncAsync(state, CancellationToken.None);
        handler.Updates.Should().BeEmpty("ничего не поменялось");

        state.SpeakerIndex = 2;
        await rooms.SyncAsync(state, CancellationToken.None);
        handler.Updates.Should().HaveCount(2, "слово перешло: старый говорящий замолкает, новый говорит");
        handler.Updates.Should().Contain(u => u.Contains(speaker.ToString()) && u.Contains("\"canPublish\":false"));
        handler.Updates.Should().Contain(u => u.Contains(state.SpeakingOrder[2].ToString()) && u.Contains("\"canPublish\":true"));
    }

    [Fact]
    public void SpokenCards_FindsNumbersNamesNegationsAndOwnLetter()
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>> { ["cage"] = [], ["ear"] = [], ["cat"] = [], ["bell"] = [], ["rose"] = [] })
            .WithNames("""{"cage": ["клетка", "птичья клетка"], "ear": ["ухо"], "cat": ["кошка"], "bell": ["колокол"], "rose": ["роза"]}""");
        List<BoardRow> board =
        [
            new() { Category = Category.Motive, Cards = ["rose", "cage"] },
            new() { Category = Category.Place, Cards = ["ear", "bell"] },
        ];
        List<HintGroup> hints = [];

        var (cards, notes) = SpokenCards.Read("Я кидала кошку. Думаю, мотив два, а точно не ухо. И место второе?", board, hints, ["cat"], tags);
        cards.Should().Equal("cat", "cage", "ear");
        notes.Should().Equal("кидал эту", "думаю, эта", "исключаю");

        SpokenCards.Read("Ставлю на клетку и розу", board, hints, [], tags).Cards.Should().Equal("cage", "rose");
        SpokenCards.Read("Привет всем", board, hints, [], tags).Cards.Should().BeEmpty();
    }

    [Fact]
    public void Wav_HeaderIsStripped()
    {
        var pcm = new byte[] { 1, 2, 3, 4 };
        using var wav = new MemoryStream();
        var w = new BinaryWriter(wav);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(-1); w.Write(pcm);
        Wav.Pcm(wav.ToArray(), out var rate).Should().Equal(pcm);
        rate.Should().Be(16000);
        Wav.Pcm(pcm, out _).Should().BeNull();
    }

    private static string Base64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')));
    }

    private sealed class FakeLiveKit : HttpMessageHandler
    {
        public List<string> Updates { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.RequestUri!.AbsolutePath.Should().EndWith("/twirp/livekit.RoomService/UpdateParticipant");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            Updates.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
