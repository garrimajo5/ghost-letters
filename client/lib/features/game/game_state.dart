import '../../models/models.dart';

/// Пометка на карте поля: ✕, ✓ и «считаю истинной».
class CardMark {
  const CardMark({this.crosses = 0, this.checks = 0, this.believed = false});

  factory CardMark.fromJson(Json j) => CardMark(
        crosses: (j['crosses'] as num?)?.toInt() ?? 0,
        checks: (j['checks'] as num?)?.toInt() ?? 0,
        believed: j['believed'] as bool? ?? false,
      );

  final int crosses;
  final int checks;
  final bool believed;

  bool get isEmpty => crosses == 0 && checks == 0 && !believed;

  CardMark copyWith({int? crosses, int? checks, bool? believed}) => CardMark(
        crosses: (crosses ?? this.crosses).clamp(0, 20),
        checks: (checks ?? this.checks).clamp(0, 20),
        believed: believed ?? this.believed,
      );

  Json toJson(String cardId) => {'cardId': cardId, 'crosses': crosses, 'checks': checks, 'believed': believed};
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
bool needsMe(GameView v) => v.me != null && v.allowedCommands.any((c) => !_optionalCommands.contains(c));

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
