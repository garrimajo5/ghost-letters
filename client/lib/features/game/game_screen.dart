import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import '../lobby/settings_sheet.dart';
import 'action_panel.dart';
import 'game_sheets.dart';
import 'game_state.dart';

/// Экран партии: шапка, игроки, поле, подсказки по раундам, рука и главная кнопка хода.
class GameScreen extends ConsumerStatefulWidget {
  const GameScreen({super.key, required this.gameId});

  final String gameId;

  @override
  ConsumerState<GameScreen> createState() => GameScreenState();
}

class GameScreenState extends ConsumerState<GameScreen> {
  GameSnapshot? _snap;
  String? _error;
  final _subs = <StreamSubscription<Object?>>[];
  late final Realtime _realtime;
  late final AppLifecycleListener _lifecycle;
  final _scroll = ScrollController();
  final _panelKey = GlobalKey();

  /// Лобби партии — чтобы хост мог менять раунды, темп и таймеры прямо в игре.
  Lobby? lobby;

  /// Выбор игрока на текущем шаге: карты руки, улики ночи, письма в ящике, голос, цель.
  final selectedHand = <String>{};
  final selectedMailbox = <String>{};
  final truth = <int, int>{};
  int? voteColumn;
  String? target;
  final marks = <String, CardMark>{};

  /// Мои подозрения из заметок: id игрока → от −2 (точно чист) до 2 (это он!).
  final suspicion = <String, int>{};
  final chat = <ChatMessage>[];
  int unread = 0;

  /// Сколько раз экран сигналил «ваш ход» — для вспышки кнопки.
  int turnPulse = 0;
  bool _wasMyTurn = false;

  GameView? get view => _snap?.view;

  List<RosterEntry> get roster => _snap?.roster ?? const [];

  RosterEntry? rosterOf(String? id) {
    for (final r in roster) {
      if (r.id == id) return r;
    }
    return null;
  }

  String nick(String? id) => rosterOf(id)?.nickname ?? '?';

  String colorOf(String? id) => rosterOf(id)?.avatarColor ?? '#3D6A99';

  String? get lobbyId => _snap?.lobbyId;

  void openLobby(String id) => context.go('/lobby/$id');

  @override
  void initState() {
    super.initState();
    _realtime = ref.read(realtimeProvider);
    _subs.add(_realtime.views.listen((u) {
      if (u.view.gameId != widget.gameId || !mounted) return;
      final current = _snap;
      if (current == null || u.view.version < current.view.version) return;
      setState(() {
        if (u.view.phase != current.view.phase || u.view.round != current.view.round) _resetSelection();
        _snap = current.withView(u.view, u.deadline);
      });
    }));
    _subs.add(_realtime.chat.listen((m) {
      if (!mounted || chat.any((c) => c.id == m.id)) return;
      setState(() {
        chat.add(m);
        unread++;
      });
    }));
    _subs.add(_realtime.lobbyUpdates.listen((l) {
      if (mounted && l.id == lobbyId) setState(() => lobby = l);
    }));
    // Вернулись из фона: связь могла прерваться — подписываемся заново и перечитываем партию.
    _lifecycle = AppLifecycleListener(onResume: _resync);
    _load();
  }

  @override
  void setState(VoidCallback fn) {
    super.setState(fn);
    _checkTurn();
  }

