// Модели ответов сервера. JSON — camelCase, перечисления строками (как на сервере).

typedef Json = Map<String, dynamic>;

List<T> _list<T>(dynamic v, T Function(Json) f) =>
    v is List ? v.whereType<Map>().map((e) => f(Map<String, dynamic>.from(e))).toList() : <T>[];

List<String> _strings(dynamic v) => v is List ? v.map((e) => e.toString()).toList() : <String>[];

List<int> _ints(dynamic v) => v is List ? v.map((e) => (e as num).toInt()).toList() : <int>[];

DateTime? _date(dynamic v) => v is String ? DateTime.tryParse(v) : null;

class User {
  const User({required this.id, required this.nickname, required this.avatarColor, this.avatarId});

  factory User.fromJson(Json j) => User(
        id: j['id'] as String,
        nickname: j['nickname'] as String,
        avatarColor: j['avatarColor'] as String,
        avatarId: j['avatarId'] as String?,
      );

  final String id;
  final String nickname;
  final String avatarColor;

  /// Загруженная аватарка; null — круг с буквой.
  final String? avatarId;

  Json toJson() => {'id': id, 'nickname': nickname, 'avatarColor': avatarColor, 'avatarId': avatarId};
}

class AuthTokens {
  const AuthTokens({required this.accessToken, required this.refreshToken, required this.user});

  factory AuthTokens.fromJson(Json j) => AuthTokens(
        accessToken: j['accessToken'] as String,
        refreshToken: j['refreshToken'] as String,
        user: User.fromJson(Map<String, dynamic>.from(j['user'] as Map)),
      );

  final String accessToken;
  final String refreshToken;
  final User user;
}

// ---------- Роли и настройки ----------

enum ImitatorMode { none, replaceDetective, replaceAccomplice }

const _imitatorWire = {
  ImitatorMode.none: 'None',
  ImitatorMode.replaceDetective: 'ReplaceDetective',
  ImitatorMode.replaceAccomplice: 'ReplaceAccomplice',
};

class RoleOptions {
  const RoleOptions({
    this.killerEnabled = true,
    this.useWitness = true,
    this.useExpert = true,
    this.useBlackmailer = false,
    this.imitator = ImitatorMode.none,
    this.extraAccomplices = 0,
  });

  factory RoleOptions.fromJson(Json j) => RoleOptions(
        killerEnabled: j['killerEnabled'] as bool? ?? true,
        useWitness: j['useWitness'] as bool? ?? true,
        useExpert: j['useExpert'] as bool? ?? true,
        useBlackmailer: j['useBlackmailer'] as bool? ?? false,
        imitator: _imitatorWire.entries
            .firstWhere((e) => e.value == j['imitator'], orElse: () => const MapEntry(ImitatorMode.none, 'None'))
            .key,
        extraAccomplices: (j['extraAccomplices'] as num?)?.toInt() ?? 0,
      );

  final bool killerEnabled;
  final bool useWitness;
  final bool useExpert;
  final bool useBlackmailer;
  final ImitatorMode imitator;

  /// Сколько Детективов заменить Сообщниками сверх таблицы правил (0–2).
  final int extraAccomplices;

  RoleOptions copyWith(
          {bool? killerEnabled, bool? useWitness, bool? useExpert, bool? useBlackmailer, ImitatorMode? imitator, int? extraAccomplices}) =>
      RoleOptions(
        killerEnabled: killerEnabled ?? this.killerEnabled,
        useWitness: useWitness ?? this.useWitness,
        useExpert: useExpert ?? this.useExpert,
        useBlackmailer: useBlackmailer ?? this.useBlackmailer,
        imitator: imitator ?? this.imitator,
        extraAccomplices: extraAccomplices ?? this.extraAccomplices,
      );

  Json toJson() => {
        'killerEnabled': killerEnabled,
        'useWitness': useWitness,
        'useExpert': useExpert,
        'useBlackmailer': useBlackmailer,
        'imitator': _imitatorWire[imitator],
        'extraAccomplices': extraAccomplices,
      };
}

