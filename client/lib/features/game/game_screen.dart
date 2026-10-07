import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'action_panel.dart';
import 'game_sheets.dart';
import 'game_state.dart';

/// Экран партии: игроки, поле, подсказки по раундам, рука и панель действий текущей фазы.
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

  /// Выбор игрока на текущем шаге: карты руки, улики ночи, письма в ящике, голос, цель.
  final selectedHand = <String>{};
  final selectedMailbox = <String>{};
  final truth = <int, int>{};
  int? voteColumn;
  String? target;
  final marks = <String, CardMark>{};
  final chat = <ChatMessage>[];
  int unread = 0;

  GameView? get view => _snap?.view;

  List<RosterEntry> get roster => _snap?.roster ?? const [];

  RosterEntry? rosterOf(String? id) {
    for (final r in roster) {
      if (r.id == id) return r;
    }
    return null;
  }

  String nick(String? id) => rosterOf(id)?.nickname ?? '?';

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
      if (!mounted) return;
      setState(() {
        chat.add(m);
        unread++;
      });
    }));
    _load();
  }

  Future<void> _load() async {
    try {
      final snap = await _realtime.subscribeGame(widget.gameId);
      final api = ref.read(apiProvider);
      final history = await api.chat(widget.gameId);
      final savedMarks = snap.view.me == null ? const <Json>[] : await api.marks(widget.gameId);
      if (!mounted) return;
      setState(() {
        _snap = snap;
        chat
          ..clear()
          ..addAll(history);
        marks
          ..clear()
          ..addEntries(savedMarks.map((m) => MapEntry(m['cardId'] as String, CardMark.fromJson(m))));
      });
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
    _realtime.forgetGame(widget.gameId);
    super.dispose();
  }

  /// Отправить команду. Новое состояние придёт по SignalR; если нет — перечитаем снимок.
  Future<void> send(String type, [Json payload = const {}]) async {
    final v = view;
    if (v == null) return;
    final ok = await runAction(context, () => ref.read(apiProvider).command(widget.gameId, type, payload, v.version));
    if (ok != null && mounted) {
      setState(_resetSelection);
      final fresh = await runAction(context, () => ref.read(apiProvider).snapshot(widget.gameId));
      if (fresh != null && mounted && fresh.view.version >= (view?.version ?? 0)) setState(() => _snap = fresh);
    }
  }

  void refresh() => setState(() {});

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

  void _onCardTap(int row, int column, String cardId) {
    final v = view!;
    final stage = v.finale?.currentStage;
    if (v.can('ChooseTruth') || v.can('NameTruth')) {
      setState(() => truth[row] = column);
    } else if (v.can('CastVote') && stage != null && stage.isRow && stage.row == row && stage.candidateColumns.contains(column)) {
      setState(() => voteColumn = column);
    } else if (v.me != null) {
      MarkSheet.show(context, this, cardId);
    }
  }

  void _onPlayerTap(String id) {
    final v = view!;
    final pickable = v.can('HuntPick') || v.can('BlackmailerPick') || v.can('GiveFloor') || v.can('Nominate') ||
        (v.can('CastVote') && v.finale?.currentStage?.isRow == false);
    if (pickable) {
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
        appBar: AppBar(title: const Text('Партия')),
        body: Center(
          child: _error == null
              ? const CircularProgressIndicator()
              : Column(mainAxisSize: MainAxisSize.min, children: [
                  Text(_error!),
                  TextButton(onPressed: () => context.go('/'), child: const Text('На главную')),
                ]),
        ),
      );
    }

    final v = snap.view;
    return Scaffold(
      appBar: AppBar(
        leading: IconButton(icon: const Icon(Icons.home_outlined), onPressed: () => context.go('/')),
        title: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(T.phase(v.phase)),
          if (v.round > 0) Text('Раунд ${v.round} из ${v.totalRounds}', style: Theme.of(context).textTheme.bodySmall),
        ]),
        actions: [
          Countdown(deadline: snap.deadline),
          IconButton(
            tooltip: 'Чат',
            onPressed: () {
              setState(() => unread = 0);
              ChatSheet.show(context, this);
            },
            icon: Badge(isLabelVisible: unread > 0, label: Text('$unread'), child: const Icon(Icons.chat_bubble_outline)),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(children: [
          _PlayersStrip(screen: this, onTap: _onPlayerTap),
          const Divider(height: 1),
          Expanded(
            child: LayoutBuilder(
              builder: (context, box) => ListView(
                padding: const EdgeInsets.fromLTRB(8, 8, 8, 16),
                children: [
                  _Board(screen: this, width: box.maxWidth - 16, onTap: _onCardTap),
                  const SizedBox(height: 12),
                  _Hints(view: v),
                  const SizedBox(height: 12),
                  ActionPanel(screen: this),
                ],
              ),
            ),
          ),
          if (v.me != null) _Hand(screen: this),
        ]),
      ),
    );
  }
}

