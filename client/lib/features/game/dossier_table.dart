part of 'game_screen.dart';

/// Experimental presentation of the same authorized snapshot and public chat.
/// No synthetic bot statements or additional role information are requested.
class _DossierTable extends StatefulWidget {
  const _DossierTable({required this.screen});
  final GameScreenState screen;
  @override
  State<_DossierTable> createState() => _DossierTableState();
}

class _DossierTableState extends State<_DossierTable> with _EvidenceBoard {
  final _seen = <String>{};
  final _queue = <ChatMessage>[];
  @override
  final _canvas = GlobalKey();
  final _source = GlobalKey();
  @override
  final _cards = <String, GlobalKey>{};
  ChatMessage? _message;
  Timer? _timer;
  final _typed = ValueNotifier<int>(0);
  List<String> _letters = [];
  int _holdTicks = 0;
  int _cueTicks = 0;
  final _mentioned = <int>{};
  final _mentionAt = <int, int>{};
  bool _shown(int index) => index < _visible || _mentioned.contains(index);
  int _visible = 0;
  bool _paused = false;
  @override
  GameScreenState get screen => widget.screen;
  List<ChatMessage> get messages => screen.chat.where((m) =>
      m.channel == 'public' && m.authorId != null &&
      (m.text?.isNotEmpty == true || m.cardIds.isNotEmpty || m.isVoice)).toList();

  @override
  void initState() {
    super.initState();
    screen.chatChanges.addListener(_changed);
    screen.thoughtChanges.addListener(_thought);
    _changed(initial: true);
  }

  void _changed({bool initial = false}) {
    final all = messages;
    final fresh = all.where((m) => _seen.add(m.id)).toList();
    if (initial || fresh.length > 5) {
      _queue.clear();
      _show(all.lastOrNull, animate: false);
    } else {
      _queue.addAll(fresh);
      if (_queue.length > 12) _queue.removeRange(0, _queue.length - 12);
      if (!_paused && _timer == null && _queue.isNotEmpty) _next();
    }
    if (mounted) setState(() {});
  }

  void _thought() {
    if (mounted) setState(() {});
  }

  void _show(ChatMessage? message, {bool animate = true}) {
    _timer?.cancel();
    _timer = null;
    _letters = (message?.text ?? '').characters.toList();
    _mentioned.clear();
    _mentionAt.clear();
    if (message != null) {
      for (var i = 0; i < message.cardIds.length; i++) {
        for (final row in screen.view!.board) {
          final column = row.cards.indexOf(message.cardIds[i]);
          if (column < 0) continue;
          final category = T.category(row.category).toLowerCase();
          final match = RegExp('${RegExp.escape(category)}\\s+(?:карта\\s+)?${column + 1}(?![0-9])',
            caseSensitive: false).firstMatch(message.text ?? '');
          if (match != null) {
            _mentionAt[i] = (message.text ?? '').substring(0, match.end).characters.length;
          }
        }
      }
    }
    _typed.value = animate ? 0 : _letters.length;
    _holdTicks = 0;
    _cueTicks = 0;
    setState(() {
      _message = message;
      _visible = animate ? 0 : message?.cardIds.length ?? 0;
    });
    if (message == null || !animate || _paused) return;
    _run();
  }

  void _run() {
    _timer?.cancel();
    // Only the text notifier rebuilds for each grapheme, not the whole board.
    _timer = Timer.periodic(const Duration(milliseconds: 35), (timer) {
      if (_typed.value < _letters.length) {
        _typed.value++;
        if (_typed.value % 5 == 0 && _letters[_typed.value - 1].trim().isNotEmpty) {
          screen.ref.read(soundProvider).play(Sfx.typing);
        }
        final mentioned = _mentionAt.entries.where((e) => e.value <= _typed.value && !_mentioned.contains(e.key));
        if (mentioned.isNotEmpty) setState(() => _mentioned.addAll(mentioned.map((e) => e.key)));
        return;
      }
      // Structured card notes are shown as individual spoken beats. A mark
      // appears with its beat, never before the referenced card is presented.
      if (_visible < (_message?.cardIds.length ?? 0)) {
        if (_cueTicks++ % 40 == 0) setState(() => _visible++);
        return;
      }
      if (++_holdTicks >= 86) {
        timer.cancel();
        _timer = null;
        if (_queue.isNotEmpty) _next();
      }
    });
  }

