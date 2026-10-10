part of 'game_screen.dart';

/// Причины нити — короткие метки из прототипа D01.
const _threadReasons = ['цвет', 'форма', 'предмет', 'настроение', 'деталь'];

/// Изменение доски, ещё не выложенное на стол.
class _BoardOp {
  const _BoardOp(this.json, this.label, this.color);
  final Json json;
  final String label;
  final Color color;

  bool same(_BoardOp o) =>
      json['kind'] == o.json['kind'] && json['target'] == o.json['target'] &&
      json['source'] == o.json['source'] && json['thread'] == o.json['thread'] && json['round'] == o.json['round'];
}

/// Улика, от которой тянут нить: подсказка Призрака или названное письмо.
class _ThreadSource {
  const _ThreadSource(this.kind, this.card);
  final String kind;
  final String card;
  String get key => '$kind:$card';
  @override
  bool operator ==(Object other) => other is _ThreadSource && other.kind == kind && other.card == card;
  @override
  int get hashCode => Object.hash(kind, card);
}

class _ThreadLine {
  const _ThreadLine(this.start, this.end, this.color, this.width, this.dashed, this.opacity, this.label, this.fill);
  final Offset start;
  final Offset end;
  final Color color;
  final double width;
  final bool dashed;
  final double opacity;
  final String label;
  final Color fill;
}