  /// Ход перешёл к игроку: вибрация, вспышка кнопки и прокрутка к панели действий.
  void _checkTurn() {
    final v = view;
    final mine = v != null && needsMe(v);
    if (mine && !_wasMyTurn) {
      turnPulse++;
      HapticFeedback.mediumImpact();
      if (ActionPanel.cta(this) == null) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          final ctx = _panelKey.currentContext;
          if (ctx != null && ctx.mounted) {
            Scrollable.ensureVisible(ctx, duration: const Duration(milliseconds: 350), alignmentPolicy: ScrollPositionAlignmentPolicy.keepVisibleAtEnd);
          }
        });
      }
    }
    _wasMyTurn = mine;
  }

  Future<void> _resync() async {
    try {
      await _realtime.resync();
      final fresh = await ref.read(apiProvider).snapshot(widget.gameId);
      if (mounted && fresh.view.version >= (view?.version ?? 0)) setState(() => _snap = fresh);
    } catch (_) {
      // Не вышло — обновление придёт, когда хаб переподключится.
    }
  }

  Future<void> _load() async {
    try {
      final snap = await _realtime.subscribeGame(widget.gameId);
      if (!mounted) return;
      // Снимок — сразу, чтобы обновления по хабу, пришедшие во время загрузки чата, не терялись.
      final current = _snap;
      setState(() => _snap = current != null && current.view.version > snap.view.version ? current : snap);

      final api = ref.read(apiProvider);
      final history = await api.chat(widget.gameId);
      final savedMarks = snap.view.me == null ? const <Json>[] : await api.marks(widget.gameId);
      final notes = snap.view.me == null ? const <Json>[] : await api.notes(widget.gameId).catchError((_) => const <Json>[]);
      if (!mounted) return;
      setState(() {
        final known = {for (final m in history) m.id};
        final live = chat.where((m) => !known.contains(m.id)).toList();
        chat
          ..clear()
          ..addAll(history)
          ..addAll(live);
        marks
          ..clear()
          ..addEntries(savedMarks.map((m) => MapEntry(m['cardId'] as String, CardMark.fromJson(m))));
        suspicion
          ..clear()
          ..addEntries(notes.map((n) => MapEntry(n['targetUserId'] as String, ((n['suspicion'] as num?) ?? 0).toInt())));
      });

      final lobbyId = snap.lobbyId;
      if (lobbyId != null && snap.view.me != null) {
        try {
          final l = await _realtime.subscribeLobby(lobbyId);
          if (mounted) setState(() => lobby = l);
        } catch (_) {
          // Лобби нужно только для настроек хоста.
        }
      }
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    }
  }

  void _resetSelection() {
    selectedHand.clear();
    selectedMailbox.clear();
    truth.clear();
    voteColumn = null;
    target = null;
  }

  @override
  void dispose() {
    for (final s in _subs) {
      s.cancel();
    }
    _lifecycle.dispose();
    _scroll.dispose();
    _realtime.forgetGame(widget.gameId);
    final l = lobby;
    if (l != null) _realtime.unsubscribeLobby(l.id);
    super.dispose();
  }

  /// Отправить команду. Новое состояние придёт по SignalR; если нет — перечитаем снимок.
  /// Если партия успела измениться (VERSION_CONFLICT), перечитываем её и, если ход ещё нужен, повторяем.
  Future<void> send(String type, [Json payload = const {}]) async {
    final v = view;
    if (v == null) return;
    final api = ref.read(apiProvider);
    try {
      await api.command(widget.gameId, type, payload, v.version);
    } on ApiError catch (e) {
      if (e.code != 'VERSION_CONFLICT') {
        _snack(e.message);
        return;
      }

      final fresh = await runAction(context, () => api.snapshot(widget.gameId));
      if (fresh == null || !mounted) return;
      setState(() => _snap = fresh);
      if (!fresh.view.can(type)) return;
      try {
        await api.command(widget.gameId, type, payload, fresh.view.version);
      } on ApiError catch (e2) {
        _snack(e2.message);
        return;
      }
    }

    if (!mounted) return;
    setState(_resetSelection);
    final fresh = await runAction(context, () => api.snapshot(widget.gameId));
    if (fresh != null && mounted && fresh.view.version >= (view?.version ?? 0)) setState(() => _snap = fresh);
  }

  void _snack(String text) {
    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
  }

  bool get isHost => lobby != null && lobby!.hostUserId == view?.me?.id;

  Future<void> editSettings() async {
    final l = lobby;
    if (l == null) return;
    final s = await SettingsSheet.show(context, l.settings, inGame: true, players: view?.players.length);
    if (s == null || !mounted) return;
    final updated = await runAction(context, () => ref.read(apiProvider).saveSettings(l.id, s));
    if (updated != null && mounted) setState(() => lobby = updated);
  }

  void refresh() => setState(() {});

  void setSuspicion(String userId, int value) {
    if (mounted) setState(() => suspicion[userId] = value);
  }

  void openChat() {
    setState(() => unread = 0);
    ChatSheet.show(context, this);
  }

  Future<void> saveMark(String cardId, CardMark mark) async {
    setState(() {
      if (mark.isEmpty) {
        marks.remove(cardId);
      } else {
        marks[cardId] = mark;
      }
    });
    await runAction(
      context,
      () => ref.read(apiProvider).saveMarks(widget.gameId, [for (final e in marks.entries) e.value.toJson(e.key)]),
    );
  }

  /// Ряд, где сейчас выбирают карту: истина ночью, назвать улики, голосование по ряду.
  bool get choosingTruth => view != null && (view!.can('ChooseTruth') || view!.can('NameTruth'));

  void tapCard(int row, int column, String cardId) {
    final v = view!;
    final stage = v.finale?.currentStage;
    if (choosingTruth) {
      setState(() => truth[row] = column);
    } else if (v.can('CastVote') && stage != null && stage.isRow && stage.row == row) {
      // В ряду голосования выбираются только кандидаты; остальные карты не реагируют.
      if (stage.candidateColumns.contains(column)) setState(() => voteColumn = column);
    } else if (v.me != null) {
      MarkSheet.show(context, this, cardId);
    }
  }

  /// Можно выбрать игрока целью: голос за Убийцу, охота, «дать слово», выдвижение.
  bool get pickingPlayer {
    final v = view!;
    return v.can('HuntPick') || v.can('BlackmailerPick') || v.can('GiveFloor') ||
        (v.can('CastVote') && v.finale?.currentStage?.isRow == false);
  }

  void tapPlayer(String id) {
    final v = view!;
    if (pickingPlayer) {
      setState(() => target = target == id ? null : id);
    } else if (v.me != null && id != v.me!.id) {
      NoteSheet.show(context, this, id);
    }
  }

  @override
  Widget build(BuildContext context) {
    final snap = _snap;
    if (snap == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('ПАРТИЯ')),
        body: Center(
          child: _error == null
              ? const CircularProgressIndicator()
              : Column(mainAxisSize: MainAxisSize.min, children: [
                  Padding(padding: const EdgeInsets.all(16), child: Text(_error!, textAlign: TextAlign.center)),
                  FilledButton(
                    onPressed: () {
                      setState(() => _error = null);
                      _load();
                    },
                    child: const Text('Повторить'),
                  ),
                  TextButton(onPressed: () => context.go('/'), child: const Text('На главную')),
                ]),
        ),
      );
    }

    final v = snap.view;
    final night = v.phase == 'Night' && v.can('ChooseTruth');
    final finale = isFinale(v);
    final panelFirst = v.can('RevealHints') || const {'VoteTie', 'AwardNomination', 'AwardVoting', 'Finished'}.contains(v.phase);
    return Scaffold(
      backgroundColor: night ? AppColors.night : AppColors.bg,
      body: SafeArea(
        child: Column(children: [
          _Header(screen: this, deadline: snap.deadline),
          _PlayersStrip(screen: this),
          if (v.phase == 'RoleReveal' && v.me != null)
            Expanded(child: _RoleScreen(screen: this))
          else if (v.me == null && MediaQuery.sizeOf(context).width >= 720)
            // Экран стола на планшете или ТВ: поле слева, ход партии и подсказки справа.
            Expanded(
              child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Expanded(
                  child: ListView(padding: const EdgeInsets.fromLTRB(16, 4, 8, 16), children: [
                    _Board(screen: this, maxCard: 150),
                  ]),
                ),
                SizedBox(
                  width: 360,
                  child: ListView(padding: const EdgeInsets.fromLTRB(8, 4, 16, 16), children: [
                    ActionPanel(screen: this),
                    const SizedBox(height: 10),
                    _Hints(view: v),
                  ]),
                ),
              ]),
            )
          else
          Expanded(
            child: ListView(
              controller: _scroll,
              padding: const EdgeInsets.fromLTRB(12, 4, 12, 16),
              children: [
                // Когда главное действие — в панели (письма Призрака, выбор игрока, итоги), панель идёт первой.
                if (panelFirst) ...[
                  KeyedSubtree(key: _panelKey, child: ActionPanel(screen: this)),
                  const SizedBox(height: 10),
                ],
                if (night) const _NightBanner(),
                _Board(screen: this),
                const SizedBox(height: 10),
                _Hints(view: v),
                if (v.me != null && v.me!.letters.isNotEmpty && !finale) ...[
                  const SizedBox(height: 10),
                  _MyLetters(me: v.me!),
                ],
                if (!panelFirst) ...[
                  const SizedBox(height: 10),
                  KeyedSubtree(key: _panelKey, child: ActionPanel(screen: this)),
                ],
              ],
            ),
          ),
          _Dock(screen: this),
        ]),
      ),
    );
  }
}