  void _next() {
    if (_queue.isNotEmpty) _show(_queue.removeAt(0));
  }

  void _complete() {
    _typed.value = _letters.length;
    setState(() => _visible = _message?.cardIds.length ?? 0);
    _holdTicks = 0;
  }

  @override
  void dispose() {
    screen.chatChanges.removeListener(_changed);
    screen.thoughtChanges.removeListener(_thought);
    _timer?.cancel();
    _typed.dispose();
    super.dispose();
  }

  Color _color(int index) => theoryNegative(_message?.noteFor(index) ?? '')
      ? AppColors.redSoft : AppColors.greenSoft;

  void _edit([String? source]) => showModalBottomSheet<void>(
      context: context, isScrollControlled: true, useSafeArea: true,
      builder: (_) => TableTheorySheet(screen: screen, initialSource: source));

  Widget _board(double size) {
    final m = _message;
    return CustomPaint(
      key: _canvas,
      foregroundPainter: _DossierLinks(() {
        final canvas = _canvas.currentContext?.findRenderObject();
        final source = _source.currentContext?.findRenderObject();
        if (canvas is! RenderBox || source is! RenderBox || m == null) return [];
        final start = canvas.globalToLocal(source.localToGlobal(Offset(source.size.width / 2, 0)));
        return [for (var i = 1; i < m.cardIds.length; i++)
          if (_shown(i) && theoryLinked(m, i) && _cards[m.cardIds[i]]?.currentContext?.findRenderObject() is RenderBox)
            (() {
              final target = _cards[m.cardIds[i]]!.currentContext!.findRenderObject()! as RenderBox;
              return (start, canvas.globalToLocal(target.localToGlobal(Offset(target.size.width / 2, target.size.height))), _color(i));
            })()];
      }),
      child: CustomPaint(foregroundPainter: _TableThreadsPainter(_threadLines),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        _botThinking(),
        Text('УЛИКИ НА СТОЛЕ', style: sectionLabel()),
        const SizedBox(height: 12),
        _Board(screen: screen, maxCard: size, decorate: (id, child) {
          final index = m?.cardIds.indexOf(id) ?? -1;
          final active = index >= 0 && _shown(index);
          return Container(key: _cards.putIfAbsent(id, GlobalKey.new),
            foregroundDecoration: active ? BoxDecoration(
              border: Border.all(color: _color(index), width: 3),
              borderRadius: BorderRadius.circular(12)) : null,
            child: Stack(children: [_boardOverlay(id, child), if (active) Positioned(right: 3, bottom: 3,
              child: IgnorePointer(child: Semantics(
                label: '${screen.nick(m!.authorId)}: ${theoryLabel(m.noteFor(index) ?? '')}',
                child: Container(key: ValueKey('dossier-mark-$id'),
                  padding: const EdgeInsets.all(2),
                  decoration: BoxDecoration(color: AppColors.bg,
                    borderRadius: BorderRadius.circular(8)),
                  child: Column(mainAxisSize: MainAxisSize.min, children: [
                    Avatar(nickname: screen.nick(m.authorId), color: screen.colorOf(m.authorId),
                      photoId: screen.photoOf(m.authorId), size: 20),
                    Icon(theoryNegative(m.noteFor(index) ?? '') ? Icons.close :
                      theoryLabel(m.noteFor(index) ?? '') == 'За' ? Icons.check : Icons.search,
                      size: 14, color: _color(index)),
                  ])))))]));
        }),
        _roundHints(size),
        _claimsStrip(size),
        _boardControls(),
        if (m != null && m.cardIds.isNotEmpty) Padding(
          padding: const EdgeInsets.only(top: 16),
          child: Row(children: [
            GestureDetector(key: _source, onTap: () => showCardZoom(context, m.cardIds.first),
              child: CardImage(cardId: m.cardIds.first, size: 54)),
            const SizedBox(width: 12),
            Expanded(child: Text('${screen.nick(m.authorId)} · раунд ${m.round}\n'
                'Публичная версия, не подтверждённая истина',
                style: const TextStyle(color: AppColors.muted, fontSize: 12))),
          ])),
      ])));
  }

  Widget _dialogue() {
    final m = _message;
    return Container(padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: AppColors.surface,
        border: Border.all(color: AppColors.border), borderRadius: BorderRadius.circular(16)),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('ЗА СТОЛОМ', style: sectionLabel()),
        const SizedBox(height: 12),
        if (m == null) const Text('Здесь появятся публичные реплики и версии игроков.')
        else ...[
          Row(children: [Avatar(nickname: screen.nick(m.authorId), color: screen.colorOf(m.authorId),
            photoId: screen.photoOf(m.authorId), size: 30), const SizedBox(width: 8),
            Expanded(child: Text(screen.nick(m.authorId), style: heading(17)))]),
          const SizedBox(height: 12),
          GestureDetector(onTap: _complete, child: Stack(children: [
            ExcludeSemantics(child: Opacity(opacity: 0, child: Text(
              m.text ?? 'Голосовое сообщение — откройте разговор для прослушивания.',
              style: const TextStyle(height: 1.6)))),
            ValueListenableBuilder<int>(
            valueListenable: _typed, builder: (context, count, _) => Text(
              m.text == null ? 'Голосовое сообщение — откройте разговор для прослушивания.' :
                _letters.take(count).join(),
              key: const Key('dossier-statement'), style: const TextStyle(height: 1.6))),
          ])),
          if (_visible > 0 && m.cardIds.isNotEmpty) Padding(
            padding: const EdgeInsets.only(top: 12),
            child: Row(children: [
              Expanded(child: Text(_cardCue(m, _visible - 1),
                key: const Key('dossier-card-cue'), style: const TextStyle(color: AppColors.ice))),
              CardImage(cardId: m.cardIds[_visible - 1], size: 52),
            ])),
          const SizedBox(height: 12),
          Wrap(spacing: 6, runSpacing: 6, children: [
            for (var i = 0; i < m.cardIds.length; i++)
              if (_shown(i))
              GestureDetector(onTap: () => showCardZoom(context, m.cardIds[i]),
                child: Column(mainAxisSize: MainAxisSize.min, children: [
                  CardImage(cardId: m.cardIds[i], size: 46),
                  Text(theoryLabel(m.noteFor(i) ?? ''), style: const TextStyle(fontSize: 10)),
                ])),
          ]),
        ],
        Wrap(spacing: 4, children: [
          TextButton.icon(icon: Icon(_paused ? Icons.play_arrow : Icons.pause),
            label: Text(_paused ? 'Продолжить' : 'Пауза'), onPressed: () {
              setState(() => _paused = !_paused);
              if (_paused) { _timer?.cancel(); _timer = null; }
              else if (_message != null) { _run(); }
            }),
          TextButton(onPressed: _message == null ? null : _complete,
            child: const Text('Показать сразу')),
          TextButton(onPressed: _queue.isEmpty ? null : _next, child: const Text('Дальше')),
          TextButton(onPressed: screen.openChat, child: const Text('Весь разговор')),
        ]),
        if (screen.view!.me != null && screen.view!.me!.role != 'Ghost')
          OutlinedButton.icon(key: const Key('dossier-edit'),
            icon: const Icon(Icons.account_tree_outlined), label: const Text('Моя версия'),
            onPressed: () => _edit()),
      ]));
  }

  String _cardCue(ChatMessage m, int index) {
    final note = m.noteFor(index) ?? '';
    if (note.startsWith('кидал')) return 'Я говорил, что отправил эту карту';
    if (theorySource(note)) return 'Рассматриваю эту улику';
    if (theoryNegative(note)) return 'По моей версии, эту карту исключаю';
    if (theoryLabel(note) == 'Проверка') return 'Проверял связь с этой картой';
    return 'По моей версии, эта карта подходит';
  }

  Widget _actions() => Column(children: [
    KeyedSubtree(key: screen._panelKey, child: ActionPanel(screen: screen)),
    if (screen.view!.me?.letters.isNotEmpty == true)
      ExpansionTile(title: const Text('Мои письма'), children: [_claimButtons(), _MyLetters(screen: screen)]),
  ]);

  Widget _roundHints(double size) => ExpansionTile(
    key: const Key('dossier-bottom-hints'), initiallyExpanded: true,
    title: const Text('Подсказки Призрака · по раундам'),
    children: [SingleChildScrollView(scrollDirection: Axis.horizontal,
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        for (final hint in screen.view!.hints)
          Padding(padding: const EdgeInsets.only(right: 16, bottom: 12),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(hint.round == 0 ? 'Первая зацепка' : 'Раунд ${hint.round}', style: sectionLabel()),
              const SizedBox(height: 8),
              if (hint.cards.isEmpty) SizedBox(width: size, height: size,
                child: const Center(child: Text('Нет открытых улик')))
              else Row(children: [for (final card in hint.cards)
                Padding(padding: const EdgeInsets.only(right: 5), child: _sourceCard(
                  _ThreadSource('Hint', card), size, key: Key('hint-$card'),
                  onLongPress: screen.view!.me == null ? null : () => HintSheet.show(context, screen, card, hint.round))),
              ]),
            ])),
      ]))]);

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(child: Column(children: [
      const ConnectionBanner(),
      _Header(screen: screen, deadline: screen._snap!.deadline),
      _PlayersStrip(screen: screen),
      Expanded(child: LayoutBuilder(builder: (context, box) {
        final wide = box.maxWidth >= 1050;
        final boardWidth = box.maxWidth - (wide ? 380 : 0) - 32;
        final columns = screen.view!.board.isEmpty ? 5 : screen.view!.board.first.cards.length;
        var size = ((boardWidth - _Board.labelWidth - _Board.gap * columns) / columns).clamp(24.0, 260.0).floorToDouble();
        if (size > 96) {
          final label = (size * .55).clamp(_Board.labelWidth, 100.0).floorToDouble();
          size = ((boardWidth - label - _Board.gap * columns) / columns).clamp(24.0, 260.0).floorToDouble();
        }
        final board = Padding(padding: const EdgeInsets.all(16), child: _board(size));
        if (!wide) {
          return ListView(controller: screen._scroll,
            children: [board, Padding(padding: const EdgeInsets.all(12), child: _dialogue()), _actions()]);
        }
        return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Expanded(child: SingleChildScrollView(child: board)),
          SizedBox(width: 380, child: ListView(controller: screen._scroll,
            padding: const EdgeInsets.all(12), children: [_dialogue(), const SizedBox(height: 12), _actions()])),
        ]);
      })),
      _Dock(screen: screen, compact: true),
    ])));
}

class _DossierLinks extends CustomPainter {
  _DossierLinks(this.links);
  final List<(Offset, Offset, Color)> Function() links;
  @override
  void paint(Canvas canvas, Size size) {
    for (final (start, end, color) in links()) {
      final path = Path()..moveTo(start.dx, start.dy)
        ..cubicTo(start.dx, start.dy - 25, end.dx, end.dy + 25, end.dx, end.dy);
      canvas.drawPath(path, Paint()..color = color.withValues(alpha: .8)
        ..style = PaintingStyle.stroke..strokeWidth = 2);
    }
  }
  @override
  bool shouldRepaint(_DossierLinks oldDelegate) => true;
}