/// Доска улик (редизайн стола, экран D01): нити от подсказок и писем к картам поля,
/// булавки версий, отметки «проверял — исчезло», слои по игрокам и черновик поста.
mixin _EvidenceBoard on State<_DossierTable> {
  GameScreenState get screen;
  Map<String, GlobalKey> get _cards;
  GlobalKey get _canvas;

  final _sourceKeys = <String, GlobalKey>{};
  final _drafts = <_BoardOp>[];
  String _layer = 'all';
  _ThreadSource? _held;
  String? _tool;
  bool _posting = false;

  GameView get _bv => screen.view!;
  TableView get _table => _bv.table;
  String? get _me => _bv.me?.id;
  bool get _canPost => _table.canPost && _me != null && !_bv.isGhost;
  bool get _pinsOnly => _table.pinsOnly;
  bool get _intercepting => _canPost && (_held != null || _tool != null);

  GlobalKey _sourceKey(String kind, String card) => _sourceKeys.putIfAbsent('$kind:$card', GlobalKey.new);

  String _initial(String id) {
    final n = screen.nick(id);
    return n.isEmpty ? '?' : n.characters.first.toUpperCase();
  }

  (int, int)? _cell(String id) {
    final board = _bv.board;
    for (var r = 0; r < board.length; r++) {
      final c = board[r].cards.indexOf(id);
      if (c >= 0) return (r, c);
    }
    return null;
  }

  String _cellLabel(String id) {
    final cell = _cell(id);
    if (cell == null) return 'карта';
    return '${T.category(_bv.board[cell.$1].category).toLowerCase()} ${cell.$2 + 1}';
  }

  String _sourceLabel(_ThreadSource s) {
    if (s.kind == 'Letter') {
      final claim = _table.claims.where((c) => c.card == s.card).firstOrNull;
      return claim == null ? 'письмо' : 'письмо р.${claim.round}';
    }
    final hint = _bv.hints.where((h) => h.cards.contains(s.card)).firstOrNull;
    if (hint == null) return 'подсказка';
    return hint.round == 0 ? 'зацепка' : 'р.${hint.round}';
  }

  bool _visibleAuthor(String author) =>
      _layer == 'all' || _layer == 'conflict' || _layer == author || (_layer == 'me' && author == _me);

  /// Спорная карта: на неё тянут и «за», и «против», или в её ряду булавки на разных картах.
  bool _conflict(String id) {
    final threads = _table.threads.where((t) => t.target == id);
    if (threads.any((t) => t.isFor) && threads.any((t) => !t.isFor)) return true;
    final cell = _cell(id);
    if (cell == null) return false;
    final pins = _table.pins.where((p) => p.row == cell.$1);
    return pins.any((p) => p.column == cell.$2) && pins.map((p) => p.column).toSet().length > 1;
  }

  void _addOp(_BoardOp op) {
    final same = _drafts.indexWhere(op.same);
    if (same >= 0) {
      setState(() => _drafts.removeAt(same));
      return;
    }
    if (_drafts.length >= 6) {
      screen._snack('В одном посте до 6 изменений — выложите эти на стол.');
      return;
    }
    setState(() => _drafts.add(op));
  }

  void _tapSource(_ThreadSource source) {
    HapticFeedback.selectionClick();
    setState(() {
      _held = _held == source ? null : source;
      _tool = null;
    });
  }

  void _tapBoardCard(String id) {
    final held = _held;
    if (held != null) {
      _openThreadSheet(held, id);
      return;
    }
    final cell = _cell(id);
    if (cell == null) return;
    if (_tool == 'pin') {
      final mine = _table.pins.any((p) => p.author == _me && p.row == cell.$1 && p.column == cell.$2);
      _drafts.removeWhere((d) => d.json['kind'] == 'Pin' && d.json['target'] != id && _cell(d.json['target'] as String)?.$1 == cell.$1);
      _addOp(_BoardOp({'kind': mine ? 'Unpin' : 'Pin', 'target': id},
        '${mine ? 'снять булавку' : 'булавка'}: ${_cellLabel(id)}', AppColors.amber));
    } else if (_tool == 'check') {
      final mine = _table.checks.any((c) => c.author == _me && c.card == id);
      _addOp(_BoardOp({'kind': mine ? 'Uncheck' : 'Check', 'target': id},
        '${mine ? 'снять ✕' : '✕ проверено'}: ${_cellLabel(id)}', AppColors.redSoft));
    }
  }

  Future<void> _openThreadSheet(_ThreadSource source, String target) async {
    final ops = await showModalBottomSheet<List<_BoardOp>>(
      context: context, isScrollControlled: true, useSafeArea: true,
      builder: (_) => _ThreadSheet(board: this, source: source, target: target));
    if (!mounted) return;
    setState(() => _held = null);
    for (final op in ops ?? const <_BoardOp>[]) {
      _addOp(op);
    }
  }

  Future<void> _post() async {
    if (_drafts.isEmpty || _posting) return;
    setState(() => _posting = true);
    final ok = await screen.postTable([for (final d in _drafts) d.json]);
    if (!mounted) return;
    setState(() {
      _posting = false;
      if (ok) {
        _drafts.clear();
        _held = null;
        _tool = null;
      }
    });
  }

  List<_ThreadLine> _threadLines() {
    final canvas = _canvas.currentContext?.findRenderObject();
    if (canvas is! RenderBox || !canvas.attached) return const [];
    final lines = <_ThreadLine>[];
    for (final t in _table.threads) {
      final src = _sourceKeys['${t.sourceKind}:${t.source}']?.currentContext?.findRenderObject();
      final dst = _cards[t.target]?.currentContext?.findRenderObject();
      if (src is! RenderBox || dst is! RenderBox || !src.attached || !dst.attached) continue;
      final start = canvas.globalToLocal(src.localToGlobal(Offset(src.size.width / 2, 0)));
      final end = canvas.globalToLocal(dst.localToGlobal(Offset(dst.size.width / 2, dst.size.height)));
      final mine = t.author == _me;
      final on = _visibleAuthor(t.author) && (_layer != 'conflict' || _conflict(t.target));
      lines.add(_ThreadLine(start, end, t.isFor ? AppColors.believed : AppColors.redSoft, mine ? 3 : 2,
        !t.isFor, on ? (mine || _layer != 'all' ? .95 : .7) : .08,
        '${_initial(t.author)}${t.endorsedBy.isEmpty ? '' : ' +${t.endorsedBy.length}'}',
        colorFromHex(screen.colorOf(t.author))));
    }
    return lines;
  }

  /// Булавки, проверки и подсветка поверх карты поля; перехватывает нажатие, когда улика в руке.
  Widget _boardOverlay(String id, Widget child) {
    final cell = _cell(id);
    final pins = [
      for (final p in _table.pins)
        if (cell != null && p.row == cell.$1 && p.column == cell.$2 && _visibleAuthor(p.author)) p.author,
    ].take(3).toList();
    final checks = [for (final c in _table.checks) if (c.card == id && _visibleAuthor(c.author)) c.author];
    final conflict = _layer == 'conflict' && _conflict(id);
    final dimmed = _layer == 'conflict' && !conflict;
    Widget w = Stack(children: [
      child,
      if (checks.isNotEmpty) Positioned(top: 2, left: 0, right: 0, child: IgnorePointer(child: Center(
        child: Container(key: ValueKey('table-check-$id'),
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
          decoration: BoxDecoration(color: AppColors.bg.withValues(alpha: .85), borderRadius: BorderRadius.circular(8)),
          child: Text('✕ ${checks.map(_initial).join()}',
            style: const TextStyle(color: AppColors.redSoft, fontSize: 10, fontWeight: FontWeight.w700)))))),
      if (pins.isNotEmpty) Positioned(left: 2, bottom: 2, child: IgnorePointer(child: Row(
        key: ValueKey('table-pins-$id'), mainAxisSize: MainAxisSize.min, children: [
          for (final a in pins) Padding(padding: const EdgeInsets.only(right: 1),
            child: Avatar(nickname: screen.nick(a), color: screen.colorOf(a), photoId: screen.photoOf(a), size: 16)),
        ]))),
    ]);
    if (conflict || (_held != null && _canPost)) {
      w = Container(
        foregroundDecoration: BoxDecoration(
          border: Border.all(color: conflict ? AppColors.amber : AppColors.ice, width: conflict ? 3 : 1),
          borderRadius: BorderRadius.circular(10)),
        child: w);
    }
    if (dimmed) w = Opacity(opacity: .35, child: w);
    return GestureDetector(key: ValueKey('table-card-$id'),
      onTap: _intercepting ? () => _tapBoardCard(id) : null, child: w);
  }

  /// Карта-улика под полем: подсказка или письмо. Нажатие берёт её в руку, двойное — увеличивает.
  Widget _sourceCard(_ThreadSource source, double size, {VoidCallback? onLongPress, Key? key}) {
    final held = _held == source;
    final canHold = _canPost && !_pinsOnly;
    return GestureDetector(key: key,
      onTap: canHold ? () => _tapSource(source) : () => showCardZoom(context, source.card),
      onDoubleTap: canHold ? () => showCardZoom(context, source.card) : null,
      onLongPress: onLongPress,
      child: AnimatedContainer(duration: const Duration(milliseconds: 150),
        transform: Matrix4.translationValues(0, held ? -4 : 0, 0),
        foregroundDecoration: held ? BoxDecoration(border: Border.all(color: AppColors.amber, width: 3),
          borderRadius: BorderRadius.circular(9)) : null,
        child: KeyedSubtree(key: _sourceKey(source.kind, source.card), child: CardImage(cardId: source.card, size: size))));
  }

  /// Письма, которые игроки назвали своими: от них тоже можно тянуть нити.
  Widget _claimsStrip(double size) {
    final cards = <String, List<LetterClaim>>{};
    for (final c in _table.claims) {
      (cards[c.card] ??= []).add(c);
    }
    if (cards.isEmpty) return const SizedBox.shrink();
    return Padding(padding: const EdgeInsets.only(top: 12), child: Column(
      crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('ПИСЬМА, НАЗВАННЫЕ СВОИМИ', style: sectionLabel()),
        const SizedBox(height: 8),
        SingleChildScrollView(scrollDirection: Axis.horizontal, child: Row(children: [
          for (final e in cards.entries) Padding(padding: const EdgeInsets.only(right: 8), child: Column(children: [
            _sourceCard(_ThreadSource('Letter', e.key), size, key: Key('claim-${e.key}')),
            const SizedBox(height: 4),
            Text(e.value.map((c) => '${screen.nick(c.author)} · р.${c.round}').join('\n'),
              textAlign: TextAlign.center, style: const TextStyle(fontSize: 10, color: AppColors.muted)),
          ])),
        ])),
      ]));
  }

  /// Кнопки «это моё письмо» для своих писем — заявление видно всем на доске.
  Widget _claimButtons() {
    final me = _bv.me;
    if (!_canPost || _pinsOnly || me == null || me.letters.isEmpty) return const SizedBox.shrink();
    return Padding(padding: const EdgeInsets.fromLTRB(16, 0, 16, 8), child: Wrap(spacing: 6, runSpacing: 6, children: [
      for (final l in me.letters)
        if (!_table.claims.any((c) => c.author == me.id && c.round == l.round && c.card == l.cardId))
          ActionChip(key: Key('claim-letter-${l.round}'), avatar: CardImage(cardId: l.cardId, size: 20),
            label: Text('Моё письмо р.${l.round} — на доску'),
            onPressed: () => _addOp(_BoardOp({'kind': 'Claim', 'round': l.round, 'source': l.cardId},
              'моё письмо р.${l.round}', AppColors.ice))),
    ]));
  }

  List<(String, String)> _layers() {
    final authors = <String>{
      for (final t in _table.threads) t.author,
      for (final p in _table.pins) p.author,
      for (final c in _table.checks) c.author,
    }..remove(_me);
    final players = [for (final p in _bv.players) if (authors.contains(p.id)) p.id];
    return [
      ('all', 'Все версии'),
      if (_me != null && !_bv.isGhost) ('me', 'Моя'),
      for (final id in players) (id, screen.nick(id)),
      ('conflict', 'Спорные'),
    ];
  }

  Widget _boardControls() {
    if (!_canPost && _table.isEmpty) return const SizedBox.shrink();
    final rows = _bv.board.length;
    final myPins = _table.pins.where((p) => p.author == _me).length;
    final String prompt;
    if (!_canPost) {
      prompt = 'Доска улик: нити, булавки и проверки игроков. Это их версии, а не истина.';
    } else if (_pinsOnly) {
      prompt = 'Идёт голосование: можно только переставить булавки своей версии.';
    } else if (_held != null) {
      prompt = 'Улика в руке. Нажмите карту поля, на которую она указывает.';
    } else if (_tool == 'pin') {
      prompt = 'Нажмите карту — булавка «моя версия по ряду». В ряду одна булавка.';
    } else if (_tool == 'check') {
      prompt = 'Нажмите карту, которую проверяли письмом, — письмо исчезло.';
    } else {
      prompt = 'Нажмите подсказку Призрака, потом карту поля — появится нить. Нити видят все, их читают и боты.';
    }
    Widget tool(String id, String label, IconData icon) => ChoiceChip(key: Key('table-tool-$id'),
      avatar: Icon(icon, size: 16), label: Text(label), selected: _tool == id,
      onSelected: (on) => setState(() {
        _tool = on ? id : null;
        _held = null;
      }));
    return Padding(padding: const EdgeInsets.only(top: 12), child: Column(
      crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('ДОСКА УЛИК', style: sectionLabel()),
        const SizedBox(height: 8),
        SingleChildScrollView(scrollDirection: Axis.horizontal, child: Row(children: [
          for (final (id, label) in _layers()) Padding(padding: const EdgeInsets.only(right: 6),
            child: ChoiceChip(key: Key('layer-$id'), label: Text(label), selected: _layer == id,
              onSelected: (_) => setState(() => _layer = id))),
        ])),
        const SizedBox(height: 8),
        Text(prompt, key: const Key('table-prompt'), style: TextStyle(fontSize: 13, height: 1.35,
          color: _held != null ? AppColors.amber : AppColors.muted)),
        if (_canPost) ...[
          const SizedBox(height: 8),
          Wrap(spacing: 6, runSpacing: 6, children: [
            tool('pin', 'Булавка', Icons.push_pin_outlined),
            if (!_pinsOnly) tool('check', 'Проверено ✕', Icons.close),
          ]),
          if (_drafts.isNotEmpty) ...[
            const SizedBox(height: 8),
            Wrap(spacing: 6, runSpacing: 6, children: [
              for (var i = 0; i < _drafts.length; i++)
                InputChip(key: Key('table-draft-$i'), label: Text(_drafts[i].label),
                  side: BorderSide(color: _drafts[i].color),
                  onDeleted: () => setState(() => _drafts.removeAt(i))),
            ]),
          ],
          const SizedBox(height: 8),
          SizedBox(width: double.infinity, height: 52, child: FilledButton(key: const Key('table-post'),
            onPressed: _drafts.isEmpty || _posting ? null : _post,
            child: Text(_drafts.isEmpty ? 'МОЯ ВЕРСИЯ: $myPins ИЗ $rows РЯДОВ' : 'ВЫЛОЖИТЬ НА СТОЛ (${_drafts.length})',
              style: heading(16, color: _drafts.isEmpty ? AppColors.muted : AppColors.onAmber)))),
        ],
      ]));
  }
}