class _PlayersStrip extends StatelessWidget {
  const _PlayersStrip({required this.screen, required this.onTap});

  final GameScreenState screen;
  final void Function(String id) onTap;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    return SizedBox(
      height: 92,
      child: ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
        children: [
          for (final p in [...v.players]..sort((a, b) => a.seat.compareTo(b.seat)))
            GestureDetector(
              onTap: () => onTap(p.id),
              child: Container(
                width: 72,
                margin: const EdgeInsets.symmetric(horizontal: 3),
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(
                    color: screen.target == p.id
                        ? AppTheme.accent
                        : (v.currentSpeaker == p.id ? Colors.amber : Colors.transparent),
                    width: 2,
                  ),
                ),
                child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                  Stack(clipBehavior: Clip.none, children: [
                    Avatar(
                      nickname: screen.nick(p.id),
                      color: screen.rosterOf(p.id)?.avatarColor ?? '#5C7C99',
                      size: 40,
                      highlight: p.id == v.me?.id,
                    ),
                    if (p.hasActed) const Positioned(right: -4, bottom: -4, child: Icon(Icons.check_circle, size: 16, color: Colors.green)),
                    if (v.raisedHands.contains(p.id))
                      const Positioned(left: -6, top: -6, child: Icon(Icons.pan_tool, size: 16, color: Colors.amber)),
                    if (v.finale?.arrested.contains(p.id) == true)
                      const Positioned(right: -6, top: -6, child: Icon(Icons.lock, size: 16, color: AppTheme.danger)),
                  ]),
                  const SizedBox(height: 4),
                  Text(screen.nick(p.id), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12)),
                  Text(
                    p.knownRole == null ? '' : T.role(p.knownRole),
                    maxLines: 1,
                    style: TextStyle(fontSize: 10, color: p.knownRole == 'Killer' || p.knownRole == 'Accomplice' ? AppTheme.danger : null),
                  ),
                ]),
              ),
            ),
        ],
      ),
    );
  }
}

class _Board extends StatelessWidget {
  const _Board({required this.screen, required this.width, required this.onTap});

  final GameScreenState screen;
  final double width;
  final void Function(int row, int column, String cardId) onTap;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final columns = v.board.isEmpty ? 5 : v.board.first.cards.length;
    const labelWidth = 56.0;
    final size = ((width - labelWidth) / columns - 6).clamp(36.0, 120.0);
    final stage = v.finale?.currentStage;
    final outcomes = v.finale?.outcomes ?? const <VoteOutcome>[];

    return Column(children: [
      for (var r = 0; r < v.board.length; r++)
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 3),
          child: Row(children: [
            SizedBox(
              width: labelWidth,
              child: Text(T.category(v.board[r].category), style: Theme.of(context).textTheme.labelMedium),
            ),
            for (var c = 0; c < v.board[r].cards.length; c++)
              Padding(
                padding: const EdgeInsets.all(3),
                child: GestureDetector(
                  onTap: () => onTap(r, c, v.board[r].cards[c]),
                  onLongPress: v.me == null
                      ? null
                      : () {
                          final id = v.board[r].cards[c];
                          final m = screen.marks[id] ?? const CardMark();
                          screen.saveMark(id, m.copyWith(believed: !m.believed));
                        },
                  child: _BoardCard(
                    cardId: v.board[r].cards[c],
                    size: size,
                    mark: screen.marks[v.board[r].cards[c]],
                    isTruth: v.truth != null && v.truth!.length > r && v.truth![r] == c,
                    chosen: screen.truth[r] == c ||
                        (stage != null && stage.isRow && stage.row == r && (screen.voteColumn ?? v.finale?.myVoteColumn) == c),
                    voted: outcomes.any((o) => o.kind == 'Row' && o.row == r && o.column == c),
                    dimmed: stage != null && stage.isRow && stage.row == r && !stage.candidateColumns.contains(c),
                  ),
                ),
              ),
          ]),
        ),
    ]);
  }
}