/// Настройки лобби. Таймеры храним как есть (секунды по фазам) и правим нужные.
class LobbySettings {
  const LobbySettings({
    this.useSecretRow = true,
    this.columns = 5,
    this.rounds,
    this.handSize = 5,
    this.roles = const RoleOptions(),
    this.discussion = 'Radio',
    this.tempo = 'live',
    this.turnHours = 24,
    this.timers = const {},
    this.cardSets = const ['original', 'mailbox', 'ritual', 'mirror'],
    this.ghostUserId,
    this.ranked = true,
  });

  factory LobbySettings.fromJson(Json j) => LobbySettings(
        useSecretRow: j['useSecretRow'] as bool? ?? true,
        columns: (j['columns'] as num?)?.toInt() ?? 5,
        rounds: (j['rounds'] as num?)?.toInt(),
        handSize: (j['handSize'] as num?)?.toInt() ?? 5,
        roles: j['roles'] is Map ? RoleOptions.fromJson(Map<String, dynamic>.from(j['roles'] as Map)) : const RoleOptions(),
        discussion: j['discussion'] as String? ?? 'Radio',
        tempo: j['tempo'] as String? ?? 'live',
        turnHours: (j['turnHours'] as num?)?.toInt() ?? 24,
        timers: j['timers'] is Map
            ? Map<String, dynamic>.from(j['timers'] as Map).map((k, v) => MapEntry(k, (v as num).toInt()))
            : const {},
        cardSets: _strings(j['cardSets']).isEmpty ? const ['original', 'mailbox', 'ritual', 'mirror'] : _strings(j['cardSets']),
        ghostUserId: j['ghostUserId'] as String?,
        ranked: j['ranked'] as bool? ?? true,
      );

  final bool useSecretRow;
  final int columns;
  final int? rounds;
  final int handSize;
  final RoleOptions roles;
  final String discussion;
  final String tempo;
  final int turnHours;
  final Map<String, int> timers;
  final List<String> cardSets;

  /// Призрак, назначенный хостом; null — по жребию.
  final String? ghostUserId;

  /// Рейтинговая партия (итог меняет рейтинг) или обычная.
  final bool ranked;

  int timer(String key, int fallback) => timers[key] ?? fallback;

  LobbySettings copyWith({
    bool? useSecretRow,
    int? columns,
    int? rounds,
    bool clearRounds = false,
    RoleOptions? roles,
    String? discussion,
    String? tempo,
    int? turnHours,
    Map<String, int>? timers,
    List<String>? cardSets,
    String? ghostUserId,
    bool clearGhost = false,
    bool? ranked,
  }) =>
      LobbySettings(
        useSecretRow: useSecretRow ?? this.useSecretRow,
        columns: columns ?? this.columns,
        rounds: clearRounds ? null : rounds ?? this.rounds,
        handSize: handSize,
        roles: roles ?? this.roles,
        discussion: discussion ?? this.discussion,
        tempo: tempo ?? this.tempo,
        turnHours: turnHours ?? this.turnHours,
        timers: timers ?? this.timers,
        cardSets: cardSets ?? this.cardSets,
        ghostUserId: clearGhost ? null : ghostUserId ?? this.ghostUserId,
        ranked: ranked ?? this.ranked,
      );

  Json toJson() => {
        'useSecretRow': useSecretRow,
        'columns': columns,
        'rounds': rounds,
        'handSize': handSize,
        'roles': roles.toJson(),
        'discussion': discussion,
        'tempo': tempo,
        'turnHours': turnHours,
        if (timers.isNotEmpty) 'timers': timers,
        'cardSets': cardSets,
        'ghostUserId': ghostUserId,
        'ranked': ranked,
      };
}

// ---------- Лобби ----------

class WatchableGame {
  const WatchableGame({required this.code, required this.title, required this.gameId, required this.phase, required this.players});

  factory WatchableGame.fromJson(Json j) => WatchableGame(
        code: j['code'] as String, title: j['title'] as String, gameId: j['gameId'] as String,
        phase: j['phase'] as String, players: j['players'] as int,
      );

  final String code;
  final String title;
  final String gameId;
  final String phase;
  final int players;
}