/// Шторка нити: «подсказка → карта поля», причина, булавка, ЗА / ПРОТИВ и чужие нити между теми же картами.
class _ThreadSheet extends StatefulWidget {
  const _ThreadSheet({required this.board, required this.source, required this.target});
  final _EvidenceBoard board;
  final _ThreadSource source;
  final String target;
  @override
  State<_ThreadSheet> createState() => _ThreadSheetState();
}

class _ThreadSheetState extends State<_ThreadSheet> {
  String? _reason;
  bool _pin = false;

  _EvidenceBoard get b => widget.board;

  @override
  Widget build(BuildContext context) {
    final s = widget.source;
    final target = widget.target;
    final same = b._table.threads.where((t) => t.sourceKind == s.kind && t.source == s.card && t.target == target).toList();
    final mine = same.where((t) => t.author == b._me).firstOrNull;
    final others = same.where((t) => t.author != b._me).toList();
    final title = '${s.kind == 'Letter' ? 'Письмо' : 'Подсказка'} ${b._sourceLabel(s)} → ${b._cellLabel(target)}';

    void link(String stance) {
      final reason = _reason;
      final label = '${b._sourceLabel(s)} → ${b._cellLabel(target)} · ${stance == 'For' ? 'за' : 'против'}'
          '${reason == null ? '' : ' ($reason)'}';
      Navigator.pop(context, [
        _BoardOp({'kind': 'Link', 'sourceKind': s.kind, 'source': s.card, 'target': target, 'stance': stance,
          if (reason != null) 'reason': reason}, label, stance == 'For' ? AppColors.believed : AppColors.redSoft),
        if (_pin) _BoardOp({'kind': 'Pin', 'target': target}, 'булавка: ${b._cellLabel(target)}', AppColors.amber),
      ]);
    }

    Widget stanceButton(String stance) => Expanded(child: SizedBox(height: 52, child: FilledButton(
      key: Key('thread-${stance.toLowerCase()}'),
      style: FilledButton.styleFrom(backgroundColor: stance == 'For' ? AppColors.green : AppColors.red,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14))),
      onPressed: () => link(stance),
      child: Text(stance == 'For' ? 'ЗА' : 'ПРОТИВ', style: heading(16, color: Colors.white)))));

    return Padding(padding: EdgeInsets.fromLTRB(16, 18, 16, 24 + MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          CardImage(cardId: s.card, size: 52),
          const Padding(padding: EdgeInsets.symmetric(horizontal: 8), child: Icon(Icons.arrow_forward, color: AppColors.muted)),
          CardImage(cardId: target, size: 52),
          const SizedBox(width: 10),
          Expanded(child: Text(title, style: const TextStyle(fontSize: 14, height: 1.35))),
        ]),
        if (others.isNotEmpty) ...[
          const SizedBox(height: 14),
          Text('УЖЕ НА СТОЛЕ', style: sectionLabel()),
          for (final t in others) _OtherThread(board: b, thread: t),
        ],
        const SizedBox(height: 14),
        const Text('Почему (по желанию)', style: TextStyle(fontSize: 12, color: AppColors.muted)),
        const SizedBox(height: 6),
        Wrap(spacing: 6, runSpacing: 6, children: [
          for (final r in _threadReasons) ChoiceChip(key: Key('reason-$r'), label: Text(r), selected: _reason == r,
            onSelected: (_) => setState(() => _reason = _reason == r ? null : r)),
        ]),
        const SizedBox(height: 6),
        CheckboxListTile(key: const Key('thread-pin'), contentPadding: EdgeInsets.zero, value: _pin,
          controlAffinity: ListTileControlAffinity.leading,
          title: const Text('Это моя версия по ряду (булавка)'),
          onChanged: (v) => setState(() => _pin = v ?? false)),
        Row(children: [stanceButton('For'), const SizedBox(width: 8), stanceButton('Against')]),
        if (mine != null) TextButton(key: const Key('thread-unlink'),
          onPressed: () => Navigator.pop(context, [_BoardOp({'kind': 'Unlink', 'thread': mine.id},
            'убрать нить: ${b._sourceLabel(s)} → ${b._cellLabel(target)}', AppColors.muted)]),
          child: const Text('Убрать мою нить')),
        Center(child: TextButton(onPressed: () => Navigator.pop(context), child: const Text('Отмена'))),
      ])));
  }
}