/// Раздача ролей: большая карта рубашкой вверх, по нажатию — роль, подсказка и кто Призрак.
class _RoleScreen extends StatelessWidget {
  const _RoleScreen({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final me = v.me!;
    final ghost = v.players.where((p) => p.isGhost && p.id != me.id).firstOrNull;
    final team = v.players.where((p) => p.id != me.id && isKillerTeam(p.knownRole)).toList();
    Widget pill(String id, String text) => Container(
          padding: const EdgeInsets.fromLTRB(6, 6, 14, 6),
          decoration: BoxDecoration(color: AppColors.surface, borderRadius: BorderRadius.circular(99)),
          child: Row(mainAxisSize: MainAxisSize.min, children: [
            Avatar(nickname: screen.nick(id), color: screen.colorOf(id), size: 32),
            const SizedBox(width: 10),
            Flexible(child: Text(text, style: const TextStyle(fontSize: 14))),
          ]),
        );
    return LayoutBuilder(
      builder: (context, box) => SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(24, 12, 24, 16),
        child: Column(children: [
          const Text('РАЗДАЧА РОЛЕЙ', style: TextStyle(fontSize: 13, color: AppColors.muted, letterSpacing: 2)),
          const SizedBox(height: 16),
          RoleReveal(
            role: me.role,
            width: (box.maxHeight * 0.5 / 1.42).clamp(140.0, 260.0),
            footer: Column(children: [
              if (ghost != null) pill(ghost.id, 'Призрак — ${screen.nick(ghost.id)}. Видит истинные улики.'),
              for (final p in team) ...[
                const SizedBox(height: 8),
                pill(p.id, '${T.role(p.knownRole)} — ${screen.nick(p.id)}'),
              ],
            ]),
          ),
        ]),
      ),
    );
  }
}