class LobbyMember {
  const LobbyMember({
    required this.userId,
    required this.nickname,
    required this.avatarColor,
    required this.seat,
    required this.mode,
    required this.isReady,
    this.isBot = false,
    this.avatarId,
  });

  factory LobbyMember.fromJson(Json j) => LobbyMember(
        userId: j['userId'] as String,
        nickname: j['nickname'] as String,
        avatarColor: j['avatarColor'] as String,
        seat: (j['seat'] as num).toInt(),
        mode: j['mode'] as String,
        isReady: j['isReady'] as bool? ?? false,
        isBot: j['isBot'] as bool? ?? false,
        avatarId: j['avatarId'] as String?,
      );

  final String? avatarId;
  final String userId;
  final String nickname;
  final String avatarColor;
  final int seat;
  final String mode;
  final bool isReady;
  final bool isBot;

  bool get isTable => mode == 'table';
  bool get isSpectator => mode == 'spectator';
  bool get isPlayer => mode == 'player';
}

class Lobby {
  const Lobby({
    required this.id,
    required this.code,
    required this.title,
    required this.hostUserId,
    required this.status,
    required this.settings,
    required this.currentGameId,
    required this.members,
  });

  factory Lobby.fromJson(Json j) => Lobby(
        id: j['id'] as String,
        code: j['code'] as String,
        title: j['title'] as String? ?? '',
        hostUserId: j['hostUserId'] as String,
        status: j['status'] as String,
        settings: LobbySettings.fromJson(Map<String, dynamic>.from(j['settings'] as Map)),
        currentGameId: j['currentGameId'] as String?,
        members: _list(j['members'], LobbyMember.fromJson),
      );

  final String id;
  final String code;
  final String title;
  final String hostUserId;
  final String status;
  final LobbySettings settings;
  final String? currentGameId;
  final List<LobbyMember> members;

  List<LobbyMember> get players => members.where((m) => m.isPlayer).toList();
}

// ---------- Партия ----------

class BoardRow {
  const BoardRow(this.category, this.cards);

  factory BoardRow.fromJson(Json j) => BoardRow(j['category'] as String, _strings(j['cards']));

  final String category;
  final List<String> cards;
}

class HintGroup {
  const HintGroup(this.round, this.cards);

  factory HintGroup.fromJson(Json j) => HintGroup((j['round'] as num).toInt(), _strings(j['cards']));

  final int round;
  final List<String> cards;
}

class PlayerInfo {
  const PlayerInfo({
    required this.id,
    required this.seat,
    required this.isGhost,
    required this.knownRole,
    required this.hasActed,
    required this.handCount,
  });

  factory PlayerInfo.fromJson(Json j) => PlayerInfo(
        id: j['id'] as String,
        seat: (j['seat'] as num).toInt(),
        isGhost: j['isGhost'] as bool? ?? false,
        knownRole: j['knownRole'] as String?,
        hasActed: j['hasActed'] as bool? ?? false,
        handCount: (j['handCount'] as num?)?.toInt() ?? 0,
      );

  final String id;
  final int seat;
  final bool isGhost;
  final String? knownRole;
  final bool hasActed;
  final int handCount;
}

class MyLetter {
  const MyLetter(this.round, this.cardId, this.revealed);

  factory MyLetter.fromJson(Json j) => MyLetter((j['round'] as num).toInt(), j['cardId'] as String, j['revealed'] as bool?);

  final int round;
  final String cardId;
  final bool? revealed;
}

class Me {
  const Me({required this.id, required this.role, required this.hand, required this.letters, this.discarded = const []});

  factory Me.fromJson(Json j) => Me(
        id: j['id'] as String,
        role: j['role'] as String,
        hand: _strings(j['hand']),
        letters: _list(j['letters'], MyLetter.fromJson),
        discarded: _strings(j['discarded']),
      );

  final String id;
  final String role;
  final List<String> hand;
  final List<MyLetter> letters;

  /// Карты, которые я сбросил за партию (видны только мне).
  final List<String> discarded;
}

