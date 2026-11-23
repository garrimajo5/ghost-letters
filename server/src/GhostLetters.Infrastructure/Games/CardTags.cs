using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Что нарисовано на картах: несколько тегов на карту (предмет, цвет, материал, тема) из tags.json рядом с cards.json.
/// Ботам этого хватает, чтобы «видеть» сходство писем и улик, не подглядывая в чужие роли.
/// </summary>
public sealed class CardTags
{
    public sealed record Detail(string Tag, double Weight, string Label);
    private IReadOnlyDictionary<string, List<Detail>> _details = new Dictionary<string, List<Detail>>();
    private IReadOnlyDictionary<string, IReadOnlyList<Detail>> _meanings = new Dictionary<string, IReadOnlyList<Detail>>();
    private IReadOnlyDictionary<string, Dictionary<string, double>> _meaningWeights = new Dictionary<string, Dictionary<string, double>>();
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _names = new Dictionary<string, IReadOnlyList<string>>();
    public static readonly CardTags Empty = new(new Dictionary<string, HashSet<string>>());

    private readonly IReadOnlyDictionary<string, HashSet<string>> _tags;

    private readonly Dictionary<string, double> _specificity;

    public CardTags(IReadOnlyDictionary<string, HashSet<string>> tags)
    {
        _tags = Trusted(tags);
        // Smoothed inverse document frequency. A common property carries less evidence
        // than a shared subject or purpose; rare annotations retain their original weight.
        _specificity = _tags.Values.SelectMany(ts => ts).Where(t => Group(t) == 0)
            .GroupBy(t => t).ToDictionary(g => g.Key,
                g => Math.Min(1, Math.Log((tags.Count + 32.0) / (g.Count() + 1)) / Math.Log(8)));
    }

    /// <summary>
    /// Форма из нарезки угадана по контуру картинки и часто мимо: «острые выступы» у кнута и шампуня, «по диагонали»
    /// у всего, что лежит наискось. Человек так не связывает карты. Ориентацию и «компактность» не считаем формой вовсе,
    /// «острое» — только у того, что и по смыслу острое.
    /// </summary>
    private static IReadOnlyDictionary<string, HashSet<string>> Trusted(IReadOnlyDictionary<string, HashSet<string>> tags)
    {
        if (!tags.Values.Any(t => t.Any(Untrusted))) return tags;
        return tags.ToDictionary(e => e.Key, e => e.Value.Any(Untrusted)
            ? e.Value.Where(t => !Untrusted(t) || (t == "shape-spiky" && e.Value.Overlaps(SharpThings))).ToHashSet()
            : e.Value);
    }

    private static bool Untrusted(string tag) => tag is "shape-diagonal" or "shape-compact" or "shape-spiky";

    private static readonly HashSet<string> SharpThings = ["sharp", "cactus", "thorn", "spike", "star", "starfish"];

    public int Count => _tags.Count;

    public IReadOnlyCollection<string> Of(string cardId) => _tags.TryGetValue(cardId, out var t) ? t : [];

    /// <summary>Как карту называют вслух (names.json, правится в картотеке): первое — основное название, дальше синонимы.</summary>
    public IReadOnlyList<string> NamesOf(string cardId) => _names.GetValueOrDefault(cardId) ?? [];

    /// <summary>Карты, у которых есть названия для речи.</summary>
    public IReadOnlyList<string> NamedCards => _names.Keys.ToList();