/// Ночь для Убийцы: тёмная иллюстрация и красный заголовок.
class _NightBanner extends StatelessWidget {
  const _NightBanner();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(14),
          child: SizedBox(
            height: 120,
            child: Stack(fit: StackFit.expand, children: [
              const Opacity(opacity: 0.55, child: AppImage('night', fit: BoxFit.cover)),
              const DecoratedBox(
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topCenter,
                    end: Alignment.bottomCenter,
                    colors: [Colors.transparent, AppColors.night],
                  ),
                ),
              ),
              Padding(
                padding: const EdgeInsets.all(14),
                child: Column(mainAxisAlignment: MainAxisAlignment.end, crossAxisAlignment: CrossAxisAlignment.start, children: [
                  const Text('НОЧЬ · ВЫ — УБИЙЦА', style: TextStyle(fontSize: 12, color: AppColors.redSoft, letterSpacing: 2)),
                  const SizedBox(height: 4),
                  Text('ВЫБЕРИТЕ ИСТИННЫЕ УЛИКИ', style: heading(22, spacing: 1)),
                ]),
              ),
            ]),
          ),
        ),
      );
}

/// Шапка: «РАУНД 2 / 4», фаза (или «ВАШ ХОД»), таймер и меню.
class _Header extends StatelessWidget {
  const _Header({required this.screen, required this.deadline});

  final GameScreenState screen;
  final DateTime? deadline;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final mine = needsMe(v);
    final acted = v.players.where((p) => p.hasActed).length;
    final counted = const {'Mailbox', 'Refill', 'Voting', 'VoteTie', 'RoleReveal'}.contains(v.phase);
    final killerNight = v.phase == 'Night' && v.can('ChooseTruth');
    final pillColor = killerNight ? AppColors.red : (mine ? AppColors.amber : AppColors.surface2);
    final pillText = killerNight ? AppColors.text : (mine ? AppColors.onAmber : AppColors.text);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 4, 4),
      child: Row(children: [
        if (v.round > 0)
          Text.rich(
            key: const Key('round'),
            TextSpan(children: [
              TextSpan(text: 'РАУНД ${v.round}'),
              TextSpan(text: ' / ${v.totalRounds}', style: const TextStyle(color: AppColors.dim)),
            ]),
            style: heading(18, spacing: 1),
          )
        else
          Text('ПАРТИЯ', style: heading(18, spacing: 1)),
        const SizedBox(width: 8),
        Expanded(
          child: Center(
            child: AnimatedContainer(
              key: const Key('phase-pill'),
              duration: const Duration(milliseconds: 250),
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
              decoration: BoxDecoration(color: pillColor, borderRadius: BorderRadius.circular(99)),
              child: Text(
                mine
                    ? 'Ваш ход · ${T.shortPhase(v.phase)}'
                    : '${T.shortPhase(v.phase)}${counted && v.players.isNotEmpty ? ' · $acted из ${v.players.length}' : ''}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 13, color: pillText, fontWeight: mine ? FontWeight.w600 : FontWeight.w400),
              ),
            ),
          ),
        ),
        const SizedBox(width: 8),
        Countdown(deadline: deadline),
        PopupMenuButton<String>(
          tooltip: 'Меню партии',
          icon: const Icon(Icons.more_horiz, color: AppColors.muted),
          onSelected: (value) {
            switch (value) {
              case 'home':
                context.go('/');
              case 'settings':
                screen.editSettings();
            }
          },
          itemBuilder: (_) => [
            if (screen.isHost) const PopupMenuItem(value: 'settings', child: Text('Раунды, темп и таймеры')),
            const PopupMenuItem(value: 'home', child: Text('На главную')),
          ],
        ),
      ]),
    );
  }
}

class _PlayersStrip extends StatelessWidget {
  const _PlayersStrip({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final players = [...v.players]..sort((a, b) => a.seat.compareTo(b.seat));
    return SizedBox(
      height: 84,
      child: LayoutBuilder(builder: (context, box) {
        // Помещаются все — раскладываем по ширине, иначе листаем.
        final itemWidth = (box.maxWidth - 16) / players.length >= 50 ? (box.maxWidth - 16) / players.length : 54.0;
        return ListView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: 8),
          children: [
            for (final p in players)
              SizedBox(
                width: itemWidth,
                child: _PlayerChip(screen: screen, player: p),
              ),
          ],
        );
      }),
    );
  }
}

class _PlayerChip extends StatelessWidget {
  const _PlayerChip({required this.screen, required this.player});

  final GameScreenState screen;
  final PlayerInfo player;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final p = player;
    final isMe = p.id == v.me?.id;
    final ghost = p.isGhost || p.knownRole == 'Ghost';
    final selected = screen.target == p.id;
    final speaking = v.currentSpeaker == p.id;
    final arrested = v.finale?.arrested.contains(p.id) == true;
    final name = isMe ? 'Вы' : (ghost ? 'Призрак' : screen.nick(p.id));
    final nameColor = isMe ? AppColors.amber : (ghost ? AppColors.ice : AppColors.muted);
    final suspicionRole = !isMe && isKillerTeam(p.knownRole);