class VoteStage {
  const VoteStage({
    required this.index,
    required this.kind,
    required this.row,
    required this.attempt,
    required this.candidateColumns,
    required this.candidateSuspects,
  });

  factory VoteStage.fromJson(Json j) => VoteStage(
        index: (j['index'] as num).toInt(),
        kind: j['kind'] as String,
        row: (j['row'] as num).toInt(),
        attempt: (j['attempt'] as num).toInt(),
        candidateColumns: _ints(j['candidateColumns']),
        candidateSuspects: _strings(j['candidateSuspects']),
      );

  final int index;
  final String kind;
  final int row;
  final int attempt;
  final List<int> candidateColumns;
  final List<String> candidateSuspects;

  bool get isRow => kind == 'Row';
}

class VoteRecord {
  const VoteRecord(this.stage, this.attempt, this.voter, this.column, this.suspect);

  factory VoteRecord.fromJson(Json j) => VoteRecord(
        (j['stage'] as num).toInt(),
        (j['attempt'] as num).toInt(),
        j['voter'] as String,
        (j['column'] as num?)?.toInt(),
        j['suspect'] as String?,
      );

  final int stage;
  final int attempt;
  final String voter;
  final int? column;
  final String? suspect;
}

class VoteOutcome {
  const VoteOutcome({
    required this.stage,
    required this.kind,
    required this.row,
    required this.column,
    required this.suspect,
    required this.correct,
    required this.byLot,
    required this.revealedRole,
  });

  factory VoteOutcome.fromJson(Json j) => VoteOutcome(
        stage: (j['stage'] as num).toInt(),
        kind: j['kind'] as String,
        row: (j['row'] as num).toInt(),
        column: (j['column'] as num?)?.toInt(),
        suspect: j['suspect'] as String?,
        correct: j['correct'] as bool?,
        byLot: j['byLot'] as bool? ?? false,
        revealedRole: j['revealedRole'] as String?,
      );

  final int stage;
  final String kind;
  final int row;
  final int? column;
  final String? suspect;
  final bool? correct;
  final bool byLot;
  final String? revealedRole;
}

class GameResult {
  const GameResult({
    required this.solved,
    required this.correctRows,
    required this.killerCaught,
    required this.side,
    required this.imitatorWon,
    required this.blackmailerWon,
    required this.winners,
  });

  factory GameResult.fromJson(Json j) => GameResult(
        solved: j['solved'] as bool? ?? false,
        correctRows: (j['correctRows'] as num?)?.toInt() ?? 0,
        killerCaught: j['killerCaught'] as bool? ?? false,
        side: j['side'] as String? ?? 'Nobody',
        imitatorWon: j['imitatorWon'] as bool? ?? false,
        blackmailerWon: j['blackmailerWon'] as bool? ?? false,
        winners: _strings(j['winners']),
      );

  final bool solved;
  final int correctRows;
  final bool killerCaught;
  final String side;
  final bool imitatorWon;
  final bool blackmailerWon;
  final List<String> winners;
}

class AwardEntry {
  const AwardEntry({
    required this.index,
    required this.code,
    required this.nominee,
    required this.nominatedByCount,
    required this.mine,
    required this.votes,
    required this.won,
  });

  factory AwardEntry.fromJson(Json j) => AwardEntry(
        index: (j['index'] as num).toInt(),
        code: j['code'] as String,
        nominee: j['nominee'] as String,
        nominatedByCount: (j['nominatedByCount'] as num?)?.toInt() ?? 1,
        mine: j['mineNomination'] as bool? ?? false,
        votes: (j['votes'] as num?)?.toInt(),
        won: j['won'] as bool? ?? false,
      );

  final int index;
  final String code;
  final String nominee;
  final int nominatedByCount;
  final bool mine;
  final int? votes;
  final bool won;
}

class LikeCount {
  const LikeCount(this.player, this.count, this.likedByMe);

  factory LikeCount.fromJson(Json j) =>
      LikeCount(j['player'] as String, (j['count'] as num).toInt(), j['likedByMe'] as bool? ?? false);

  final String player;
  final int count;
  final bool likedByMe;
}

