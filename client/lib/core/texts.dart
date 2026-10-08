/// Русские подписи для значений с сервера.
class T {
  static const roles = {
    'Ghost': 'Призрак',
    'Detective': 'Детектив',
    'Killer': 'Убийца',
    'Accomplice': 'Сообщник',
    'Witness': 'Свидетель',
    'Expert': 'Эксперт',
    'Blackmailer': 'Шантажист',
    'Imitator': 'Подражатель',
  };

  static const roleHints = {
    'Ghost': 'Знаете всё. Помогайте детективам только картами-подсказками — ни слова.',
    'Detective': 'Найдите истинные улики и Убийцу.',
    'Killer': 'Выберите истинные улики и запутайте следствие. Не выдайте себя.',
    'Accomplice': 'Знаете Убийцу и улики. Мешайте детективам, но не попадитесь.',
    'Witness': 'Знаете Убийцу. Помогите, но не выдайте себя — Убийца будет искать вас.',
    'Expert': 'Знаете истинные улики. Помогите, но не выдайте себя.',
    'Blackmailer': 'Знаете команду Убийцы. Угадайте улики и не выдайте себя.',
    'Imitator': 'Никто вас не знает. Добейтесь, чтобы вас арестовали как Убийцу.',
  };

  static const categories = {'Motive': 'Мотив', 'Place': 'Место', 'Method': 'Способ', 'Secret': 'Тайна'};

  static const phases = {
    'RoleReveal': 'Знакомство с ролью',
    'Night': 'Ночь',
    'FirstClue': 'Первая зацепка',
    'Mailbox': 'Почтовый ящик',
    'GhostPick': 'Призрак выбирает',
    'Refill': 'Сброс и добор',
    'Discussion': 'Обсуждение',
    'Voting': 'Голосование',
    'VoteTie': 'Ничья',
    'Hunt': 'Охота',
    'BlackmailerHunt': 'Поиск Шантажиста',
    'BlackmailerClaim': 'Шантажист',
    'AwardNomination': 'Итоги и награды',
    'AwardVoting': 'Голосование за ачивки',
    'Finished': 'Партия окончена',
  };

  static const sides = {
    'Detectives': 'Дело раскрыто — победа детективов',
    'Killer': 'Победа команды Убийцы',
    'Blackmailer': 'Шантажист перехитрил всех',
    'Nobody': 'Дело не раскрыто',
  };

  static String role(String? r) => roles[r] ?? (r ?? '?');

  static String phase(String p) => phases[p] ?? p;

  /// Короткие названия фаз для плашки в шапке.
  static const shortPhases = {
    'RoleReveal': 'Роли',
    'FirstClue': 'Зацепка',
    'Mailbox': 'Письма',
    'GhostPick': 'Призрак',
    'Refill': 'Добор',
    'BlackmailerHunt': 'Охота',
    'BlackmailerClaim': 'Шантажист',
    'AwardNomination': 'Ачивки',
    'AwardVoting': 'Ачивки',
    'Finished': 'Итоги',
  };

  static String shortPhase(String p) => shortPhases[p] ?? phase(p);

  static String category(String c) => categories[c] ?? c;

  static const _months = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];

  /// «сегодня 21:40», «вчера 18:05», «7 окт 21:40» — по местному времени.
  static String when(DateTime? t, {DateTime? now}) {
    if (t == null) return '';
    final local = t.toLocal();
    final today = now ?? DateTime.now();
    final day = DateTime(local.year, local.month, local.day);
    final diff = DateTime(today.year, today.month, today.day).difference(day).inDays;
    final hm = '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
    if (diff == 0) return 'сегодня $hm';
    if (diff == 1) return 'вчера $hm';
    return '${local.day} ${_months[local.month - 1]}${local.year != today.year ? ' ${local.year}' : ''} $hm';
  }

  /// «6 игроков», «2 игрока», «1 игрок».
  static String players(int n) {
    final mod10 = n % 10, mod100 = n % 100;
    final word = mod10 == 1 && mod100 != 11
        ? 'игрок'
        : (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14) ? 'игрока' : 'игроков');
    return '$n $word';
  }
}