    return GestureDetector(
      key: Key('player-${p.id}'),
      behavior: HitTestBehavior.opaque,
      onTap: () => screen.tapPlayer(p.id),
      child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
        Stack(clipBehavior: Clip.none, children: [
          AnimatedContainer(
            duration: const Duration(milliseconds: 200),
            padding: const EdgeInsets.all(2),
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              boxShadow: selected ? [BoxShadow(color: AppColors.amber.withValues(alpha: 0.6), blurRadius: 10, spreadRadius: 1)] : null,
              border: Border.all(
                color: selected ? AppColors.amber : (speaking ? AppColors.ice : Colors.transparent),
                width: 2,
              ),
            ),
            child: Opacity(
              opacity: arrested ? 0.5 : 1,
              child: Avatar(
                nickname: screen.nick(p.id),
                color: screen.colorOf(p.id),
                size: 40,
                ring: isMe ? AppColors.amber : (ghost ? AppColors.ice : null),
              ),
            ),
          ),
          if (speaking || v.radioHolder == p.id)
            const Positioned(top: -4, right: -4, child: _Radio()),
          if (p.hasActed)
            Positioned(
              bottom: -2,
              right: -2,
              child: Container(
                width: 16,
                height: 16,
                decoration: BoxDecoration(color: AppColors.ice, shape: BoxShape.circle, border: Border.all(color: AppColors.bg, width: 1.5)),
                alignment: Alignment.center,
                child: const Icon(Icons.check, size: 10, color: AppColors.bg),
              ),
            ),
          if (suspicionRole)
            Positioned(top: -4, left: -6, child: CountBadge(text: p.knownRole == 'Killer' ? 'У' : 'С', color: AppColors.red))
          else if (!isMe && (screen.suspicion[p.id] ?? 0) > 0)
            Positioned(
              top: -4,
              left: -6,
              child: CountBadge(key: Key('suspect-${p.id}'), text: screen.suspicion[p.id]! >= 2 ? 'У!' : 'У?', color: AppColors.red),
            ),
          if (v.raisedHands.contains(p.id))
            const Positioned(left: -6, bottom: -2, child: Icon(Icons.pan_tool, size: 15, color: AppColors.amber)),
          if (arrested)
            const Positioned(left: -4, bottom: -2, child: Icon(Icons.lock, size: 15, color: AppColors.redBright)),
        ]),
        const SizedBox(height: 3),
        Text(
          name,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(fontSize: 11, color: selected ? AppColors.amber : nameColor, fontWeight: selected ? FontWeight.w600 : null),
        ),
        if (p.knownRole != null && !ghost && !isMe)
          Text(T.role(p.knownRole), maxLines: 1, style: TextStyle(fontSize: 9, color: suspicionRole ? AppColors.redSoft : AppColors.dim)),
      ]),
    );
  }
}

class _Radio extends StatelessWidget {
  const _Radio();

  @override
  Widget build(BuildContext context) => Container(
        width: 20,
        height: 20,
        decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: AppColors.bg, width: 2)),
        child: const AppImage('radio', circle: true, width: 16, height: 16),
      );
}

/// Поле улик: жетоны категорий слева, номера столбцов сверху. Карты всегда умещаются по ширине.
class _Board extends StatelessWidget {
  const _Board({required this.screen, this.maxCard = 96});

  final GameScreenState screen;
  final double maxCard;

  static const gap = 5.0;
  static const labelWidth = 46.0;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final columns = v.board.isEmpty ? 5 : v.board.first.cards.length;
    final stage = v.finale?.currentStage;
    final outcomes = v.finale?.outcomes ?? const <VoteOutcome>[];
    final killerNight = v.phase == 'Night' && v.can('ChooseTruth');