class Finale {
  const Finale({
    required this.currentStage,
    required this.stagesTotal,
    required this.myVoteColumn,
    required this.myVoteSuspect,
    required this.hasMyVote,
    required this.votes,
    required this.outcomes,
    required this.arrested,
    required this.huntTarget,
    required this.huntSuccess,
    required this.result,
    required this.awards,
    required this.likes,
  });

  factory Finale.fromJson(Json j) {
    final myVote = j['myVote'] is Map ? Map<String, dynamic>.from(j['myVote'] as Map) : null;
    final hunt = j['hunt'] is Map ? Map<String, dynamic>.from(j['hunt'] as Map) : null;
    return Finale(
      currentStage: j['currentStage'] is Map ? VoteStage.fromJson(Map<String, dynamic>.from(j['currentStage'] as Map)) : null,
      stagesTotal: (j['stagesTotal'] as num?)?.toInt() ?? 0,
      hasMyVote: myVote != null,
      myVoteColumn: (myVote?['column'] as num?)?.toInt(),
      myVoteSuspect: myVote?['suspect'] as String?,
      votes: _list(j['votes'], VoteRecord.fromJson),
      outcomes: _list(j['outcomes'], VoteOutcome.fromJson),
      arrested: _strings(j['arrested']),
      huntTarget: hunt?['target'] as String?,
      huntSuccess: hunt?['success'] as bool?,
      result: j['result'] is Map ? GameResult.fromJson(Map<String, dynamic>.from(j['result'] as Map)) : null,
      awards: _list(j['awards'], AwardEntry.fromJson),
      likes: _list(j['likes'], LikeCount.fromJson),
    );
  }

  final VoteStage? currentStage;
  final int stagesTotal;
  final bool hasMyVote;
  final int? myVoteColumn;
  final String? myVoteSuspect;
  final List<VoteRecord> votes;
  final List<VoteOutcome> outcomes;
  final List<String> arrested;
  final String? huntTarget;
  final bool? huntSuccess;
  final GameResult? result;
  final List<AwardEntry> awards;
  final List<LikeCount> likes;
}

/// Проекция партии для одного игрока (или экрана стола — тогда me == null).
class GameView {
  const GameView({
    required this.gameId,
    required this.version,
    required this.phase,
    required this.round,
    required this.totalRounds,
    required this.discussion,
    required this.board,
    required this.hints,
    required this.vanishedCount,
    required this.players,
    required this.me,
    required this.truth,
    required this.mailboxCount,
    required this.mailboxForGhost,
    required this.radioHolder,
    required this.currentSpeaker,
    required this.floorGrantedTo,
    required this.raisedHands,
    required this.allowedCommands,
    required this.finale,
    this.teamSuggestions = const [],
    this.huntRoles = const [],
  });

  factory GameView.fromJson(Json j) => GameView(
        gameId: j['gameId'] as String,
        version: (j['version'] as num).toInt(),
        phase: j['phase'] as String,
        round: (j['round'] as num).toInt(),
        totalRounds: (j['totalRounds'] as num).toInt(),
        discussion: j['discussion'] as String? ?? 'Radio',
        board: _list(j['board'], BoardRow.fromJson),
        hints: _list(j['hints'], HintGroup.fromJson),
        vanishedCount: (j['vanishedCount'] as num?)?.toInt() ?? 0,
        players: _list(j['players'], PlayerInfo.fromJson),
        me: j['me'] is Map ? Me.fromJson(Map<String, dynamic>.from(j['me'] as Map)) : null,
        truth: j['truth'] is List ? _ints(j['truth']) : null,
        mailboxCount: (j['mailboxCount'] as num?)?.toInt() ?? 0,
        mailboxForGhost: j['mailboxForGhost'] is List ? _strings(j['mailboxForGhost']) : null,
        radioHolder: j['radioHolder'] as String?,
        currentSpeaker: j['currentSpeaker'] as String?,
        floorGrantedTo: j['floorGrantedTo'] as String?,
        raisedHands: _strings(j['raisedHands']),
        allowedCommands: _strings(j['allowedCommands']),
        finale: j['finale'] is Map ? Finale.fromJson(Map<String, dynamic>.from(j['finale'] as Map)) : null,
        teamSuggestions: j['teamSuggestions'] is List ? _list(j['teamSuggestions'], TeamSuggestion.fromJson) : const [],
        huntRoles: _strings(j['huntRoles']),
      );