    /// <summary>
    /// Поиск карт по разметке (кабинет карточек): подстрока в ключе метки, подписи смысла или детали,
    /// словарной подписи признака (FeatureLabels) или названии карты. Без учёта регистра.
    /// </summary>
    public IReadOnlyList<string> SearchByTag(string term)
    {
        var t = term.Trim().ToLowerInvariant();
        if (t.Length == 0) return [];
        bool Has(string? s) => s?.Contains(t, StringComparison.OrdinalIgnoreCase) == true;
        bool TagHit(string tag) => tag.Contains(t, StringComparison.Ordinal) || Has(FeatureLabel(tag));
        return _tags.Keys.Where(card =>
                Of(card).Any(TagHit)
                || (_meanings.GetValueOrDefault(card) ?? []).Any(m => TagHit(m.Tag) || Has(m.Label))
                || (_details.GetValueOrDefault(card) ?? []).Any(d => TagHit(d.Tag) || Has(d.Label))
                || NamesOf(card).Any(Has))
            .Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Основное название карты для речи; нет — null.</summary>
    public string? NameOf(string cardId) => NamesOf(cardId).FirstOrDefault();

    /// <summary>Названия карт (names.json): {"orig_0001": ["клетка", "птичья клетка"]}.</summary>
    public CardTags WithNames(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? [];
        var names = _names.ToDictionary(e => e.Key, e => e.Value);
        foreach (var (card, list) in raw)
        {
            var clean = CleanNames(list);
            if (clean.Count > 0) names[card] = clean;
        }
        return new CardTags(_tags) { _details = _details, _meanings = _meanings, _meaningWeights = _meaningWeights, _names = names };
    }

    public static IReadOnlyList<string> CleanNames(IEnumerable<string>? list) =>
        (list ?? []).Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public CardAnnotation AnnotationOf(string card) => new(Of(card).Where(t => Group(t) != 0).Order().ToList(),
        _meanings.GetValueOrDefault(card) ?? Of(card).Where(t => Group(t) == 0).Order().Select(t => new Detail(t, 1, t)).ToList(),
        _details.GetValueOrDefault(card) ?? [], NamesOf(card));

    public CardTags WithAnnotations(IReadOnlyDictionary<string, CardAnnotation> annotations)
    {
        var tags = _tags.ToDictionary(e => e.Key, e => e.Value);
        var details = _details.ToDictionary(e => e.Key, e => e.Value);
        var meanings = _meanings.ToDictionary(e => e.Key, e => e.Value);
        var weights = _meaningWeights.ToDictionary(e => e.Key, e => e.Value);
        foreach (var (key, value) in annotations)
        {
            tags[key] = value.Tags.Concat(value.Meanings.Select(m => m.Tag)).ToHashSet();
            details[key] = value.Details.ToList(); meanings[key] = value.Meanings;
            weights[key] = value.Meanings.ToDictionary(m => m.Tag, m => m.Weight);
        }
        var names = _names.ToDictionary(e => e.Key, e => e.Value);
        foreach (var (key, value) in annotations)
            if (value.Names is { Count: > 0 } own) names[key] = own;
        return new CardTags(tags) { _details = details, _meanings = meanings, _meaningWeights = weights, _names = names };
    }

    private double Salience(string card, string tag, double secondary)
    {
        var weight = _meaningWeights.GetValueOrDefault(card)?.GetValueOrDefault(tag, 1) ?? 1;
        return weight >= 1 ? 1 : Math.Clamp(weight * Math.Clamp(secondary, 0, 1) * BotTuning.Current.AssociationScale, 0, 1);
    }

    private IReadOnlyCollection<string> Visible(string card, double secondary) => Of(card).Where(t => Salience(card, t, secondary) > 0).ToList();

    public CardTags WithDetails(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<Detail>>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (raw.Values.SelectMany(v => v).Any(d => string.IsNullOrWhiteSpace(d.Tag) ||
            string.IsNullOrWhiteSpace(d.Label) || !double.IsFinite(d.Weight) || d.Weight <= 0 || d.Weight > 1))
        {
            throw new FormatException("Деталям нужны тег, подпись и вес от 0 до 1.");
        }

        // Some older main tags already describe tiny objects (e.g. the roses on
        // the cage). Once classified as a detail, do not also count them as the
        // main subject: Details=0 must really ignore them.
        var main = _tags.ToDictionary(e => e.Key, e => e.Value
            .Except((raw.GetValueOrDefault(e.Key) ?? []).Select(d => d.Tag)).ToHashSet());
        return new CardTags(main) { _details = raw, _names = _names };
    }

    /// <summary>
    /// Ассоциации по смыслу (associations.json): то, что человек вспоминает при взгляде на карту — тема, обстановка,
    /// жанр. Вес меньше 1: они «вторичные смыслы», насколько бот их видит, решает его характер (SecondaryMeanings).
    /// </summary>
    public CardTags WithAssociations(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<Detail>>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var tags = _tags.ToDictionary(e => e.Key, e => e.Value.ToHashSet());
        var meanings = _meanings.ToDictionary(e => e.Key, e => e.Value);
        var weights = _meaningWeights.ToDictionary(e => e.Key, e => new Dictionary<string, double>(e.Value));
        foreach (var (card, list) in raw)
        {
            if (!tags.TryGetValue(card, out var own)) continue;
            var added = list.Where(a => !string.IsNullOrWhiteSpace(a.Tag) && double.IsFinite(a.Weight) && a.Weight > 0)
                .Select(a => a with { Tag = a.Tag.Trim().ToLowerInvariant(), Weight = Math.Min(a.Weight, .95) })
                .Where(a => !own.Contains(a.Tag) && Group(a.Tag) == 0).GroupBy(a => a.Tag).Select(g => g.MaxBy(a => a.Weight)!).ToList();
            if (added.Count == 0) continue;
            var main = meanings.GetValueOrDefault(card) ?? own.Where(t => Group(t) == 0).Order().Select(t => new Detail(t, 1, t)).ToList();
            meanings[card] = [.. main, .. added];
            var w = weights.GetValueOrDefault(card) ?? new Dictionary<string, double>();
            foreach (var a in added) w[a.Tag] = a.Weight;
            weights[card] = w;
            foreach (var a in added) own.Add(a.Tag);
        }

        return new CardTags(tags) { _details = _details, _meanings = meanings, _meaningWeights = weights, _names = _names };
    }

    private Dictionary<string, double> DetailsOf(string card) =>
        (_details.GetValueOrDefault(card) ?? []).Where(d => Group(d.Tag) == 0 && !IsMaterial(d.Tag)).GroupBy(d => d.Tag)
        .ToDictionary(g => g.Key, g => g.Max(d => d.Weight));

    public double Similarity(string a, string b, (double Meaning, double Shape, double Color) attention, double details, double secondaryMeanings = 0.35)
    {
        if (a == b) return 1;
        return Read(a, b, attention, details, secondaryMeanings).Score;
    }

    private double DetailMatch(string a, string b, double secondaryMeanings)
    {
        var x = DetailsOf(a);
        var y = DetailsOf(b);
        if (x.Count == 0 && y.Count == 0) return 0;
        // Мелкая роза на клетке может связываться и с главным предметом «роза» на другой карте.
        foreach (var t in x.Keys.Union(y.Keys).ToList())
        {
            if (Visible(a, secondaryMeanings).Contains(t)) x[t] = Math.Max(x.GetValueOrDefault(t), y.GetValueOrDefault(t));
            if (Visible(b, secondaryMeanings).Contains(t)) y[t] = Math.Max(y.GetValueOrDefault(t), x.GetValueOrDefault(t));
        }
        // Неразмеченная карта без известных связей не штрафуется за отсутствие разметки.
        if (x.Count == 0 || y.Count == 0) return 0;
        var union = x.Keys.Union(y.Keys).Sum(t => Math.Max(x.GetValueOrDefault(t), y.GetValueOrDefault(t)));
        var shared = x.Keys.Intersect(y.Keys).Sum(t => Math.Min(x[t], y[t]));
        // Слабая деталь — слабая связь: нормировка shared/union стирает масштаб весов, поэтому возвращаем его
        // через самую заметную общую деталь. Иначе едва видная розочка (вес .05) связывает карты как главный предмет.
        var strongest = x.Keys.Intersect(y.Keys).Select(t => Math.Min(x[t], y[t])).DefaultIfEmpty(0).Max();
        return shared / union * strongest;
    }

    public enum LinkKind { None, Surface, Shape, Detail, Meaning }

    private readonly record struct Match(double Meaning, double Details, double Shape, double Color, double Material, bool HasStructure)
    {
        public double Primary => Meaning + (1 - Meaning) * Details + Shape;
        public double Score => HasStructure ? Primary : Math.Max(Color, Material);
        public LinkKind Kind => Meaning > 0 ? LinkKind.Meaning : Details > 0 ? LinkKind.Detail :
            Shape > 0 ? LinkKind.Shape : Score > 0 ? LinkKind.Surface : LinkKind.None;
    }

    private Match Read(string a, string b, (double Meaning, double Shape, double Color) attention, double details, double secondary)
    {
        var w = Contributions(a, b, attention, secondary);
        var detail = DetailMatch(a, b, secondary);
        return new(w.Meaning, .20 * Math.Clamp(details, 0, 1) * detail, w.Shape, w.Color, w.Material,
            w.Meaning > 0 || detail > 0 || w.Shape > 0 || attention.Shape <= 0 && Of(a).Any(t => Group(t) == 1 && Of(b).Contains(t)));
    }

    /// <summary>A letter can use surface features only if the whole field offers no reading by subject, detail or shape.</summary>
    public sealed class BoardReading(CardTags tags, IReadOnlyList<Domain.Game.BoardRowView> rows,
        (double Meaning, double Shape, double Color) attention, double details, double secondary)
    {
        private readonly HashSet<string> _board = rows.SelectMany(r => r.Cards).ToHashSet();
        private readonly Dictionary<string, Dictionary<string, Match>> _letters = new();

        private Match For(string letter, string target)
        {
            if (!_board.Contains(target)) return tags.Read(letter, target, attention, details, secondary);
            if (!_letters.TryGetValue(letter, out var matches))
            {
                matches = _board.Where(c => c != letter).ToDictionary(c => c, c => tags.Read(letter, c, attention, details, secondary));
                if (matches.Values.Any(m => m.HasStructure))
                    foreach (var card in matches.Keys.ToArray())
                        matches[card] = matches[card] with { Color = 0, Material = 0 };
                _letters[letter] = matches;
            }
            return matches.GetValueOrDefault(target);
        }

        public double Similarity(string letter, string target) => letter == target ? 1 : For(letter, target).Score;
        public LinkKind Kind(string letter, string target) => For(letter, target).Kind;

        public string Explain(string letter, string target) => Similarity(letter, target) <= 0 ? "явной связи не вижу"
            : tags.Explain(letter, target, attention, details, secondary);

        public string? ReasonChip(string letter, string target) => Similarity(letter, target) <= 0 ? null
            : tags.ReasonChip(letter, target, attention, secondary, details);
    }

    public BoardReading AtBoard(IReadOnlyList<Domain.Game.BoardRowView> rows, Bots.BotPersonality? personality = null) =>
        personality is { } p ? new(this, rows, p.Attention, p.Details, p.SecondaryMeanings)
            : new(this, rows, (.8, .15, .05), 0, .35);

    /// <summary>Причины из тех же признаков, по которым бот действительно сравнил карты.</summary>
    public string Explain(string a, string b, (double Meaning, double Shape, double Color) attention, double details, double secondaryMeanings = 0.35)
    {
        var va = Visible(a, secondaryMeanings);
        var vb = Visible(b, secondaryMeanings);
        var da = (_details.GetValueOrDefault(a) ?? []).Where(d => Group(d.Tag) == 0 && !IsMaterial(d.Tag)).ToList();
        var db = (_details.GetValueOrDefault(b) ?? []).Where(d => Group(d.Tag) == 0 && !IsMaterial(d.Tag)).ToList();
        var weights = Read(a, b, attention, details, secondaryMeanings);
        var reasons = new List<(double Score, string Text)>();
        var common = va.Intersect(vb).ToList();
        foreach (var (group, weight) in new[] { (0, weights.Meaning), (1, weights.Shape), (2, weights.Color) })
        {
            if (weight > 0 && (group != 2 || !weights.HasStructure && weights.Color >= weights.Material))
            {
                var selected = common.Where(t => Group(t) == group && !IsMaterial(t) && (group != 2 || t.StartsWith("dominant-")))
                    .OrderByDescending(t => SharedSubject(a, b, t))
                    .ThenByDescending(t => FeatureLabel(t) is not null ||
                        _meanings.GetValueOrDefault(a)?.Any(m => m.Tag == t && m.Label != t) == true ||
                        _meanings.GetValueOrDefault(b)?.Any(m => m.Tag == t && m.Label != t) == true)
                    .ThenByDescending(t => _specificity.GetValueOrDefault(t, 1) * Math.Min(Salience(a, t, secondaryMeanings), Salience(b, t, secondaryMeanings)))
                    .ThenBy(t => t, StringComparer.Ordinal).First();
                var meaning = _meanings.GetValueOrDefault(a)?.FirstOrDefault(m => m.Tag == selected)
                    ?? _meanings.GetValueOrDefault(b)?.FirstOrDefault(m => m.Tag == selected);
                var feature = FeatureLabel(selected);
                var text = group == 0 && meaning is not null && meaning.Label != selected
                    ? "по смыслу: " + meaning.Label
                    : feature is not null ? (group == 0 ? "по смыслу: " : group == 1 ? "по форме: " : "по цвету: ") + feature
                    : null;
                // Unknown technical tags are not a concrete explanation.
                // Prefer another shared feature with a known, human-readable name.
                if (text is null) continue;
                reasons.Add((weight * Math.Min(Salience(a, selected, secondaryMeanings), Salience(b, selected, secondaryMeanings)), text));
            }
        }
        // Материал — отдельная слабая причина: «оба металлические», а не «по смыслу».
        var sharedMaterial = AccentMaterials(a).Intersect(AccentMaterials(b)).Order().FirstOrDefault();
        if (sharedMaterial is not null && !weights.HasStructure && weights.Material >= weights.Color && weights.Material > 0)
            reasons.Add((weights.Material, "по материалу: " + (FeatureLabel(sharedMaterial) ?? sharedMaterial)));
        var other = DetailsOf(b);
        foreach (var d in da)
            if (details > 0 && (other.ContainsKey(d.Tag) || vb.Contains(d.Tag)))
                reasons.Add((.20 * details * Math.Min(other.GetValueOrDefault(d.Tag, d.Weight), d.Weight), "по деталям: " + d.Label));
        foreach (var d in db)
            if (details > 0 && va.Contains(d.Tag))
                reasons.Add((.20 * details * d.Weight, "по деталям: " + d.Label));
        // Форма и материал — слабые подсказки: человек вспомнит их, только если общего смысла, цвета или детали не нашлось.
        if (reasons.Any(r => !r.Text.StartsWith("по форме", StringComparison.Ordinal) && !r.Text.StartsWith("по материалу", StringComparison.Ordinal)))
            reasons.RemoveAll(r => r.Text.StartsWith("по форме", StringComparison.Ordinal) || r.Text.StartsWith("по материалу", StringComparison.Ordinal));
        return reasons.Count == 0 ? "явной связи не вижу" :
            string.Join("; ", reasons.OrderByDescending(r => r.Text.StartsWith("по смыслу", StringComparison.Ordinal)).ThenByDescending(r => r.Score).Take(2).Select(r => r.Text));
    }

    private static string? FeatureLabel(string tag)
    {
        var key = tag.StartsWith("dominant-", StringComparison.Ordinal) ? tag[9..] :
            tag.StartsWith("accent-", StringComparison.Ordinal) ? tag[7..] : tag;
        return FeatureLabels.GetValueOrDefault(key);
    }

    private static readonly IReadOnlyDictionary<string, string> FeatureLabels = new Dictionary<string, string>
    {
        ["music"] = "музыка и музыкальные инструменты", ["sound"] = "звук",
        ["red"] = "красный акцент", ["blue"] = "синий акцент", ["green"] = "зелёный акцент",
        ["yellow"] = "жёлтый акцент", ["black"] = "чёрный акцент", ["white"] = "белый акцент",
        ["brown"] = "коричневый акцент", ["gold"] = "золото", ["silver"] = "серебро",
        ["orange"] = "оранжевый акцент", ["pink"] = "розовый акцент", ["purple"] = "фиолетовый акцент",
        ["gray"] = "серый акцент", ["grey"] = "серый акцент",
        ["water"] = "вода", ["sea"] = "море", ["fire"] = "огонь", ["weapon"] = "оружие",
        ["animal"] = "животные", ["bird"] = "птицы", ["flower"] = "цветы", ["plant"] = "растения",
        ["subject-plant"] = "растение", ["subject-paper-sheet"] = "лист бумаги",
        ["death"] = "смерть", ["danger"] = "опасность", ["love"] = "любовь", ["heart"] = "сердце",
        ["travel"] = "путешествия", ["transport"] = "транспорт", ["food"] = "еда", ["drink"] = "пить",
        ["writing"] = "письмо и письменные принадлежности", ["book"] = "книги", ["time"] = "время",
        ["clock"] = "часы", ["key"] = "ключи", ["lock"] = "замки и запирание", ["money"] = "деньги",
        ["magic"] = "магия", ["light"] = "свет", ["dark"] = "темнота", ["house"] = "дом",
        ["building"] = "здания", ["royal"] = "королевская власть", ["crown"] = "корона",
        ["metal"] = "металл", ["wood"] = "дерево как материал", ["glass"] = "стекло",
        ["stone"] = "камень", ["paper"] = "бумага", ["fabric"] = "ткань",
        ["shape-round"] = "круглая форма", ["shape-long"] = "вытянутая форма",
        ["shape-square"] = "квадратная форма", ["shape-triangle"] = "треугольная форма",
        ["shape-rectangle"] = "прямоугольная форма", ["shape-curved"] = "изогнутая форма",
        ["shape-spiky"] = "острые выступы",
        ["shape-tall"] = "высокий вытянутый силуэт", ["shape-wide"] = "широкий силуэт",
        ["tool"] = "инструменты", ["old"] = "старинные предметы", ["nature"] = "природа",
        ["clothes"] = "одежда", ["clothing"] = "одежда", ["toy"] = "игрушки",
        ["insect"] = "насекомые", ["war"] = "война", ["jewelry"] = "украшения",
        ["container"] = "ёмкости для хранения", ["drinkware"] = "посуда для напитков",
        ["science"] = "наука", ["sport"] = "спорт", ["religion"] = "религия",
        ["game"] = "игры", ["medical"] = "медицина", ["medicine"] = "медицина",
        ["tech"] = "техника", ["home"] = "домашний быт", ["art"] = "искусство",
        ["east"] = "восточная культура", ["kitchen"] = "кухня", ["poison"] = "яд",
        ["occult"] = "оккультизм", ["crime"] = "преступление", ["gothic"] = "готика",
        ["cold"] = "холод", ["retro"] = "ретро", ["car"] = "автомобили",
        ["statue"] = "скульптуры", ["leather"] = "кожа как материал",
        ["round"] = "круглые предметы", ["sharp"] = "острые предметы", ["colorful"] = "многоцветность",

        // Reviewed actions, physical properties and exact shapes (vocabulary.md).
        ["open-top"] = "открытая сверху ёмкость",
        ["aim"] = "целиться",
        ["apply"] = "наносить на тело",
        ["attach"] = "прикреплять",
        ["balance"] = "равновесие",
        ["bathroom"] = "ванная комната",
        ["blow"] = "дуть",
        ["build"] = "строить",
        ["burn"] = "гореть или сжигать",
        ["carry"] = "переносить вещи",
        ["carve"] = "вырезать фигуру",
        ["catch"] = "ловить",
        ["ceramic"] = "керамика",
        ["clean"] = "чистить",
        ["climb"] = "лезть, карабкаться",
        ["cold-blooded"] = "холоднокровное животное",
        ["cook"] = "готовить пищу",
        ["cool"] = "охлаждать",
        ["count"] = "считать количество",
        ["crawl"] = "ползать",
        ["cut"] = "резать",
        ["decorate"] = "украшать",
        ["dig"] = "копать",
        ["display"] = "выставлять для обозрения",
        ["draw"] = "рисовать",
        ["drill"] = "сверлить",
        ["dry"] = "сухой",
        ["eat"] = "есть",
        ["edible"] = "съедобный",
        ["elastic"] = "упругий, растягивающийся",
        ["enter"] = "входить внутрь",
        ["explore"] = "исследовать местность",
        ["finger-ring"] = "кольцо на палец",
        ["flexible"] = "гибкий",
        ["float"] = "держаться на воде или в воздухе",
        ["fly"] = "муха",
        ["follow"] = "идти по следам",
        ["fragile"] = "хрупкий",
        ["fur"] = "мех",
        ["gas"] = "газ",
        ["granular"] = "сыпучий",
        ["grind"] = "измельчать",
        ["grow"] = "расти или выращивать",
        ["hard"] = "твёрдый",
        ["heal"] = "лечить",
        ["hear"] = "слушать",
        ["heat"] = "жара",
        ["heavy"] = "тяжёлый",
        ["hit"] = "бить",
        ["hold-in-hand"] = "держать в руке",
        ["hollow"] = "полый",
        ["hot"] = "горячий или жгучий",
        ["ignite"] = "поджигать",
        ["illuminate"] = "освещать",
        ["infect"] = "заражать",
        ["inflatable"] = "надувной",
        ["inhale"] = "вдыхать",
        ["jump"] = "прыгать",
        ["kiss"] = "целовать",
        ["lab"] = "лаборатория",
        ["lie-on"] = "лежать на предмете или в нём",
        ["lift"] = "поднимать",
        ["light-weight"] = "лёгкий по весу",
        ["liquid"] = "жидкость",
        ["look-at"] = "рассматривать изображение или объект",
        ["look-through"] = "смотреть сквозь",
        ["make"] = "изготавливать",
        ["measure"] = "измерение",
        ["melt"] = "таять",
        ["microscopic"] = "микроскопический",
        ["move"] = "двигаться",
        ["multiply"] = "размножаться",
        ["open-close"] = "открывать и закрывать",
        ["orbit"] = "двигаться по орбите",
        ["pay"] = "платить",
        ["place-on"] = "ставить на поверхность",
        ["plastic"] = "пластик",
        ["play"] = "играть",
        ["pour"] = "наливать или насыпать",
        ["powder"] = "порошок, мелкие частицы",
        ["print"] = "печатать",
        ["protect"] = "защищать",
        ["pull"] = "тянуть",
        ["push"] = "толкать или нажимать",
        ["read"] = "читать",
        ["record"] = "записывать звук или изображение",
        ["reflect"] = "отражать свет",
        ["rescue"] = "спасать",
        ["rest"] = "отдыхать",
        ["ride"] = "ехать или кататься",
        ["ring"] = "звенеть",
        ["rock"] = "раскачивать",
        ["roll"] = "катить",
        ["rotate"] = "вращать",
        ["rubber"] = "резина",
        ["run"] = "бежать",
        ["scratch"] = "царапать",
        ["sew"] = "шить",
        ["shape-animal"] = "силуэт животного",
        ["shape-ball"] = "шар или близкий к шару объём",
        ["shape-box"] = "коробка, прямоугольный объём",
        ["shape-chain"] = "цепочка соединённых элементов",
        ["shape-cone"] = "конус, сужающийся объём",
        ["shape-container"] = "ёмкость с внутренним пространством",
        ["shape-cross"] = "крестообразная форма",
        ["shape-cylinder"] = "цилиндр",
        ["shape-flat"] = "плоская пластина",
        ["shape-handle-plus-head"] = "рукоять и расширенная рабочая часть",
        ["shape-heart"] = "форма сердца",
        ["shape-humanoid"] = "силуэт человека или части тела",
        ["shape-net"] = "сетчатая структура",
        ["shape-pair"] = "пара предметов или симметричных частей",
        ["shape-ring"] = "кольцо, обруч или разомкнутая дуга",
        ["shape-spiral"] = "спираль, свёрнутый длинный предмет",
        ["shape-star"] = "звезда с расходящимися лучами",
        ["shape-stick"] = "палочка, стержень",
        ["shoot"] = "стрелять",
        ["sing"] = "петь",
        ["sit-on"] = "сидеть на предмете",
        ["slide"] = "скользить",
        ["slippery"] = "скользкий",
        ["smell"] = "запах",
        ["soft"] = "мягкий",
        ["speak"] = "говорить",
        ["sportsman"] = "спортсмен",
        ["spray"] = "распылять",
        ["stab"] = "колоть",
        ["sticky"] = "липкий",
        ["store"] = "хранить внутри",
        ["swim"] = "плавать активно",
        ["swing"] = "махать, делать замах",
        ["think"] = "думать",
        ["throw"] = "бросать",
        ["tie"] = "галстук",
        ["touch"] = "прикосновение",
        ["toxic"] = "токсичный",
        ["transparent"] = "прозрачный",
        ["use"] = "пользоваться устройством",
        ["vinyl-record"] = "грампластинка",
        ["walk"] = "ходить",
        ["wash"] = "мыть",
        ["wear"] = "носить на теле",
        ["wet"] = "мокрый, содержащий воду",
        ["wool"] = "шерсть как материал",
        ["write"] = "писать",
    };

    /// <summary>Цвета и форма отделены от смысла; цвет связывает карты только через dominant-*.</summary>
    private static readonly HashSet<string> Colors =
        ["red", "orange", "yellow", "green", "blue", "purple", "pink", "brown", "black", "white", "gray"];

    /// <summary>
    /// Материал предмета (категория «материал» в vocabulary.md) — слабый признак: размечается по картинке и может
    /// быть ошибочным. В сходстве считается отдельно от смысла и с малым весом (<see cref="BotTuning.MaterialWeight"/>):
    /// «оба металлические» не связывает карты так, как общий предмет или тема.
    /// </summary>
    private static readonly HashSet<string> Materials =
        ["bone", "ceramic", "clay", "fabric", "fur", "glass", "leather", "metal", "paper", "plastic", "rubber", "stone", "straw", "wax", "wood", "wool", "gold", "silver", "steel"];

    private const string MaterialAccent = "material-accent-";

    public static bool IsMaterial(string tag) => Materials.Contains(tag) ||
        tag.StartsWith(MaterialAccent, StringComparison.Ordinal) && Materials.Contains(tag[MaterialAccent.Length..]);

    // Material named by the depicted subject, or explicitly marked as the focal material.
    private IEnumerable<string> AccentMaterials(string card)
    {
        foreach (var tag in Of(card))
        {
            if (Salience(card, tag, 0) == 0) continue;
            if (tag.StartsWith(MaterialAccent, StringComparison.Ordinal) && IsMaterial(tag))
                yield return tag[MaterialAccent.Length..];
            else if (tag is "plank" or "brushwood" or "wooden-crutch") yield return "wood";
            else if (tag is "gold-leaf-booklet" or "gold-bar" or "gold-nugget") yield return "gold";
            else if (tag == "silver-bar") yield return "silver";
        }
    }

    public static double Weight(string tag) => tag.StartsWith("shape-", StringComparison.Ordinal) || Colors.Contains(tag) ? 0.5 : 1;

    /// <summary>
    /// Похожесть двух карт 0…1: смысл доминирует, визуальное сходство служит слабой запасной подсказкой.
    /// Одна и та же карта — 1.
    /// </summary>
    public double Similarity(string a, string b) => Similarity(a, b, (.8, .15, .05), 0, .35);

    public double Similarity(string a, string b, (double Meaning, double Shape, double Color) attention)
        => Similarity(a, b, attention, 0, .35);

    /// <summary>Короткая причина нити для доски улик: что сильнее всего связало карты глазами бота.</summary>
    public string? ReasonChip(string a, string b, (double Meaning, double Shape, double Color) attention, double secondary = .35, double details = 0)
    {
        var w = Read(a, b, attention, details, secondary);
        if (w.Score <= .01) return null;
        if (w.Meaning > 0) return "предмет";
        if (w.Details > 0) return "деталь";
        if (w.Shape > 0) return "форма";
        return w.Color >= w.Material ? "цвет" : "материал";
    }

    // Both illustrations feature this subject. A small detail or a secondary
    // association in either annotation does not establish a direct subject link.
    private bool SharedSubject(string a, string b, string tag) => tag.StartsWith("subject-", StringComparison.Ordinal)
        && Salience(a, tag, 0) == 1 && Salience(b, tag, 0) == 1;

    private (double Meaning, double Shape, double Color, double Material) Contributions(string a, string b,
        (double Meaning, double Shape, double Color) attention, double secondary)
    {
        double Overlap(IReadOnlyCollection<string> x, IReadOnlyCollection<string> y)
        {
            var union = x.Union(y).Sum(t => Math.Max(x.Contains(t) ? Salience(a, t, secondary) : 0, y.Contains(t) ? Salience(b, t, secondary) : 0));
            var confidence = Math.Min(x.Select(t => Salience(a, t, secondary)).DefaultIfEmpty(0).Max(),
                y.Select(t => Salience(b, t, secondary)).DefaultIfEmpty(0).Max());
            return union <= 0 ? 0 : confidence * x.Intersect(y).Sum(t =>
                _specificity.GetValueOrDefault(t, 1) * Math.Min(Salience(a, t, secondary), Salience(b, t, secondary))) / union;
        }

        double Match(int group) => Overlap(
            Of(a).Where(t => Group(t) == group && !IsMaterial(t) && (group != 2 || t.StartsWith("dominant-"))).ToHashSet(),
            Of(b).Where(t => Group(t) == group && !IsMaterial(t) && (group != 2 || t.StartsWith("dominant-"))).ToHashSet());

        var meaning = Match(0);
        if (Of(a).Any(t => SharedSubject(a, b, t) && Of(b).Contains(t)))
            meaning = Math.Max(meaning, .70);
        var material = AccentMaterials(a).Intersect(AccentMaterials(b)).Any() ? 1 : 0;
        var sum = Math.Max(0, attention.Meaning) + Math.Max(0, attention.Shape) + Math.Max(0, attention.Color);
        var m = sum <= 0 ? .8 : Math.Max(.7, attention.Meaning / sum);
        // A strong semantic match does not need a visual tie-breaker. Unlabelled
        // background colours are never evidence; dominant-* is an explicit annotation.
        var fallback = meaning >= .25 ? .05 : 1 - meaning;
        return (m * meaning, Math.Min(.12, Math.Max(0, attention.Shape)) * fallback * Match(1),
            Math.Min(.08, Math.Max(0, attention.Color)) * fallback * Match(2),
            Math.Max(0, BotTuning.Current.MaterialWeight) * fallback * material);
    }

    /// <summary>0 — смысл, 1 — форма, 2 — цвет, 3 — явно отмеченный акцент материала.</summary>
    public static int Group(string tag)
    {
        if (tag.StartsWith("shape-", StringComparison.Ordinal)) return 1;
        if (tag.StartsWith(MaterialAccent, StringComparison.Ordinal) && IsMaterial(tag)) return 3;
        if (Colors.Contains(tag) || tag.StartsWith("accent-", StringComparison.Ordinal) && Colors.Contains(tag[7..]) ||
            tag.StartsWith("dominant-", StringComparison.Ordinal) && Colors.Contains(tag[9..])) return 2;
        return 0;
    }

    public static CardTags Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? [];
        return new CardTags(raw.ToDictionary(e => e.Key, e => e.Value.Select(t => t.Trim().ToLowerInvariant()).ToHashSet()));
    }

    /// <summary>Cards:TagsPath, а если не задан — tags.json рядом с Cards:ManifestPath. Нет файла — пустые теги (боты ходят наугад).</summary>
    public static CardTags FromConfiguration(IConfiguration configuration, ILogger logger)
    {
        var path = configuration["Cards:TagsPath"];
        var manifest = configuration["Cards:ManifestPath"];
        if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(manifest))
        {
            path = Path.Combine(Path.GetDirectoryName(manifest) ?? ".", "tags.json");
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            logger.LogInformation("Теги карт не найдены ({Path}) — боты будут ходить наугад", path);
            return Empty;
        }

        var tags = Parse(File.ReadAllText(path));
        var detailsPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", "details.json");
        if (File.Exists(detailsPath)) tags = tags.WithDetails(File.ReadAllText(detailsPath));
        var associationsPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", "associations.json");
        if (File.Exists(associationsPath)) tags = tags.WithAssociations(File.ReadAllText(associationsPath));
        var namesPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", "names.json");
        if (File.Exists(namesPath)) tags = tags.WithNames(File.ReadAllText(namesPath));
        logger.LogInformation("Теги карт: {Count} из {Path}", tags.Count, path);
        return tags;
    }
}
