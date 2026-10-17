using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Speech;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Речь бота в обсуждении: что он говорит (реплика-шаблон) и то же самое мыслями (<see cref="SpeechIntent"/>).</summary>
public static partial class BotPlayer
{
    private sealed partial class Brain
    {
        /// <summary>Манера речи: осторожный сомневается, рисковый говорит уверенно.</summary>
        private string Tone(string line)
        {
            if (Classic)
            {
                return line;
            }

            if (P.Risk < 0.3 && rng.Next(2) == 0)
            {
                return "Не уверен, но " + char.ToLowerInvariant(line[0]) + line[1..];
            }

            // Вопрос («Кто против?») уверенностью не заканчивают.
            return P.Risk > 0.75 && rng.Next(2) == 0 && !line.TrimEnd().EndsWith('?') ? line.TrimEnd('.') + " — уверен." : line;
        }

        /// <summary>Обвинение последней <see cref="Accusation"/> — мыслью для речи (кого, насколько, почему).</summary>
        private Thought? _accused;

        private string Accused(string text, string who, double strength, string? why = null)
        {
            _accused = new Thought(ThoughtKind.Accuse, Who: BotNames.Bare(who), Why: why, Strength: strength);
            return text;
        }

        /// <summary>
        /// Обвинение в чате. Свидетель знает Убийцу: рисковый называет его прямо, средний намекает,
        /// осторожный молчит (иначе его вычислят на охоте). Детектив с сильной памятью
        /// вспоминает, кто часто бывал Убийцей.
        /// </summary>
        public string Accusation()
        {
            _accused = null;
            if (Classic)
            {
                return "";
            }

            if (me.Role == Role.Witness && view.Players.FirstOrDefault(p => p.KnownRole == Role.Killer && p.Id != me.Id) is { } killer
                && NameOf(killer.Id) is { } name)
            {
                // Стол и так побеждает по картам или уже подозревает Убийцу — Свидетель молчит, чтобы его не вычислили.
                if (BotTuning.Current.WitnessQuiet && (Accused(killer.Id) >= .5 || (view.Board.Count > 1 &&
                        Enumerable.Range(0, view.Board.Count).Count(r => Certainty(r) > .15) >= view.Board.Count - 1)))
                {
                    return "";
                }

                if (P.Risk > 0.7 && rng.NextDouble() < P.Risk)
                {
                    return Accused($" Мне кажется, Убийца — {name}.", name, 1);
                }

                if (P.Risk > 0.4 && rng.NextDouble() < P.Risk)
                {
                    return Accused($" Я бы присмотрелся к {name}.", name, .4);
                }

                return "";
            }

            if (me.Role == Role.Ghost)
            {
                return "";
            }

            var others = view.Players.Where(p => p.Id != me.Id && !p.IsGhost).ToList();
            if (KillerTeam)
            {
                // Чёрные переводят стрелки: рисковый обвиняет того, кто обвинял его команду (или случайного «белого»).
                if (rng.NextDouble() >= P.Risk * 0.7)
                {
                    return "";
                }

                var team = others.Where(p => p.KnownRole is { } r && r.IsKillerTeam()).Select(p => p.Id).Append(me.Id).ToHashSet();
                var target = others.Where(p => !team.Contains(p.Id))
                    .OrderByDescending(p => mind!.AccusationList.Where(a => a.Author == p.Id && team.Contains(a.Target)).Sum(a => a.Strength) + rng.NextDouble() * 0.3)
                    .FirstOrDefault();
                return target is not null && NameOf(target.Id) is { } framed ? Accused($" Подозреваю, что {framed} из чёрных.", framed, .6) : "";
            }

            // Белые: говорят о подозрениях, если они достаточно сильные; смелый — раньше, осторожный — только при уверенности.
            var top = others.Where(p => p.KnownRole is not { } r || r.IsKillerTeam())
                .Select(p => (p.Id, s: Suspicion(p.Id))).OrderByDescending(x => x.s).FirstOrDefault();
            if (top.Id == Guid.Empty || NameOf(top.Id) is not { } who || top.s < 1.3 - 0.9 * P.Risk)
            {
                return "";
            }

            if (top.s > 2)
            {
                return Accused($" Думаю, Убийца — {who}: {BehaviourReason(top.Id)}.", who, 1, BehaviourReason(top.Id));
            }

            return PastSuspicion(top.Id) > 0.25 && rng.Next(2) == 0
                ? Accused($" Подозреваю {who}: уже бывал Убийцей.", who, .6, "уже бывал Убийцей")
                : Accused($" Подозреваю, что {who} из чёрных: {BehaviourReason(top.Id)}.", who, .6, BehaviourReason(top.Id));
        }