class _BoardCard extends StatelessWidget {
  const _BoardCard({
    required this.cardId,
    required this.size,
    required this.mark,
    required this.isTruth,
    required this.chosen,
    required this.voted,
    required this.dimmed,
  });

  final String cardId;
  final double size;
  final CardMark? mark;
  final bool isTruth;
  final bool chosen;
  final bool voted;
  final bool dimmed;

  @override
  Widget build(BuildContext context) {
    final m = mark;
    final borderColor = chosen
        ? AppTheme.accent
        : m?.believed == true
            ? AppTheme.believed
            : isTruth
                ? Colors.amber
                : Colors.transparent;
    return Opacity(
      opacity: dimmed ? 0.35 : 1,
      child: Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(size * 0.1),
          border: Border.all(color: borderColor, width: 3),
        ),
        child: Stack(children: [
          CardImage(cardId: cardId, size: size),
          if (voted) Positioned(left: 2, top: 2, child: Icon(Icons.how_to_vote, size: size * 0.25, color: AppTheme.accent)),
          if (m != null && (m.crosses > 0 || m.checks > 0))
            Positioned(
              right: 2,
              bottom: 2,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 3),
                color: Colors.black54,
                child: Text(
                  '${m.crosses > 0 ? '✕${m.crosses}' : ''}${m.checks > 0 ? ' ✓${m.checks}' : ''}',
                  style: const TextStyle(fontSize: 10, color: Colors.white),
                ),
              ),
            ),
        ]),
      ),
    );
  }
}

/// Подсказки Призрака: столбик на каждый раунд, пустой раунд — карта рубашкой вверх.
class _Hints extends StatelessWidget {
  const _Hints({required this.view});

  final GameView view;

  @override
  Widget build(BuildContext context) {
    if (view.hints.isEmpty) return const SizedBox.shrink();
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text('Подсказки Призрака', style: Theme.of(context).textTheme.titleSmall),
      const SizedBox(height: 6),
      SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          for (final h in view.hints)
            Padding(
              padding: const EdgeInsets.only(right: 10),
              child: Column(children: [
                Text(h.round == 0 ? 'Зацепка' : 'Раунд ${h.round}', style: Theme.of(context).textTheme.labelSmall),
                const SizedBox(height: 4),
                if (h.cards.isEmpty)
                  Container(
                    width: 48,
                    height: 48,
                    decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(6), border: Border.all(color: Colors.white24)),
                    child: const Icon(Icons.visibility_off, size: 18, color: Colors.white38),
                  )
                else
                  for (final c in h.cards) Padding(padding: const EdgeInsets.only(bottom: 4), child: CardImage(cardId: c, size: 48)),
              ]),
            ),
        ]),
      ),
      if (view.vanishedCount > 0)
        Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Text('Исчезло писем: ${view.vanishedCount}', style: Theme.of(context).textTheme.bodySmall),
        ),
    ]);
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
    return Container(
      color: AppTheme.surface,
      padding: const EdgeInsets.fromLTRB(8, 6, 8, 8),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('Ваша рука · ${T.role(me.role)}', style: Theme.of(context).textTheme.labelMedium),
        const SizedBox(height: 4),
        SizedBox(
          height: 76,
          child: ListView(scrollDirection: Axis.horizontal, children: [
            for (final c in me.hand)
              GestureDetector(
                onTap: selectable
                    ? () {
                        if (screen.selectedHand.contains(c)) {
                          screen.selectedHand.remove(c);
                        } else {
                          if (screen.selectedHand.length >= limit) screen.selectedHand.clear();
                          screen.selectedHand.add(c);
                        }
                        screen.refresh();
                      }
                    : null,
                child: Container(
                  margin: const EdgeInsets.only(right: 6),
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: screen.selectedHand.contains(c) ? AppTheme.accent : Colors.transparent, width: 3),
                  ),
                  child: CardImage(cardId: c, size: 68),
                ),
              ),
          ]),
        ),
      ]),
    );
  }
}
