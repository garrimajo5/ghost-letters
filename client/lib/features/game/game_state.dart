import '../../models/models.dart';

/// Пометка на карте: ✕ и ✓ (по словам кого), «считаю истинной», чьё это письмо (для подсказок)
/// и какую карту я называю своим письмом (для моих писем — можно и соврать).
class CardMark {
  const CardMark({
    this.crosses = 0,
    this.checks = 0,
    this.believed = false,
    this.crossBy = const [],
    this.checkBy = const [],
    this.claimedBy,
    this.claim,
  });

  factory CardMark.fromJson(Json j) {
    final src = j['sources'] is Map ? Map<String, dynamic>.from(j['sources'] as Map) : const <String, dynamic>{};
    List<String> ids(Object? v) => v is List ? v.map((e) => e.toString()).toList() : const [];
    return CardMark(
      crosses: (j['crosses'] as num?)?.toInt() ?? 0,
      checks: (j['checks'] as num?)?.toInt() ?? 0,
      believed: j['believed'] as bool? ?? false,
      crossBy: ids(src['crossBy']),
      checkBy: ids(src['checkBy']),
      claimedBy: src['claimedBy'] as String?,
      claim: src['claim'] as String?,
    );
  }

  final int crosses;
  final int checks;
  final bool believed;

  /// По словам кого стоят ✕ и ✓ (id игроков).
  final List<String> crossBy;
  final List<String> checkBy;

  /// Кто сказал, что эта подсказка — его письмо.
  final String? claimedBy;

  /// Для моего письма: какую карту я называю своей.
  final String? claim;

  bool get isEmpty => crosses == 0 && checks == 0 && !believed && crossBy.isEmpty && checkBy.isEmpty && claimedBy == null && claim == null;

  CardMark copyWith({
    int? crosses,
    int? checks,
    bool? believed,
    List<String>? crossBy,
    List<String>? checkBy,
    String? claimedBy,
    bool clearClaimedBy = false,
    String? claim,
    bool clearClaim = false,
  }) =>
      CardMark(
        crosses: (crosses ?? this.crosses).clamp(0, 20),
        checks: (checks ?? this.checks).clamp(0, 20),
        believed: believed ?? this.believed,
        crossBy: crossBy ?? this.crossBy,
        checkBy: checkBy ?? this.checkBy,
        claimedBy: clearClaimedBy ? null : (claimedBy ?? this.claimedBy),
        claim: clearClaim ? null : (claim ?? this.claim),
      );

  /// Переключить игрока в списке источников и подправить счётчик: добавили — +1, убрали — −1.
  CardMark toggleSource(String userId, {required bool cross}) {
    final list = [...(cross ? crossBy : checkBy)];
    final added = !list.remove(userId);
    if (added) list.add(userId);
    final count = (cross ? crosses : checks) + (added ? 1 : -1);
    return cross
        ? copyWith(crossBy: list, crosses: count < list.length ? list.length : count)
        : copyWith(checkBy: list, checks: count < list.length ? list.length : count);
  }

  Json toJson(String cardId) => {
        'cardId': cardId,
        'crosses': crosses,
        'checks': checks,
        'believed': believed,
        'sources': {'crossBy': crossBy, 'checkBy': checkBy, 'claimedBy': claimedBy, 'claim': claim},
      };
}

/// Сколько писем отправляет каждый игрок: вдвоём — по два.
int lettersPerPlayer(GameView view) => view.players.length == 2 ? 2 : 1;

/// Что игроку нужно сделать сейчас — строка-подсказка над панелью действий.
String actionHint(GameView v) {
  final me = v.me;
  if (me == null) return 'Экран стола';
  if (v.allowedCommands.isEmpty || (v.allowedCommands.length == 1 && v.can('Like'))) {
    return switch (v.phase) {
      'Discussion' when v.isRadio => 'Слушаем говорящего',
      'Finished' => 'Партия окончена',
      _ => 'Ждём других игроков',
    };
  }

  if (v.can('TeamSuggest')) {
    if (suggestedToKiller(v)) return 'Подсказка отправлена — решает Убийца';
    return v.phase == 'Night' ? 'Подскажите Убийце истинные улики' : 'Подскажите Убийце, кого назвать';
  }

  return switch (v.phase) {
    'RoleReveal' => 'Посмотрите свою роль',
    'Night' => 'Выберите по одной истинной улике в каждом ряду',
    'FirstClue' => 'Можно выложить первую зацепку',
    'Mailbox' => lettersPerPlayer(v) == 2 ? 'Отправьте в ящик две карты' : 'Отправьте в ящик одну карту',
    'GhostPick' when v.isGhost => 'Откройте письма-подсказки',
    'GhostPick' || 'Refill' => 'Сбросьте карту или оставьте руку',
    'Discussion' => v.isRadio ? (v.currentSpeaker == me.id ? 'Ваше слово' : 'Можно поднять руку') : 'Обсуждайте и нажмите «Готов»',
    'Voting' => 'Голосуйте',
    'VoteTie' => 'Ничья: обсудите и переголосуйте',
    'Hunt' => 'Найдите Свидетеля или Эксперта',
    'BlackmailerHunt' => 'Найдите Шантажиста',
    'BlackmailerClaim' => 'Назовите истинные улики',
    'AwardNomination' => 'Выдвиньте игрока на ачивку',
    'AwardVoting' => 'Голосуйте за выдвижения',
    _ => '',
  };
}

/// Команды, которые ничего не ждут от игрока: лайк и «поднять руку» — по желанию.
const _optionalCommands = {'Like', 'RaiseHand'};

/// От игрока сейчас ждут действие — экран подсвечивает это: «ВАШ ХОД», вибрация, кнопка внизу.
bool needsMe(GameView v) =>
    v.me != null && v.allowedCommands.any((c) => !_optionalCommands.contains(c) && !(c == 'TeamSuggest' && suggestedToKiller(v)));

/// Сообщник уже подсказал Убийце в этой фазе (подсказку можно поменять, но ход уже не ждут).
bool suggestedToKiller(GameView v) => v.me != null && v.teamSuggestions.any((s) => s.from == v.me!.id);

/// Раунды по правилам: 2–4 игрока → 5, 5–7 → 4, 8–10 → 3, 11–12 → 2.
int defaultRounds(int players) => players <= 4
    ? 5
    : players <= 7
        ? 4
        : players <= 10
            ? 3
            : 2;

/// Финал: раунды закончились — голосование, охота, итоги. Рука и письма больше не нужны.
bool isFinale(GameView v) => const {
      'Voting',
      'VoteTie',
      'Hunt',
      'BlackmailerHunt',
      'BlackmailerClaim',
      'AwardNomination',
      'AwardVoting',
      'Finished',
    }.contains(v.phase);
