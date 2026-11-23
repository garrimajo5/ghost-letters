using System.Text.Json;
using GhostLetters.Infrastructure.Games;

if (args.Length is < 1 or > 2 || !int.TryParse(args[0], out var seed) ||
    args.Length == 2 && !BotSandboxRunner.Scenarios.Contains(args[1]))
{
    Console.Error.WriteLine("dotnet run --project server/tools/BotTrace -- SEED [full-game|clear-hints|witness-reveal]");
    return 2;
}

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "client/assets/cards/cards.json"))) root = root.Parent;
if (root is null) throw new DirectoryNotFoundException("Не найден каталог игровых карт.");
var assets = Path.Combine(root.FullName, "client/assets/cards");
string Read(string name) => File.ReadAllText(Path.Combine(assets, name));
var tags = CardTags.Parse(Read("tags.json")).WithDetails(Read("details.json"))
    .WithAssociations(Read("associations.json")).WithNames(Read("names.json"));
using var manifest = JsonDocument.Parse(Read("cards.json"));
var deck = manifest.RootElement.GetProperty("sets").EnumerateArray()
    .SelectMany(s => s.GetProperty("cards").EnumerateArray()).Select(c => c.GetProperty("id").GetString()!)
    .Order(StringComparer.Ordinal).ToList();
var trace = BotSandboxRunner.Trace(args.ElementAtOrDefault(1) ?? "full-game", seed, deck, tags, CancellationToken.None);
Console.WriteLine(GameJson.Serialize(trace));
return trace.Status == "completed" ? 0 : 1;