    return LayoutBuilder(builder: (context, box) {
      // Карта = (ширина − колонка жетонов − промежутки) / столбцы, но не больше 96.
      final size = ((box.maxWidth - labelWidth - gap * columns) / columns).clamp(24.0, maxCard).floorToDouble();
      return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          const SizedBox(width: labelWidth),
          for (var c = 0; c < columns; c++)
            Container(
              width: size,
              margin: const EdgeInsets.only(left: gap),
              alignment: Alignment.center,
              child: Text('${c + 1}', style: heading(11, color: AppColors.dim, spacing: 0)),
            ),
        ]),
        const SizedBox(height: 4),
        for (var r = 0; r < v.board.length; r++)
          Padding(
            padding: const EdgeInsets.only(bottom: gap),
            child: Row(children: [
              SizedBox(
                width: labelWidth,
                child: Column(mainAxisSize: MainAxisSize.min, children: [
                  AppImage(categoryToken(v.board[r].category), width: 34, height: 34, circle: true),
                  const SizedBox(height: 1),
                  Text(
                    T.category(v.board[r].category).toUpperCase(),
                    maxLines: 1,
                    overflow: TextOverflow.fade,
                    softWrap: false,
                    style: heading(8.5, color: AppColors.ice, spacing: 0.5),
                  ),
                ]),
              ),
              for (var c = 0; c < v.board[r].cards.length; c++)
                Padding(
                  padding: const EdgeInsets.only(left: gap),
                  child: GestureDetector(
                    key: Key('board-$r-$c'),
                    onTap: () => screen.tapCard(r, c, v.board[r].cards[c]),
                    onLongPress: v.me == null
                        ? null
                        : () {
                            final id = v.board[r].cards[c];
                            final m = screen.marks[id] ?? const CardMark();
                            HapticFeedback.selectionClick();
                            screen.saveMark(id, m.copyWith(believed: !m.believed));
                          },
                    child: BoardCard(
                      cardId: v.board[r].cards[c],
                      size: size,
                      mark: screen.marks[v.board[r].cards[c]],
                      isTruth: v.truth != null && v.truth!.length > r && v.truth![r] == c,
                      chosen: screen.truth[r] == c ||
                          (stage != null && stage.isRow && stage.row == r && (screen.voteColumn ?? v.finale?.myVoteColumn) == c),
                      chosenColor: killerNight ? AppColors.redBright : AppColors.amber,
                      voted: outcomes.any((o) => o.kind == 'Row' && o.row == r && o.column == c),
                      dimmed: stage != null && stage.isRow && stage.row == r && !stage.candidateColumns.contains(c),
                      active: stage != null && stage.isRow && stage.row == r && stage.candidateColumns.contains(c),
                    ),
                  ),
                ),
            ]),
          ),
      ]);
    });
  }
}

/// Карта поля с пометками: ✕ — красный бейдж слева, ✓ — зелёный справа, «считаю истинной» — зелёная рамка.
class BoardCard extends StatelessWidget {
  const BoardCard({
    super.key,
    required this.cardId,
    required this.size,
    required this.mark,
    this.isTruth = false,
    this.chosen = false,
    this.chosenColor = AppColors.amber,
    this.voted = false,
    this.dimmed = false,
    this.active = false,
  });

  final String cardId;
  final double size;
  final CardMark? mark;
  final bool isTruth;
  final bool chosen;
  final Color chosenColor;
  final bool voted;
  final bool dimmed;
  final bool active;

  @override
  Widget build(BuildContext context) {
    final m = mark;
    final believed = m?.believed == true;
    final (Color borderColor, double borderWidth) = chosen
        ? (chosenColor, 3)
        : believed
            ? (AppColors.believed, 3)
            : isTruth
                ? (AppColors.ice, 2.5)
                : active
                    ? (AppColors.amber.withValues(alpha: 0.6), 1.5)
                    : (AppColors.border, 1);
    final crossedOut = (m?.crosses ?? 0) >= 3 && !believed;
    final radius = (size * 0.16).clamp(4.0, 12.0);
    final badge = size >= 44 ? 10.0 : 8.0;

    return Opacity(
      opacity: dimmed ? 0.3 : (crossedOut ? 0.55 : 1),
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(radius),
          boxShadow: chosen || believed
              ? [BoxShadow(color: (chosen ? chosenColor : AppColors.believed).withValues(alpha: 0.35), blurRadius: 0, spreadRadius: 2)]
              : null,
        ),
        // Рамка рисуется поверх картинки и не увеличивает карту — поле не вылезает за экран.
        foregroundDecoration: BoxDecoration(
          borderRadius: BorderRadius.circular(radius),
          border: Border.all(color: borderColor, width: borderWidth),
        ),
        child: Stack(children: [
          CardImage(cardId: cardId, size: size, radius: radius),
          if (m != null && m.crosses > 0)
            Positioned(left: 2, top: 2, child: CountBadge(key: Key('x-$cardId'), icon: Icons.close, text: '${m.crosses}', color: AppColors.red, fontSize: badge)),
          if (m != null && m.checks > 0)
            Positioned(right: 2, top: 2, child: CountBadge(key: Key('v-$cardId'), icon: Icons.check, text: '${m.checks}', color: AppColors.green, fontSize: badge)),
          if (voted)
            Positioned(
              left: 2,
              bottom: 2,
              child: Container(
                padding: const EdgeInsets.all(2),
                decoration: const BoxDecoration(color: AppColors.amber, shape: BoxShape.circle),
                child: Icon(Icons.how_to_vote, size: size * 0.18, color: AppColors.onAmber),
              ),
            ),
        ]),
      ),
    );
  }
}

/// Подсказки Призрака по раундам и жетон «исчезло ×N».
class _Hints extends StatelessWidget {
  const _Hints({required this.view});

  final GameView view;