        /// <summary>Что бот решил сказать последним <see cref="Say"/> — мыслями, для подбора живых фраз.</summary>
        public SpeechIntent? Intent { get; private set; }

        /// <summary>
        /// Настроение — из того, что с ботом происходит в партии: обвиняют ли его, открыли ли его письмо,
        /// сколько писем исчезло, насколько ясны ряды и насколько сильны его подозрения.
        /// </summary>
        public SpeechMood Mood()
        {
            if (Classic) return SpeechMood.Calm;
            if (Accused(me.Id) >= .5) return SpeechMood.Defensive;
            var letter = me.Letters.LastOrDefault();
            if (letter is { Revealed: true } && letter.Round >= view.Round - 1) return P.Risk > .5 ? SpeechMood.Excited : SpeechMood.Confident;
            if (letter is { Revealed: false } && me.Letters.Count(l => l.Revealed == false) >= 2) return SpeechMood.Frustrated;
            if (_accused is { Strength: >= .6 }) return SpeechMood.Suspicious;
            var clear = view.Board.Count == 0 ? 0 : Enumerable.Range(0, view.Board.Count).Max(Certainty);
            if (clear > .3) return P.Risk > .7 ? SpeechMood.Excited : SpeechMood.Confident;
            if (clear < .08 || view.Hints.Sum(h => h.Cards.Count) <= 1) return SpeechMood.Doubtful;
            return SpeechMood.Calm;
        }

        /// <summary>Положение за столом: раунд, сколько улик и сколько игроков вслух за мою карту в этом ряду и против.</summary>
        private TableSituation Situation(int row, int column)
        {
            int agreeing = 0, disagreeing = 0;
            if (mind is not null)
                foreach (var p in view.Players.Where(p => p.Id != me.Id && !p.IsGhost))
                {
                    var said = mind.LatestOf(p.Id).FirstOrDefault(o => o.Strength > 0 && view.Board[row].Cards.Contains(o.CardId));
                    if (said is null) continue;
                    if (said.CardId == Card(row, column)) agreeing++;
                    else disagreeing++;
                }
            return new TableSituation(view.Round, view.TotalRounds, view.Hints.Sum(h => h.Cards.Count), agreeing, disagreeing);
        }