  final String gameId;
  final int version;
  final String phase;
  final int round;
  final int totalRounds;
  final String discussion;
  final List<BoardRow> board;
  final List<HintGroup> hints;
  final int vanishedCount;
  final List<PlayerInfo> players;
  final Me? me;
  final List<int>? truth;
  final int mailboxCount;
  final List<String>? mailboxForGhost;
  final String? radioHolder;
  final String? currentSpeaker;
  final String? floorGrantedTo;
  final List<String> raisedHands;
  final List<String> allowedCommands;
  final Finale? finale;

  /// Подсказки Сообщников Убийце (ночь, охота) — приходят только команде Убийцы.
  final List<TeamSuggestion> teamSuggestions;

  /// На охоте: какие из ролей Свидетель/Эксперт есть в партии (пусто — неизвестно).
  final List<String> huntRoles;

  bool can(String command) => allowedCommands.contains(command);

  bool get isGhost => me?.role == 'Ghost';

  bool get isRadio => discussion == 'Radio';

  PlayerInfo? player(String id) {
    for (final p in players) {
      if (p.id == id) return p;
    }
    return null;
  }
}

class RosterEntry {
  const RosterEntry(this.id, this.nickname, this.avatarColor, this.seat, [this.avatarId]);

  factory RosterEntry.fromJson(Json j) => RosterEntry(
      j['id'] as String, j['nickname'] as String, j['avatarColor'] as String, (j['seat'] as num).toInt(), j['avatarId'] as String?);

  final String? avatarId;
  final String id;
  final String nickname;
  final String avatarColor;
  final int seat;
}

class GameSnapshot {
  const GameSnapshot({required this.view, required this.deadline, required this.roster, required this.lobbyId});

  factory GameSnapshot.fromJson(Json j) => GameSnapshot(
        view: GameView.fromJson(Map<String, dynamic>.from(j['view'] as Map)),
        deadline: _date(j['deadline']),
        roster: _list(j['roster'], RosterEntry.fromJson),
        lobbyId: j['lobbyId'] as String?,
      );

  final GameView view;
  final DateTime? deadline;
  final List<RosterEntry> roster;
  final String? lobbyId;

  GameSnapshot withView(GameView view, DateTime? deadline) =>
      GameSnapshot(view: view, deadline: deadline, roster: roster, lobbyId: lobbyId);
}

class ChatMessage {
  const ChatMessage({
    required this.id,
    required this.channel,
    required this.authorId,
    required this.kind,
    required this.text,
    required this.cardIds,
    required this.createdAt,
    required this.round,
    this.mediaId,
    this.durationMs,
    this.cardNotes = const [],
  });

  factory ChatMessage.fromJson(Json j) => ChatMessage(
        id: j['id'] as String,
        channel: j['channel'] as String,
        authorId: j['authorId'] as String?,
        kind: j['kind'] as String,
        text: j['text'] as String?,
        cardIds: _strings(j['cardIds']),
        createdAt: _date(j['createdAt']) ?? DateTime.now(),
        round: (j['round'] as num?)?.toInt() ?? 0,
        mediaId: j['mediaId'] as String?,
        durationMs: (j['durationMs'] as num?)?.toInt(),
        cardNotes: _strings(j['cardNotes']),
      );

  final String id;
  final String channel;
  final String? authorId;
  final String kind;
  final String? text;
  final List<String> cardIds;
  final DateTime createdAt;
  final int round;

  /// Подписи под картами по порядку cardIds: «кидал эту», «проверял эту»…
  final List<String> cardNotes;

  String? noteFor(int index) => index < cardNotes.length && cardNotes[index].isNotEmpty ? cardNotes[index] : null;

  /// Голосовое: id файла на сервере и длительность.
  final String? mediaId;
  final int? durationMs;