/// Чужая нить в шторке: можно согласиться, оспорить или снять своё мнение.
class _OtherThread extends StatelessWidget {
  const _OtherThread({required this.board, required this.thread});
  final _EvidenceBoard board;
  final TableThread thread;

  @override
  Widget build(BuildContext context) {
    final b = board;
    final t = thread;
    final me = b._me;
    final agreed = me != null && t.endorsedBy.contains(me);
    final disputed = me != null && t.disputedBy.contains(me);
    final who = b.screen.nick(t.author);
    void pick(String kind, String label) => Navigator.pop(context, [_BoardOp({'kind': kind, 'thread': t.id},
      '$label: нить $who', kind == 'Dispute' ? AppColors.redSoft : AppColors.believed)]);
    return Padding(padding: const EdgeInsets.only(top: 8), child: Row(children: [
      Avatar(nickname: who, color: b.screen.colorOf(t.author), photoId: b.screen.photoOf(t.author), size: 26),
      const SizedBox(width: 8),
      Expanded(child: Text('$who: ${t.isFor ? 'за' : 'против'}${t.reason == null ? '' : ' (${t.reason})'}'
          '${t.endorsedBy.isEmpty ? '' : ' · согласны ${t.endorsedBy.length}'}',
        style: const TextStyle(fontSize: 13))),
      if (agreed || disputed)
        TextButton(key: Key('thread-clear-${t.id}'), onPressed: () => pick('Clear', 'снять мнение'), child: const Text('Снять'))
      else ...[
        TextButton(key: Key('thread-endorse-${t.id}'), onPressed: () => pick('Endorse', 'согласен'), child: const Text('Согласен')),
        TextButton(key: Key('thread-dispute-${t.id}'), onPressed: () => pick('Dispute', 'оспорить'), child: const Text('Оспорить')),
      ],
    ]));
  }
}