        public (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes) Say()
        {
            static string Name(Category c) => c switch
            {
                Category.Motive => "Мотив",
                Category.Place => "Место",
                Category.Method => "Способ",
                _ => "Тайна",
            };

            var (claimText, claimCard) = LetterClaim();
            int target;
            int column;
            // Карту не предлагают, а отметают («исключаю»): Эксперт — ложного лидера ряда, Сообщник — истину.
            var exclude = false;
            if (Spoiler && Truth is { } spoilTruth && rng.NextDouble() < BotTuning.Current.SpoilerExclude &&
                Enumerable.Range(0, view.Board.Count).Where(r => !ClearTruth(r)).ToList() is { Count: > 0 } doubtful)
            {
                target = doubtful[rng.Next(doubtful.Count)];
                column = spoilTruth[target];
                exclude = true;
            }
            else if (SubtleExpert && rng.NextDouble() < .7 &&
                Enumerable.Range(0, view.Board.Count).Where(r => BestColumn(r, null) != Truth![r] && Evidence(Card(r, BestColumn(r, null))) > .05 && Certainty(r) < .15).ToList() is { Count: > 0 } misled)
            {
                target = misled.OrderBy(r => TruthMargin(r) + Noise()).First();
                column = BestColumn(target, null);
                exclude = true;
            }
            else if (KillerTeam && Truth is { } truth && (Classic || rng.NextDouble() < FakeChance))
            {
                // Ложь, повторённая слово в слово второй раунд подряд, выдаёт: улики меняются, а он твердит одно.
                var repeated = Repeated();
                bool Fresh(int r) => Enumerable.Range(0, view.Board[r].Cards.Count).Any(c => c != truth[r] && !repeated.Contains(Card(r, c)));
                var rows = Enumerable.Range(0, view.Board.Count).ToList();
                if (!Classic && BotTuning.Current.KillerBlend)
                {
                    // Спорим там, где ложная карта правдоподобна: в ясном ряду ложь видна и выдаёт.
                    var open = rows.Where(r => !ClearTruth(r)).ToList();
                    if (open.Count > 0) rows = open;
                }
                if (rows.Where(Fresh).ToList() is { Count: > 0 } fresh) rows = fresh;
                target = rows[rng.Next(rows.Count)];

                var fakes = Enumerable.Range(0, view.Board[target].Cards.Count).Where(c => c != truth[target]).ToList();
                column = fakes.OrderBy(c => repeated.Contains(Card(target, c)) ? 1 : 0)
                    .ThenByDescending(c => Evidence(Card(target, c)) + Noise()).First();
            }
            else
            {
                // Чёрный не твердит одну карту раунд за раундом — переходит к другому ряду.
                var said = KillerTeam ? Repeated() : [];
                target = Enumerable.Range(0, view.Board.Count)
                    .OrderBy(r => said.Contains(Card(r, BestColumn(r, null))) ? 1 : 0)
                    .ThenByDescending(r => Certainty(r) + Noise()).First();
                // Эксперт знает истину: осторожный редко её выдаёт (его ищут на охоте), рисковый — почти всегда.
                column = Truth is { } t && me.Role == Role.Expert && (Classic ? rng.Next(3) > 0 : rng.NextDouble() < 0.2 + 0.7 * P.Risk)
                    ? t[target]
                    : BestColumn(target, null);
            }

            var card = Card(target, column);
            string Where(int r, int c) => $"{Name(view.Board[r].Category).ToLowerInvariant()} {c + 1}";
            // Человек не называет версию наугад: без опоры на улики — только если в ряду остальное уже проверено.
            var blind = !exclude && !Classic && Evidence(card) <= 0.05 && !OnlyLeft(target, column);
            var lines = Evidence(card) > 0
                ? new[]
                {
                    $"скорее всего, {Name(view.Board[target].Category).ToLowerInvariant()} — карта {column + 1}.",
                    $"Ставлю на {Name(view.Board[target].Category).ToLowerInvariant()}, карта {column + 1}.",
                    $"По-моему, {Name(view.Board[target].Category)}: {column + 1}. Кто против?",
                }
                : new[]
                {
                    $"Пока не ясно. Проверил бы {Name(view.Board[target].Category).ToLowerInvariant()} — карту {column + 1}.",
                    $"Есть идея про {Name(view.Board[target].Category).ToLowerInvariant()}: карта {column + 1}?",
                };
            if (exclude)
                lines =
                [
                    $"{Name(view.Board[target].Category)} {column + 1} не подходит — улики к ней притянуты.",
                    $"Исключаю {Where(target, column)}: связь с подсказками случайная.",
                    $"Не верю в {Where(target, column)}, ищите в этом ряду другую.",
                ];
            var reply = Reply(target, card, exclude);
            var toned = Tone(lines[rng.Next(lines.Length)]);
            var opinion = reply + "Версия: " + toned;
            // Та же реплика — мыслями: что сказать, без слов (слова подберёт SpeechComposer).
            var thoughts = new List<Thought>();
            if (_reply is { } replied0) thoughts.Add(replied0);
            thoughts.Add(new Thought(exclude ? ThoughtKind.Exclude
                : Evidence(card) <= 0 ? ThoughtKind.Tentative
                : toned.StartsWith("Не уверен", StringComparison.Ordinal) ? ThoughtKind.VersionDoubt
                : toned.Contains("— уверен", StringComparison.Ordinal) ? ThoughtKind.VersionSure : ThoughtKind.Version, Where(target, column)));
            var linked = view.Hints.SelectMany(h => h.Cards.Select((id, i) => (Id: id, h.Round, Number: i + 1)))
                .Where(h => HintWeight(h.Id, card) > 0.01).OrderByDescending(h => HintWeight(h.Id, card)).Take(2).ToList();
            if (exclude) linked.Clear();
            var links = linked.Select(h => $"{BotDiscussion.HintRef(h.Round, h.Number)} → {Where(target, column)}: {Connection(h.Id, card)}.").ToList();
            thoughts.AddRange(linked.Select(h => new Thought(ThoughtKind.Link, Where(target, column), Why: Connection(h.Id, card), Hint: BotDiscussion.HintRef(h.Round, h.Number))));
            if (links.Count > 0) opinion += "\n" + string.Join("\n", links);
            else if (!exclude)
            {
                opinion += " Связь с открытыми уликами слабая — это только предположение.";
                thoughts.Add(new Thought(ThoughtKind.Weak));
            }
            // Карты «целиком думаю так» прикладываются к реплике — их подсвечивает стол.
            var versionCards = new List<string>();
            if (!Classic && view.Board.Count > 1)
            {
                var version = new List<string>();
                // Карты, проверенные исчезнувшим письмом, бот покажет красными — в «целиком думаю так» их не зовут.
                HashSet<(int Row, int Column)> gone = claimCard is not null && claimText.Contains("исчезла", StringComparison.Ordinal)
                    ? Checked(claimCard).ToHashSet()
                    : [];
                for (var r = 0; r < view.Board.Count; r++)
                {
                    var options = Enumerable.Range(0, view.Board[r].Cards.Count)
                        .Where(c => !KillerTeam || Truth is null || Truth[r] != c)
                        .Where(c => !gone.Contains((r, c))).ToList();
                    if (options.Count == 0) continue;
                    var best = BestColumn(r, options);
                    if (Evidence(Card(r, best)) > 0.05)
                    {
                        version.Add(Where(r, best));
                        versionCards.Add(Card(r, best));
                    }
                }
                if (version.Count > 1)
                {
                    opinion += "\nЦеликом думаю так: " + string.Join(", ", version) + ".";
                    thoughts.Add(new Thought(ThoughtKind.Whole, Cards: version));
                }
                else versionCards.Clear();
            }
            var accusation = Accusation();
            // «Фандорин, согласен… Подозреваю, что Фандорин из чёрных» — так не говорят: соглашаться с подозреваемым не будем.
            if (reply.Length > 0 && accusation.Length > 0 && reply.Split(',')[0] is { Length: > 1 } replied && accusation.Contains(replied, StringComparison.Ordinal))
            {
                opinion = opinion[reply.Length..];
                if (_reply is not null) thoughts.Remove(_reply);
            }
            opinion += accusation;
            if (_accused is { } accused0) thoughts.Add(accused0);
            if (blind)
            {
                var wait = claimCard is null
                    ? new[] { "Улик пока мало — версию не строю, жду подсказок.", "Пока гадать рано: подсказок почти нет.", "Подожду улик, наугад называть не буду." }[rng.Next(3)]
                    : "";
                opinion = (wait + accusation).Trim();
                thoughts.Clear();
                if (wait.Length > 0) thoughts.Add(new Thought(ThoughtKind.NoClues));
                if (_accused is { } accused1) thoughts.Add(accused1);
            }
            var situation = Situation(target, column);
            if (claimCard is null)
            {
                Intent = new SpeechIntent(Mood(), thoughts, situation);
                if (blind) return (opinion, new List<string>(), new List<string>());
                var only = new List<string> { card };
                var onlyNotes = new List<string> { exclude ? "исключаю" : "думаю, эта" };
                foreach (var c in versionCards.Where(c => !only.Contains(c)).Take(ChatService.MaxCards - 1))
                {
                    only.Add(c);
                    onlyNotes.Add("думаю, эта");
                }
                return (opinion, only, onlyNotes);
            }

            // Письмом проверяют сразу несколько карт поля — те, на которые оно похоже.
            var checkedCards = Checked(claimCard);
            // Чёрный, чьё письмо исчезло, может выдать за него другую карту руки — похожую на истину, —
            // чтобы стол счёл истину проверенной и отвергнутой. Проверить это никто, кроме Призрака, не может.
            if (!Classic && KillerTeam && Truth is { } lieTruth && claimText.Contains("исчезла", StringComparison.Ordinal)
                && rng.NextDouble() < (Spoiler ? Math.Max(BotTuning.Current.SpoilerVanish, FakeVanishChance) : FakeVanishChance) * FakeChance)
            {
                var row = Enumerable.Range(0, view.Board.Count).OrderBy(r => TruthMargin(r) + Noise()).First();
                var real = Card(row, lieTruth[row]);
                var cover = me.Hand.Where(h => !view.Board.Any(b => b.Cards.Contains(h))).OrderByDescending(h => Sim(h, real)).FirstOrDefault();
                if (cover is not null && Sim(cover, real) > .1)
                {
                    claimCard = cover;
                    checkedCards = [(row, lieTruth[row])];
                }
            }
            var cards = new List<string> { claimCard };
            var notes = new List<string> { "кидал эту" };
            foreach (var (r, c) in checkedCards)
            {
                cards.Add(Card(r, c));
                // «:0» связывает проверку с письмом — так стол знает, какое письмо проверяло эту карту.
                // Письмо исчезло — проверенные им карты бот показывает красным: по его рассуждению, это «не они».
                var gone = !Classic && claimText.Contains("исчезла", StringComparison.Ordinal);
                // Письмо ещё не открыто — проверка не удалась и не провалилась: «ждём», без зелёной метки.
                var pending = !Classic && !gone && !claimText.Contains("открылось", StringComparison.Ordinal);
                notes.Add((gone ? "проверял эту, исчезла" : pending ? "проверял эту, ждём"
                    : !Classic && !blind && !exclude && Card(r, c) == card ? "проверял эту; думаю, эта" : "проверял эту") + (Classic ? "" : ":0"));
            }

            if (!blind && !cards.Contains(card) && cards.Count < ChatService.MaxCards)
            {
                cards.Add(card);
                notes.Add(exclude ? "исключаю" : "думаю, эта");
            }

            if (!blind)
            {
                foreach (var c in versionCards.Where(c => !cards.Contains(c)))
                {
                    if (cards.Count >= ChatService.MaxCards) break;
                    cards.Add(c);
                    notes.Add("думаю, эта");
                }
            }

            // Одной фразой, как сказал бы человек: что проверял и почему (по первой связи).
            var checkedNames = checkedCards.Select(x => Where(x.Row, x.Column)).ToList();
            var checkedText = checkedCards.Count == 0 ? "Проверял: явной связи с полем нет." :
                $"Проверял: {(checkedNames.Count == 1 ? checkedNames[0] : string.Join(", ", checkedNames[..^1]) + " и " + checkedNames[^1])}" +
                $" — {Connection(claimCard, Card(checkedCards[0].Row, checkedCards[0].Column))}.";
            var vanished = claimText.Contains("исчезла", StringComparison.Ordinal);
            var conclusion = vanished
                ? checkedCards.Count == 0 ? "Письмо исчезло, но явных связей с картами поля я не вижу."
                    : NegativeWeight > 0 ? new[]
                        {
                            "Раз исчезло — это ослабляет эти версии, но не исключает: другие улики могут перевесить.",
                            "Раз Призрак его не открыл, эти карты под сомнением, но не вычеркнуты.",
                            "Исчезло — значит, скорее не они. Но одной проверки мало.",
                        }[rng.Next(3)]
                        : "Письмо исчезло, но я не считаю это исключением карт."
                : claimText.Contains("открыл", StringComparison.Ordinal)
                    ? checkedCards.Count > 0 ? "Открытие письма усиливает эти связи." : "Письмо открыто, но явных связей с картами поля я не вижу."
                    : "Пока жду результат проверки.";
            // Мысли о письме — перед мнением, в том же порядке, что и в тексте.
            var letterThoughts = new List<Thought>
            {
                new(claimText.Contains("исчезла", StringComparison.Ordinal) ? ThoughtKind.ClaimLost
                    : claimText.Contains("открылось", StringComparison.Ordinal) ? ThoughtKind.ClaimOpen : ThoughtKind.ClaimWait),
                checkedCards.Count == 0 ? new(ThoughtKind.CheckNothing)
                    : new(ThoughtKind.Check, Cards: checkedNames, Why: Connection(claimCard, Card(checkedCards[0].Row, checkedCards[0].Column))),
            };
            if (vanished && checkedCards.Count > 0)
                letterThoughts.Add(new(NegativeWeight > 0 ? ThoughtKind.LostConclusion : ThoughtKind.LostIgnored));
            else if (!vanished && claimText.Contains("открыл", StringComparison.Ordinal) && checkedCards.Count > 0)
                letterThoughts.Add(new(ThoughtKind.OpenConclusion));
            Intent = new SpeechIntent(Mood(), [.. letterThoughts, .. thoughts], situation);
            // Лимит чата соблюдаем по законченным строкам, не обрывая объяснение посреди слова.
            var sections = new[] { claimText, checkedText, conclusion, opinion }.Where(x => x.Length > 0);
            var text = string.Join("\n", sections);
            if (text.Length > ChatService.MaxTextLength)
                text = string.Join("\n", new[] { claimText, checkedText, conclusion, blind ? "" : $"Версия: {Where(target, column)}." }.Where(x => x.Length > 0));
            return (text, cards, notes);
        }