  @override
  Widget build(BuildContext context) {
    if (view.hints.isEmpty && view.vanishedCount == 0) return const SizedBox.shrink();
    const size = 40.0;
    return Panel(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
      child: IntrinsicHeight(
        child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('ПОДСКАЗКИ ПО РАУНДАМ', style: sectionLabel()),
              const SizedBox(height: 6),
              SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  for (final h in view.hints)
                    Padding(
                      padding: const EdgeInsets.only(right: 14),
                      child: Column(children: [
                        Text(h.round == 0 ? 'зацепка' : 'р. ${h.round}', style: const TextStyle(fontSize: 10, color: AppColors.muted)),
                        const SizedBox(height: 4),
                        if (h.cards.isEmpty)
                          Container(
                            width: size,
                            height: size,
                            decoration: BoxDecoration(
                              borderRadius: BorderRadius.circular(8),
                              border: Border.all(color: AppColors.border),
                            ),
                            alignment: Alignment.center,
                            child: const Text('пусто', style: TextStyle(fontSize: 9, color: AppColors.dim)),
                          )
                        else
                          for (final c in h.cards)
                            Padding(padding: const EdgeInsets.only(bottom: 4), child: CardImage(cardId: c, size: size, radius: 8)),
                      ]),
                    ),
                ]),
              ),
            ]),
          ),
          if (view.vanishedCount > 0)
            Container(
              width: 70,
              margin: const EdgeInsets.only(left: 8),
              padding: const EdgeInsets.only(left: 10),
              decoration: const BoxDecoration(border: Border(left: BorderSide(color: AppColors.border))),
              child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                const AppImage('vanished', width: 36, height: 36, circle: true),
                Text('× ${view.vanishedCount}', style: heading(16, spacing: 0.5)),
                Text('исчезло писем: ${view.vanishedCount}', textAlign: TextAlign.center, style: const TextStyle(fontSize: 9, color: AppColors.muted)),
              ]),
            ),
        ]),
      ),
    );
  }
}

/// Нижняя панель: рука и главная кнопка хода + чат. Когда ждут игрока — янтарная кнопка со вспышкой.
class _Dock extends StatelessWidget {
  const _Dock({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final me = v.me;
    final cta = ActionPanel.cta(screen);
    final mine = needsMe(v);
    final lastLetter = me == null || me.letters.isEmpty ? null : me.letters.last;

    return Container(
      decoration: const BoxDecoration(
        color: AppColors.bg,
        border: Border(top: BorderSide(color: AppColors.surface2)),
      ),
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 10),
      child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (me != null && me.hand.isNotEmpty && v.phase != 'RoleReveal' && !isFinale(v)) ...[
          Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
            Text('ВАША РУКА', style: sectionLabel(size: 12)),
            const SizedBox(width: 8),
            Text(T.role(me.role), style: TextStyle(fontSize: 11, color: isKillerTeam(me.role) ? AppColors.redSoft : AppColors.muted)),
            const Spacer(),
            if (lastLetter != null)
              Flexible(
                child: Text(
                  'раунд ${lastLetter.round}: ${lastLetter.revealed == null ? 'письмо в ящике' : (lastLetter.revealed! ? 'ваше письмо открыто' : 'ваше письмо исчезло')}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontSize: 11, color: AppColors.muted),
                ),
              ),
          ]),
          const SizedBox(height: 6),
          _Hand(screen: screen),
          const SizedBox(height: 8),
        ],
        Row(children: [
          Expanded(
            child: cta != null
                ? _Pulse(
                    pulse: screen.turnPulse,
                    child: FilledButton.icon(
                      key: const Key('cta'),
                      style: cta.danger ? FilledButton.styleFrom(backgroundColor: AppColors.red, foregroundColor: Colors.white) : null,
                      onPressed: cta.onPressed,
                      icon: Icon(cta.icon ?? Icons.arrow_forward, size: 20),
                      label: Text(cta.label.toUpperCase(), maxLines: 1, overflow: TextOverflow.ellipsis),
                    ),
                  )
                : _StatusBar(text: actionHint(v), mine: mine, pulse: screen.turnPulse),
          ),
          const SizedBox(width: 10),
          _ChatButton(screen: screen),
        ]),
      ]),
    );
  }
}

/// Строка состояния вместо кнопки: «Ждём других игроков» или «Ваш ход ↑» (действие — в панели выше).
class _StatusBar extends StatelessWidget {
  const _StatusBar({required this.text, required this.mine, required this.pulse});

  final String text;
  final bool mine;
  final int pulse;

  @override
  Widget build(BuildContext context) {
    final bar = Container(
      key: const Key('status-bar'),
      height: 52,
      padding: const EdgeInsets.symmetric(horizontal: 14),
      decoration: BoxDecoration(
        color: mine ? const Color(0xFF3A2B12) : AppColors.surface,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: mine ? AppColors.amber : AppColors.border),
      ),
      child: Row(children: [
        Icon(mine ? Icons.touch_app : Icons.hourglass_empty, size: 20, color: mine ? AppColors.amber : AppColors.muted),
        const SizedBox(width: 10),
        Expanded(
          child: Text(
            mine ? 'Ваш ход: $text' : text,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 13, color: mine ? AppColors.amberLight : AppColors.muted, fontWeight: mine ? FontWeight.w600 : null),
          ),
        ),
        if (mine) const Icon(Icons.keyboard_arrow_up, color: AppColors.amber),
      ]),
    );
    return mine ? _Pulse(pulse: pulse, child: bar) : bar;
  }
}