  bool get isVoice => kind == 'voice' && mediaId != null;
}

class MyGame {
  const MyGame({
    required this.gameId,
    required this.status,
    required this.phase,
    required this.yourTurn,
    required this.deadline,
    this.round = 0,
    this.totalRounds = 0,
    this.players = 0,
    this.title,
    this.role,
    this.won,
    this.startedAt,
    this.finishedAt,
  });

  factory MyGame.fromJson(Json j) => MyGame(
        gameId: j['gameId'] as String,
        status: j['status'] as String,
        phase: j['phase'] as String,
        yourTurn: j['yourTurn'] as bool? ?? false,
        deadline: _date(j['deadline']),
        round: (j['round'] as num?)?.toInt() ?? 0,
        totalRounds: (j['totalRounds'] as num?)?.toInt() ?? 0,
        players: (j['players'] as num?)?.toInt() ?? 0,
        title: j['title'] as String?,
        role: j['role'] as String?,
        won: j['won'] as bool?,
        startedAt: _date(j['startedAt']),
        finishedAt: _date(j['finishedAt']),
      );

  final String gameId;
  final String status;
  final String phase;
  final bool yourTurn;
  final DateTime? deadline;
  final int round;
  final int totalRounds;
  final int players;
  final String? title;
  final String? role;
  final bool? won;
  final DateTime? startedAt;
  final DateTime? finishedAt;
}

class Profile {
  const Profile({
    required this.user,
    required this.games,
    required this.wins,
    required this.rating,
    required this.likes,
    required this.achievements,
  });

  factory Profile.fromJson(Json j) {
    final stats = Map<String, dynamic>.from(j['stats'] as Map);
    return Profile(
      user: User.fromJson(Map<String, dynamic>.from(j['user'] as Map)),
      games: (stats['games'] as num).toInt(),
      wins: (stats['wins'] as num).toInt(),
      rating: (stats['rating'] as num).toInt(),
      likes: (stats['likesReceived'] as num).toInt(),
      achievements: _list(j['achievements'], (a) => (title: a['title'] as String, count: (a['count'] as num).toInt())),
    );
  }

  final User user;
  final int games;
  final int wins;
  final int rating;
  final int likes;
  final List<({String title, int count})> achievements;
}

/// Код входа на другом устройстве.
class LinkCode {
  const LinkCode({required this.code, required this.expiresAt});

  factory LinkCode.fromJson(Json j) => LinkCode(code: j['code'] as String, expiresAt: DateTime.parse(j['expiresAt'] as String).toLocal());

  final String code;
  final DateTime expiresAt;

  /// Для чтения вслух и ввода: «ABCD-EFGH».
  String get pretty => code.length == 8 ? '${code.substring(0, 4)}-${code.substring(4)}' : code;
}

/// Подсказка Сообщника: карты ночью (columns) или игрок на охоте (target, guess).
class TeamSuggestion {
  const TeamSuggestion({required this.from, this.columns, this.target, this.guess});

  factory TeamSuggestion.fromJson(Json j) => TeamSuggestion(
        from: j['from'] as String,
        columns: j['columns'] is List ? _ints(j['columns']) : null,
        target: j['target'] as String?,
        guess: j['guess'] as String?,
      );

  final String from;
  final List<int>? columns;
  final String? target;
  final String? guess;
}

/// Строка таблицы лидеров.
class LeaderRow {
  const LeaderRow({required this.user, required this.rating, required this.games, required this.wins, this.isBot = false});

  factory LeaderRow.fromJson(Json j) => LeaderRow(
        user: User.fromJson(Map<String, dynamic>.from(j['user'] as Map)),
        rating: (j['rating'] as num).toInt(),
        games: (j['games'] as num).toInt(),
        wins: (j['wins'] as num).toInt(),
        isBot: j['isBot'] as bool? ?? false,
      );

  final User user;
  final int rating;
  final int games;
  final int wins;
  final bool isBot;
}