class _TableThreadsPainter extends CustomPainter {
  _TableThreadsPainter(this.lines);
  final List<_ThreadLine> Function() lines;

  @override
  void paint(Canvas canvas, Size size) {
    for (final l in lines()) {
      final paint = Paint()..color = l.color.withValues(alpha: l.opacity)
        ..style = PaintingStyle.stroke..strokeWidth = l.width;
      final d = l.end - l.start;
      final length = d.distance;
      if (length == 0) continue;
      if (l.dashed) {
        final u = d / length;
        for (var s = 0.0; s < length; s += 10) {
          canvas.drawLine(l.start + u * s, l.start + u * (s + 6 < length ? s + 6 : length), paint);
        }
      } else {
        canvas.drawLine(l.start, l.end, paint);
      }
      final badge = l.opacity < .5 ? .1 : 1.0;
      final c = Offset.lerp(l.start, l.end, .62)!;
      final text = TextPainter(text: TextSpan(text: l.label,
          style: TextStyle(color: Colors.white.withValues(alpha: badge), fontSize: 9, fontFamily: 'Oswald')),
        textDirection: TextDirection.ltr)..layout();
      final rect = RRect.fromRectAndRadius(
        Rect.fromCenter(center: c, width: (text.width + 8).clamp(18.0, 60.0), height: 18), const Radius.circular(9));
      canvas.drawRRect(rect, Paint()..color = l.fill.withValues(alpha: badge));
      canvas.drawRRect(rect, Paint()..color = l.color.withValues(alpha: badge)..style = PaintingStyle.stroke..strokeWidth = 2);
      text.paint(canvas, c - Offset(text.width / 2, text.height / 2));
    }
  }

  @override
  bool shouldRepaint(_TableThreadsPainter oldDelegate) => true;
}