/// Короткая вспышка, когда ход переходит к игроку (одна, без бесконечной анимации).
class _Pulse extends StatelessWidget {
  const _Pulse({required this.pulse, required this.child});

  final int pulse;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    if (pulse == 0) return child;
    return TweenAnimationBuilder<double>(
      key: ValueKey(pulse),
      tween: Tween(begin: 1, end: 0),
      duration: const Duration(milliseconds: 1400),
      builder: (context, t, child) {
        // Две волны свечения за время анимации.
        final glow = (t * 2 % 1) * t;
        return DecoratedBox(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(14),
            boxShadow: [BoxShadow(color: AppColors.amber.withValues(alpha: 0.7 * glow), blurRadius: 18 * glow, spreadRadius: 4 * glow)],
          ),
          child: child,
        );
      },
      child: child,
    );
  }
}

class _ChatButton extends StatelessWidget {
  const _ChatButton({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: 'Чат',
      child: InkWell(
        onTap: screen.openChat,
        borderRadius: BorderRadius.circular(14),
        child: Container(
          width: 52,
          height: 52,
          decoration: BoxDecoration(
            color: AppColors.surface,
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: AppColors.border),
          ),
          child: Stack(alignment: Alignment.center, children: [
            const Icon(Icons.chat_bubble_outline, color: AppColors.ice),
            if (screen.unread > 0)
              Positioned(
                top: 5,
                right: 5,
                child: Container(
                  constraints: const BoxConstraints(minWidth: 18),
                  height: 18,
                  padding: const EdgeInsets.symmetric(horizontal: 4),
                  decoration: BoxDecoration(color: AppColors.amber, borderRadius: BorderRadius.circular(99)),
                  alignment: Alignment.center,
                  child: Text('${screen.unread}', style: const TextStyle(fontSize: 11, color: AppColors.onAmber, fontWeight: FontWeight.w700)),
                ),
              ),
          ]),
        ),
      ),
    );
  }
}

class _Hand extends StatelessWidget {
  const _Hand({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final me = v.me!;
    final selectable = v.can('SendLetter') || v.can('Discard') || v.can('GiveFirstClue');
    final limit = v.can('SendLetter') ? lettersPerPlayer(v) : 1;
    return LayoutBuilder(builder: (context, box) {
      final size = ((box.maxWidth - 8 * (me.hand.length - 1)) / me.hand.length).clamp(40.0, 62.0).floorToDouble();
      return SizedBox(
        height: size + 8,
        child: ListView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.only(top: 6),
          children: [
            if ((size + 8) * me.hand.length < box.maxWidth) SizedBox(width: (box.maxWidth - (size + 8) * me.hand.length) / 2),
            for (final c in me.hand)
              GestureDetector(
                key: Key('hand-$c'),
                onTap: selectable
                    ? () {
                        HapticFeedback.selectionClick();
                        if (screen.selectedHand.contains(c)) {
                          screen.selectedHand.remove(c);
                        } else {
                          if (screen.selectedHand.length >= limit) screen.selectedHand.clear();
                          screen.selectedHand.add(c);
                        }
                        screen.refresh();
                      }
                    : null,
                child: AnimatedContainer(
                  duration: const Duration(milliseconds: 150),
                  margin: const EdgeInsets.symmetric(horizontal: 4),
                  transform: Matrix4.translationValues(0, screen.selectedHand.contains(c) ? -6 : 0, 0),
                  foregroundDecoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(10),
                    border: Border.all(
                      color: screen.selectedHand.contains(c) ? AppColors.amber : AppColors.border,
                      width: screen.selectedHand.contains(c) ? 3 : 1,
                    ),
                  ),
                  child: Opacity(opacity: selectable || !needsMe(v) ? 1 : 0.6, child: CardImage(cardId: c, size: size, radius: 10)),
                ),
              ),
          ],
        ),
      );
    });
  }
}

/// Мои письма по раундам: что отправил и открыл ли Призрак — чтобы не держать в голове.
class _MyLetters extends StatelessWidget {
  const _MyLetters({required this.me});

  final Me me;

  @override
  Widget build(BuildContext context) {
    return Panel(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('МОИ ПИСЬМА', style: sectionLabel()),
        const SizedBox(height: 6),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(children: [
            for (final l in me.letters)
              Padding(
                padding: const EdgeInsets.only(right: 12),
                child: Column(children: [
                  Text('р. ${l.round}', style: const TextStyle(fontSize: 10, color: AppColors.muted)),
                  const SizedBox(height: 4),
                  Opacity(opacity: l.revealed == false ? 0.4 : 1, child: CardImage(cardId: l.cardId, size: 40, radius: 8)),
                  const SizedBox(height: 2),
                  Icon(
                    l.revealed == null ? Icons.hourglass_empty : (l.revealed! ? Icons.visibility : Icons.visibility_off),
                    size: 14,
                    color: l.revealed == true ? AppColors.believed : AppColors.dim,
                  ),
                ]),
              ),
          ]),
        ),
      ]),
    );
  }
}