/// Характер бота: шесть спектров 0…1 (внимание к смыслу/форме/цвету — доли).
class BotSpectra {
  const BotSpectra({
    this.meaning = 0.5,
    this.shape = 0.25,
    this.color = 0.25,
    this.negative = 0.5,
    this.memory = 0.3,
    this.risk = 0.5,
    this.compromise = 0.5,
    this.variability = 0.2,
    this.strictness = 0.5,
    this.details = 0.25,
  });

  factory BotSpectra.fromJson(Json j) {
    double v(String k, double d) => (j[k] as num?)?.toDouble() ?? d;
    return BotSpectra(
      meaning: v('meaning', 0.5),
      shape: v('shape', 0.25),
      color: v('color', 0.25),
      negative: v('negative', 0.5),
      memory: v('memory', 0.3),
      risk: v('risk', 0.5),
      compromise: v('compromise', 0.5),
      variability: v('variability', 0.2),
      strictness: v('strictness', 0.5),
      details: v('details', 0.25),
    );
  }

  final double meaning;
  final double shape;
  final double color;
  final double negative;
  final double memory;
  final double risk;
  final double compromise;
  final double variability;

  /// Строгость ассоциаций: 0 — одним письмом проверяет всё связанное, 1 — ровно одну карту.
  final double strictness;
  final double details;

  BotSpectra copyWith({
    double? meaning,
    double? shape,
    double? color,
    double? negative,
    double? memory,
    double? risk,
    double? compromise,
    double? variability,
    double? strictness,
    double? details,
  }) =>
      BotSpectra(
        meaning: meaning ?? this.meaning,
        shape: shape ?? this.shape,
        color: color ?? this.color,
        negative: negative ?? this.negative,
        memory: memory ?? this.memory,
        risk: risk ?? this.risk,
        compromise: compromise ?? this.compromise,
        variability: variability ?? this.variability,
        strictness: strictness ?? this.strictness,
        details: details ?? this.details,
      );

  /// Доли внимания в процентах (сумма 100).
  (int, int, int) get attentionPercent {
    final sum = meaning + shape + color;
    if (sum <= 0) return (34, 33, 33);
    final m = (meaning / sum * 100).round();
    final s = (shape / sum * 100).round();
    return (m, s, 100 - m - s);
  }

  Json toJson() => {
        'meaning': meaning,
        'shape': shape,
        'color': color,
        'negative': negative,
        'memory': memory,
        'risk': risk,
        'compromise': compromise,
        'strictness': strictness,
        'details': details,
        'variability': variability,
      };
}

/// Бот в кабинете админа.
class BotInfo {
  const BotInfo({
    required this.id,
    required this.nickname,
    required this.avatarColor,
    this.avatarId,
    required this.about,
    required this.spectra,
    required this.enabled,
    this.games = 0,
    this.wins = 0,
    this.rating = 1000,
  });

  factory BotInfo.fromJson(Json j) => BotInfo(
        id: j['id'] as String,
        nickname: j['nickname'] as String,
        avatarColor: j['avatarColor'] as String,
        avatarId: j['avatarId'] as String?,
        about: j['about'] as String? ?? '',
        spectra: BotSpectra.fromJson(Map<String, dynamic>.from(j['spectra'] as Map)),
        enabled: j['enabled'] as bool? ?? true,
        games: (j['games'] as num?)?.toInt() ?? 0,
        wins: (j['wins'] as num?)?.toInt() ?? 0,
        rating: (j['rating'] as num?)?.toInt() ?? 1000,
      );

  final String id;
  final String nickname;
  final String avatarColor;
  final String? avatarId;
  final String about;
  final BotSpectra spectra;
  final bool enabled;
  final int games;
  final int wins;
  final int rating;
}

/// Бот для выбора в лобби.
class BotCard {
  const BotCard({required this.id, required this.nickname, required this.avatarColor, this.avatarId, this.about = ''});

  factory BotCard.fromJson(Json j) => BotCard(
        id: j['id'] as String,
        nickname: j['nickname'] as String,
        avatarColor: j['avatarColor'] as String,
        avatarId: j['avatarId'] as String?,
        about: j['about'] as String? ?? '',
      );

  final String id;
  final String nickname;
  final String avatarColor;
  final String? avatarId;
  final String about;
}