        /// <summary>Карты, которые бот сам называл вслух в прошлом раунде (главная карта реплики — первая «думаю, эта»).</summary>
        private HashSet<string> Repeated() => (mind?.OwnSaid ?? [])
            .Where(o => !o.IsCheck && o.Strength > 0 && o.Round == view.Round - 1).Select(o => o.CardId).ToHashSet();

        /// <summary>Карта — последняя непроверенная в ряду: остальные уже ослаблены исчезнувшими письмами.</summary>
        private bool OnlyLeft(int row, int column) =>
            Enumerable.Range(0, view.Board[row].Cards.Count).Where(c => c != column).All(c => Evidence(Card(row, c)) < -0.05);

        /// <summary>Отклик последнего <see cref="Reply(int, string, bool)"/> — мыслью для речи.</summary>
        private Thought? _reply;

        /// <summary>
        /// Отклик на последнее чужое мнение о том же ряду — как в живом разговоре: «Аня, согласен» или «Боб, не соглашусь».
        /// </summary>
        private string Reply(int row, string card, bool exclude = false)
        {
            _reply = null;
            if (mind is null) return "";
            // Отвечают только на сказанное вслух: ночной шёпот команды и мысли у доски никто, кроме своих, не слышал.
            var said = mind.Opinions.Where(o => o.Public && !o.IsCheck && o.Strength > 0 && o.Author != me.Id
                && view.Board[row].Cards.Contains(o.CardId)).ToList();
            var last = said.LastOrDefault(o => o.Round is null || o.Round == view.Round) ?? said.LastOrDefault();
            if (last is null || NameOf(last.Author) is not { } who) return "";
            who = BotNames.Bare(who);
            var agree = (last.CardId == card) != exclude;
            if (last.Round is { } spokenIn && spokenIn < view.Round)
            {
                // Спор с давней репликой — с пометкой, о каком раунде речь, иначе непонятно, с чем спорят.
                var when = view.Round - spokenIn == 1 ? "в прошлом раунде" : "раньше";
                _reply = new Thought(agree ? ThoughtKind.Agree : ThoughtKind.Disagree, CardName(last.CardId), who, Round: spokenIn);
                return agree
                    ? $"{who}, {when} у тебя тоже был {CardName(last.CardId)}, теперь и я так думаю. "
                    : $"{who}, {when} у тебя был {CardName(last.CardId)}, тут не соглашусь. ";
            }
            // Сверяемся с тем, что игрок сказал в последней реплике об этом ряду, а не с давним упоминанием.
            var theirs = mind.LatestOf(last.Author).FirstOrDefault(o => o.Strength > 0 && view.Board[row].Cards.Contains(o.CardId))?.CardId ?? last.CardId;
            // Исключаю карту, за которую он, — это спор, а не согласие.
            var agrees = (theirs == card) != exclude;
            _reply = new Thought(agrees ? ThoughtKind.Agree : ThoughtKind.Disagree, CardName(theirs), who);
            return agrees ? $"{who}, согласен. " : $"{who}, тут не соглашусь. ";
        }
    }
}
