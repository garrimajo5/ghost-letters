part of 'game_screen.dart';

/// Новый стол по наброску владельца. Боком: меню слева сверху, лесенка подсказок
/// по рядам, поле с вертикальными подписями (Тайна, Мотив, Способ, Место), справа
/// игроки, главная кнопка и чат, снизу веер руки. Вертикально — игроки сверху,
/// кнопка и чат под веером. Ход партии (панель) открывается глазиком на месте стола;
/// выбор карт и игроков при этом сохраняется — он хранится в экране партии.
class _MainTable extends StatefulWidget {
  const _MainTable({required this.screen, this.seance = false});

  final GameScreenState screen;

  /// Сборка «Досье»: спиритический сеанс — круглый стол, сектора подсказок, игроки по кругу.
  final bool seance;

  @override
  State<_MainTable> createState() => _MainTableState();
}

class _MainTableState extends State<_MainTable> {
  /// null — по фазе: панель сама открывается там, где действие только в ней.
  bool? _panel;
  String? _phase;
  final _say = _Say();
  late final _typing = _Typing(screen);
  bool _emoji = false;
  bool _dragging = false;

  GameScreenState get screen => widget.screen;

  @override
  void initState() {
    super.initState();
    _say.addListener(_refresh);
    _typing.sync(initial: true);
    _typing.addListener(_refresh);
    screen.chatChanges.addListener(_chat);
    screen.chatChanges.addListener(_refresh);
    screen.reactionChanges.addListener(_refresh);
  }

  void _chat() => _typing.sync();

  void _refresh() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    screen.chatChanges.removeListener(_refresh);
    screen.chatChanges.removeListener(_chat);
    _typing.dispose();
    screen.reactionChanges.removeListener(_refresh);
    _say.dispose();
    super.dispose();
  }

  // Охота на Свидетеля видна всем: панель показывает, кого ищет команда Убийцы.
  static bool _panelPhase(GameView v) => v.can('RevealHints') ||
      const {'VoteTie', 'Hunt', 'BlackmailerHunt', 'AwardNomination', 'AwardVoting', 'Finished'}.contains(v.phase);

  bool _panelOpen(GameView v) => _panel ?? _panelPhase(v);

  void _toggle(GameView v) => setState(() => _panel = !_panelOpen(v));

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    if (_phase != v.phase) {
      _phase = v.phase;
      _panel = null;
      _say.reset();
      _typing.reset();
    }
    final size = MediaQuery.sizeOf(context);
    final landscape = size.width > size.height;
    final night = v.phase == 'Night' && (v.can('ChooseTruth') || v.can('TeamSuggest'));
    final open = _panelOpen(v);
    final stage = v.finale?.currentStage;
    final suspectVote = stage != null && !stage.isRow;
    final seance = widget.seance;
    final board = suspectVote
        ? _SuspectBoard(screen: screen)
        : _TableArea(screen: screen, landscape: landscape, say: _say, staircase: !seance, typing: _typing, maxCard: seance ? 130 : 260);
    final Widget center;
    if (open && v.can('RevealHints') && _panel == null) {
      // Призрак выбирает подсказки, глядя на стол: письма сверху, под ними — поле с истинными уликами.
      center = LayoutBuilder(builder: (context, box) => Column(children: [
            ConstrainedBox(
              constraints: BoxConstraints(maxHeight: box.maxHeight * 0.42),
              child: SingleChildScrollView(
                key: const Key('main-panel'),
                controller: screen._scroll,
                padding: const EdgeInsets.fromLTRB(12, 4, 12, 4),
                child: KeyedSubtree(key: screen._panelKey, child: ActionPanel(screen: screen)),
              ),
            ),
            Expanded(child: board),
          ]));
    } else if (open) {
      center = _panelView(v);
    } else if (!seance) {
      center = board;
    } else {
      final circle = _SeanceCircle(screen: screen);
      center = landscape
          // Большой экран: круг и поле рядом, по центру, в спокойном масштабе — как в прототипе.
          ? LayoutBuilder(builder: (context, box) {
              final side = [box.maxHeight, box.maxWidth * .45, 560.0].reduce((a, b) => a < b ? a : b).floorToDouble();
              return Center(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 1360),
                  child: Row(crossAxisAlignment: CrossAxisAlignment.center, children: [
                    SizedBox(width: side, height: side, child: circle),
                    const SizedBox(width: 16),
                    Expanded(child: SizedBox(height: box.maxHeight, child: board)),
                  ]),
                ),
              );
            })
          : LayoutBuilder(builder: (context, box) {
              final side = (box.maxHeight * 0.44 < box.maxWidth ? box.maxHeight * 0.44 : box.maxWidth).floorToDouble();
              return Column(children: [
                SizedBox(height: side, child: circle),
                Expanded(child: board),
              ]);
            });
    }
    final hand = v.me != null;
    // Большой экран: веер крупнее, чтобы рука не терялась внизу.
    final fanHeight = landscape ? (size.height * 0.2).clamp(56.0, 150.0) : 96.0;
    final dropCenter = _dropZone(v, center);
    return ScrollConfiguration(
      // Никаких полос прокрутки: ленты листаются пальцем или мышью.
      behavior: ScrollConfiguration.of(context).copyWith(scrollbars: false, dragDevices: PointerDeviceKind.values.toSet()),
      child: Scaffold(
        key: Key(seance ? 'seance-table' : 'main-table'),
        backgroundColor: seance ? _SeanceColors.bg : night ? AppColors.night : AppColors.bg,
        body: SafeArea(
          child: Stack(children: [
            Positioned.fill(child: Column(children: [
            const ConnectionBanner(),
            _TopBar(screen: screen, panelOpen: open, seance: seance, onEye: () => _toggle(v)),
            if (seance)
              Expanded(child: dropCenter)
            else if (landscape)
              Expanded(
                child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  Expanded(child: dropCenter),
                  SizedBox(
                    width: (size.width * 0.24).clamp(150.0, 260.0),
                    child: Column(children: [
                      Expanded(child: _PlayersColumn(screen: screen)),
                      Padding(
                        padding: const EdgeInsets.fromLTRB(6, 4, 8, 6),
                        child: _Actions(screen: screen, onStatus: () => setState(() => _panel = true)),
                      ),
                    ]),
                  ),
                ]),
              )
            else ...[
              _PlayersRow(screen: screen),
              Expanded(child: dropCenter),
            ],
            if (hand)
              _Fan(
                screen: screen,
                say: _say,
                onEmoji: () => setState(() => _emoji = !_emoji),
                onDrag: (on) => setState(() => _dragging = on),
                height: fanHeight,
              ),
            if (!landscape || seance)
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 4, 12, 10),
                child: _Actions(screen: screen, row: true, onStatus: () => setState(() => _panel = true)),
              ),
            ])),
            // Эмодзи проплывают по столу снизу вверх и не мешают нажатиям.
            Positioned.fill(child: IgnorePointer(child: _FlyingReactions(screen: screen))),
            // Выбор эмодзи — прямо над картой-смайликом (может перекрыть стол — это нестрашно).
            if (_emoji)
              Positioned(
                right: 8,
                bottom: fanHeight + (!landscape || seance ? 70 : 0) - 6,
                child: _EmojiBar(screen: screen, onClose: () => setState(() => _emoji = false)),
              ),
          ]),
        ),
      ),
    );
  }

  /// Что сделает карта, вытащенная из веера на стол; null — сейчас сыграть нечего.
  String? _dropLabel(GameView v) {
    if (_say.active) return 'Кидал эту';
    if (v.can('SendLetter')) return lettersPerPlayer(v) > 1 && screen.selectedHand.isEmpty ? 'Первое письмо' : 'Отправить письмо';
    if (v.can('GiveFirstClue')) return 'Выложить зацепку';
    if (v.can('Discard')) return 'Сбросить карту';
    return null;
  }

  void _played(GameView v, String card) {
    if (_say.active) {
      if (!_say.isSource(card)) _say.pick(card, hint: false);
      return;
    }
    if (_dropLabel(v) == null) return;
    final limit = v.can('SendLetter') ? lettersPerPlayer(v) : 1;
    if (!screen.selectedHand.contains(card)) _toggleHand(screen, card, limit);
    // Набрали нужное число карт — ход сделан той же кнопкой, что и внизу.
    if (screen.selectedHand.length == limit) ActionPanel.cta(screen)?.onPressed?.call();
  }

  Widget _dropZone(GameView v, Widget child) => DragTarget<String>(
        onWillAcceptWithDetails: (_) => _dropLabel(v) != null,
        onAcceptWithDetails: (d) => _played(v, d.data),
        builder: (context, candidates, _) {
          final over = candidates.isNotEmpty;
          final label = _dropLabel(v);
          return Stack(children: [
            Positioned.fill(child: child),
            // Пока карту тянут, посреди стола — куда её нести: почтовый ящик, мусорка, зацепка.
            if ((_dragging || over) && label != null)
              Positioned.fill(
                child: IgnorePointer(
                  child: AnimatedContainer(
                    key: over ? const Key('drop-zone') : const Key('drop-hint'),
                    duration: const Duration(milliseconds: 150),
                    margin: const EdgeInsets.all(6),
                    decoration: BoxDecoration(
                      color: AppColors.bg.withValues(alpha: over ? .55 : .35),
                      borderRadius: BorderRadius.circular(16),
                      border: Border.all(color: AppColors.amber.withValues(alpha: over ? 1 : .5), width: over ? 3 : 2),
                    ),
                    alignment: Alignment.center,
                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                      AnimatedScale(
                        duration: const Duration(milliseconds: 150),
                        scale: over ? 1.15 : 1,
                        child: Icon(_dropIcon(v), key: Key('drop-icon-${_dropIcon(v).codePoint}'), size: 96,
                            color: over ? AppColors.amberLight : AppColors.amber.withValues(alpha: .8)),
                      ),
                      const SizedBox(height: 6),
                      Text(label, style: heading(20, color: AppColors.amberLight, spacing: 1)),
                    ]),
                  ),
                ),
              ),
          ]);
        },
      );

  IconData _dropIcon(GameView v) {
    if (_say.active) return Icons.record_voice_over_outlined;
    if (v.can('SendLetter')) return Icons.markunread_mailbox_outlined;
    if (v.can('Discard')) return Icons.delete_outline;
    if (v.can('GiveFirstClue')) return Icons.style_outlined;
    return Icons.pan_tool_outlined;
  }

  Widget _panelView(GameView v) => ListView(
        key: const Key('main-panel'),
        controller: screen._scroll,
        padding: const EdgeInsets.fromLTRB(12, 4, 12, 16),
        children: [
          KeyedSubtree(key: screen._panelKey, child: ActionPanel(screen: screen)),
          TableStatements(screen: screen),
          if (v.me != null && v.me!.letters.isNotEmpty && !isFinale(v)) ...[
            const SizedBox(height: 10),
            _MyLetters(screen: screen),
          ],
        ],
      );
}

/// Верхняя строка: меню слева, раунд и фаза, таймер, глазик «стол / ход партии» всегда на одном месте.
class _TopBar extends StatelessWidget {
  const _TopBar({required this.screen, required this.panelOpen, required this.onEye, this.seance = false});

  final GameScreenState screen;
  final bool panelOpen;
  final bool seance;
  final VoidCallback onEye;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final mine = needsMe(v);
    final killerNight = v.phase == 'Night' && (v.can('ChooseTruth') || v.can('TeamSuggest'));
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 2, 4, 2),
      child: Row(children: [
        PopupMenuButton<String>(
          key: const Key('main-menu'),
          tooltip: 'Меню партии',
          icon: const Icon(Icons.menu, color: AppColors.muted),
          onSelected: (value) {
            switch (value) {
              case 'home':
                context.go('/');
              case 'audio':
                SoundSettingsSheet.show(context);
              case 'settings':
                screen.editSettings();
              case 'rules':
                context.push('/rules');
              case 'zoom':
                screen.setZoomed(true);
              case 'classic':
                screen.setClassicTable(true);
            }
          },
          itemBuilder: (_) => [
            const PopupMenuItem(value: 'audio', child: Text('Звук и музыка')),
            if (screen.isHost) const PopupMenuItem(value: 'settings', child: Text('Раунды, темп и таймеры')),
            if (v.board.isNotEmpty) const PopupMenuItem(value: 'zoom', child: Text('Стол крупно')),
            PopupMenuItem(value: 'classic', child: Text(seance ? 'Прежнее досье' : 'Классический стол')),
            const PopupMenuItem(value: 'rules', child: Text('Правила')),
            const PopupMenuItem(value: 'home', child: Text('На главную')),
          ],
        ),
        Text(
          seance
              ? (v.round <= 0 ? 'СЕАНС' : MediaQuery.sizeOf(context).width >= 700
                  ? 'СЕАНС · РАУНД ${v.round} ИЗ ${v.totalRounds}' : 'СЕАНС · ${v.round}/${v.totalRounds}')
              : v.round > 0 ? 'Р. ${v.round}/${v.totalRounds}' : 'ПАРТИЯ',
          key: const Key('round'),
          style: heading(15, spacing: 1),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Align(
            alignment: Alignment.centerLeft,
            child: Container(
            key: const Key('phase-pill'),
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            decoration: BoxDecoration(
              color: killerNight ? AppColors.red : (mine ? AppColors.amber : AppColors.surface2),
              borderRadius: BorderRadius.circular(99),
            ),
            child: Text(
              mine ? 'Ваш ход · ${T.shortPhase(v.phase)}' : T.shortPhase(v.phase),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 12, color: mine && !killerNight ? AppColors.onAmber : AppColors.text),
            ),
          ),
          ),
        ),
        Countdown(deadline: screen._snap!.deadline),
        IconButton(
          key: const Key('main-eye'),
          tooltip: panelOpen ? 'Показать стол' : 'Ход партии',
          icon: Icon(panelOpen ? Icons.visibility_outlined : Icons.list_alt, color: panelOpen ? AppColors.amber : AppColors.muted),
          onPressed: onEye,
        ),
      ]),
    );
  }
}

/// Лесенка подсказок слева и поле: подсказка раунда стоит на высоте своего ряда
/// (зацепка — Тайна, р. 1 — Мотив, р. 2 — Способ, р. 3 — Место), следующие ниже.
class _TableArea extends StatelessWidget {
  const _TableArea({required this.screen, required this.landscape, required this.say, this.staircase = true, this.typing, this.maxCard = 260});

  /// Предел размера карты поля (на сеансе поле скромнее — главное место у круга).
  final double maxCard;

  final GameScreenState screen;
  final bool landscape;
  final _Say say;
  final _Typing? typing;

  ChatMessage? get _current {
    if (!const {'Discussion', 'Voting', 'VoteTie'}.contains(screen.view!.phase)) return null;
    final typed = typing?.current;
    // Допечатанная чужая реплика уступает строку, когда слово перешло ко мне.
    if (typed != null && typed.round == screen.view!.round && (typing!.busy || !_myFloorFree(screen))) return typed;
    return _statement(screen);
  }

  /// Лесенка подсказок слева; на сеансе подсказки — в секторах круга.
  final bool staircase;

  /// Отметка карты поля: свой показ (пока не отправлен) или последнее заявление говорящего.
  /// Цвета связей показа: у каждой улики свой цвет (первая — зелёная), карты поля, на которые она указывает, —
  /// того же цвета; на карту указывают несколько улик — рамка из нескольких цветов. Исключённая — красная
  /// (при нескольких уликах — вместе с цветом улики, которая её исключила).
  List<Color> _links(String id, ChatMessage? statement) {
    if (say.active || statement == null) return const [];
    final sources = [for (var i = 0; i < statement.cardIds.length; i++) if (theorySource(statement.noteFor(i) ?? '')) i];
    final out = <Color>[];
    void add(Color c) {
      if (!out.contains(c)) out.add(c);
    }
    for (var i = 0; i < statement.cardIds.length; i++) {
      if (statement.cardIds[i] != id) continue;
      if (typing?.current?.id == statement.id && !typing!.isRevealed(i)) continue;
      final note = statement.noteFor(i) ?? '';
      final k = sources.indexOf(i);
      if (k >= 0) {
        add(linkColor(k));
        continue;
      }
      // noteFor убирает «:N» — номер улики читаем из исходной заметки.
      final raw = i < statement.cardNotes.length ? statement.cardNotes[i] : '';
      final link = RegExp(r':(\d+)$').firstMatch(raw);
      final n = link != null ? int.parse(link[1]!) : (sources.length == 1 ? 0 : -1);
      if (theoryNegative(note)) {
        add(AppColors.redBright);
        if (sources.length > 1 && n >= 0 && n < sources.length) add(linkColor(n));
      } else {
        add(n >= 0 && n < sources.length ? linkColor(n) : AppColors.believed);
      }
    }
    return out;
  }

  Color? _mark(String id, ChatMessage? statement) {
    if (say.active) {
      final m = say.marks[id];
      return m == null ? null : (m.$1 ? AppColors.believed : AppColors.redBright);
    }
    if (statement == null) return null;
    final i = statement.cardIds.indexOf(id);
    if (i < 0) return null;
    // Подсветка — когда реплика до карты дошла.
    if (typing?.current?.id == statement.id && !typing!.isRevealed(i)) return null;
    final note = statement.noteFor(i) ?? '';
    if (theorySource(note)) return null;
    return theoryNegative(note) ? AppColors.redBright : AppColors.believed;
  }

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final rows = v.board.isEmpty ? 4 : v.board.length;
    final columns = v.board.isEmpty ? 5 : v.board.first.cards.length;
    final hintCards = staircase ? v.hints.fold<int>(1, (m, h) => h.cards.length > m ? h.cards.length : m).clamp(1, 2) : 0;
    return LayoutBuilder(builder: (context, box) {
      const gap = _Board.gap;
      // Вертикально улики Призрака — полосой под полем (ниже — разговор), сбоку — столбиком слева.
      final below = !landscape && staircase;
      final side = below ? 0 : hintCards;
      // Ширина: подсказки сбоку (до двух карт по 0.8) + подпись + столбцы поля.
      final labels = staircase && !below ? _HintStep.labelWidth : 0.0;
      final byWidth = (box.maxWidth - 16 - _Board.verticalLabel - gap * columns - (side * 3 + 6) - labels) / (columns + side * 0.8);
      final stage = v.finale?.currentStage;
      final voting = stage != null && stage.isRow && const {'Voting', 'VoteTie'}.contains(v.phase);
      // Вертикально под полем много места: там идёт разговор — пузырями, как в мессенджере.
      final dialog = !landscape && staircase && const {'Discussion', 'Voting', 'VoteTie'}.contains(v.phase);
      final stripHeight = voting || say.active || (!dialog && _current != null) || _canSay(v) ? _SayStrip.height : 0.0;
      // Все ряды — в высоту без прокрутки и вертикально тоже: на сеансе под кругом места мало, и четвёртый ряд уходил вниз.
      // С разговором поле берёт две трети высоты под строкой, разговор — треть (см. flex ниже).
      final usable = dialog ? (box.maxHeight - stripHeight) * 2 / 3 : box.maxHeight - stripHeight;
      final byHeight = box.maxHeight.isFinite ? (usable - 12 - (below ? 10 : 0)) / (rows + (below ? 0.75 : 0)) - gap : double.infinity;
      final card = (byWidth < byHeight ? byWidth : byHeight).clamp(28.0, maxCard).floorToDouble();
      // Подпись ступеньки ~14 dp — карта подсказки умещается в высоту ряда.
      // Подсказки почти в рост ряда, подпись раунда — поверх карты: ступеньки идут вплотную.
      final hint = (card * (below ? 0.75 : 0.8)).floorToDouble();
      final hintsWidth = staircase && !below ? hintCards * (hint + 3) + 6 + labels : 0.0;
      final order = _Board.tableOrder(v);
      final statement = _current;
      final strip = voting || say.active || (!dialog && statement != null) || _canSay(v);
      final board = SingleChildScrollView(
        key: const Key('main-board'),
        padding: const EdgeInsets.fromLTRB(4, 2, 8, 8),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
        Row(mainAxisAlignment: MainAxisAlignment.center, crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (staircase && !below) SizedBox(
            width: hintsWidth,
            child: Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              // Подсказки раунда относятся ко всему столу, а не к ряду: столбик вплотную, без сетки поля.
              for (final group in v.hints)
                Padding(
                  padding: const EdgeInsets.only(bottom: 3),
                  child: SizedBox(height: hint, child: _HintStep(screen: screen, say: say, hint: group, size: hint, links: (id) => _links(id, statement))),
                ),
              if (v.vanishedCount > 0)
                Padding(
                  padding: const EdgeInsets.only(top: 4, right: 4),
                  child: Text('исчезло ×${v.vanishedCount}', key: const Key('main-vanished'), style: const TextStyle(fontSize: 10, color: AppColors.dim)),
                ),
            ]),
          ),
          // Поле ровно по своим картам — по центру свободного места, лесенка вплотную слева.
          Flexible(
            child: SizedBox(
            width: _Board.verticalLabel + (card + gap) * columns + 1,
            child: _Board(
              screen: screen,
              maxCard: card,
              order: order,
              vertical: true,
              focusRow: voting ? stage.row : null,
              reveal: v.truth != null && const {'AwardNomination', 'AwardVoting', 'Finished'}.contains(v.phase),
              decorate: (id, child) {
                final color = _mark(id, statement);
                // На сеансе исключённая карта — тёмно-красная вуаль с крестом, как в прототипе.
                Widget out = color == null
                    ? child
                    : !staircase && color == AppColors.redBright
                    ? Stack(key: Key('say-mark-$id'), children: [
                        child,
                        Positioned.fill(
                          child: IgnorePointer(
                            child: Container(
                              margin: const EdgeInsets.all(2),
                              decoration: BoxDecoration(color: const Color(0x66B0261E), borderRadius: BorderRadius.circular(9)),
                              child: const Center(child: Icon(Icons.close, color: Colors.white, size: 26)),
                            ),
                          ),
                        ),
                      ])
                    : _LinkBorder(key: Key('say-mark-$id'), colors: say.active ? [color] : _links(id, statement).ifEmpty(color), child: child);
                // Показ на столе: нажатие — зелёная («улика указывает»), ещё раз — красная («не эта»), ещё — снять.
                if (say.active) out = GestureDetector(onTap: () => say.cycle(id), child: out);
                return out;
              },
            ),
            ),
          ),
        ]),
        if (below && v.hints.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: SingleChildScrollView(
              key: const Key('main-hints-below'),
              scrollDirection: Axis.horizontal,
              child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
                for (final group in v.hints)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: SizedBox(
                      height: hint,
                      width: _HintStep.labelWidth + (group.cards.isEmpty ? 1 : group.cards.length) * (hint + 3) + 4,
                      child: _HintStep(screen: screen, say: say, hint: group, size: hint, links: (id) => _links(id, statement)),
                    ),
                  ),
                if (v.vanishedCount > 0)
                  Padding(
                    padding: const EdgeInsets.only(left: 4, bottom: 2),
                    child: Text('исчезло ×${v.vanishedCount}', key: const Key('main-vanished'), style: const TextStyle(fontSize: 10, color: AppColors.dim)),
                  ),
              ]),
            ),
          ),
        ]),
      );
      if (dialog) {
        return Column(children: [
          if (strip)
            if (voting) _VoteStrip(screen: screen, stage: stage) else _SayStrip(screen: screen, say: say, statement: null, typing: typing),
          Flexible(flex: 2, child: board),
          Expanded(child: _Dialog(screen: screen, typing: typing)),
        ]);
      }
      if (!strip) return board;
      return Column(children: [
        if (voting) _VoteStrip(screen: screen, stage: stage) else _SayStrip(screen: screen, say: say, statement: statement, typing: typing),
        Expanded(child: board),
      ]);
    });
  }
}

/// Ступенька лесенки: подпись раунда и его подсказки; лишние карты листаются свайпом.
class _HintStep extends StatelessWidget {
  const _HintStep({required this.screen, required this.say, required this.hint, required this.size, this.links});

  final GameScreenState screen;
  final _Say say;
  final HintGroup hint;
  final double size;
  /// Цвет улики в показе говорящего — тот же, что у карт поля, на которые она указывает.
  final List<Color> Function(String id)? links;

  /// Ширина полосы с подписью раунда слева от карт.
  static const labelWidth = 11.0;

  @override
  Widget build(BuildContext context) {
    final label = hint.round == 0 ? 'зацепка' : 'р. ${hint.round}';
    // Подпись раунда — узкой полосой слева от карт, а не поверх них: картинку ничто не заслоняет.
    return Row(mainAxisSize: MainAxisSize.min, mainAxisAlignment: MainAxisAlignment.end, children: [
      SizedBox(
        width: labelWidth,
        height: size,
        child: RotatedBox(
          quarterTurns: 3,
          child: FittedBox(
            fit: BoxFit.scaleDown,
            child: Text(label, key: Key('hint-label-${hint.round}'), maxLines: 1, style: const TextStyle(fontSize: 9, color: AppColors.ice)),
          ),
        ),
      ),
      Flexible(child: SizedBox(
        height: size,
        child: hint.cards.isEmpty
            ? Container(
                width: size,
                margin: const EdgeInsets.only(right: 4),
                decoration: BoxDecoration(borderRadius: BorderRadius.circular(8), border: Border.all(color: AppColors.border)),
                alignment: Alignment.center,
                child: const Text('—', style: TextStyle(fontSize: 11, color: AppColors.dim)),
              )
            : ListView(
                scrollDirection: Axis.horizontal,
                reverse: true,
                shrinkWrap: true,
                children: [
                  for (final c in hint.cards.reversed)
                    Padding(
                      padding: const EdgeInsets.only(right: 3),
                      child: GestureDetector(
                        key: Key('hint-$c'),
                        onTap: () => say.active ? say.pick(c, hint: true) : HintSheet.show(context, screen, c, hint.round),
                        onLongPress: () => showCardZoom(context, c),
                        child: Stack(clipBehavior: Clip.none, children: [
                          Container(
                            foregroundDecoration: say.active && say.isSource(c)
                                ? BoxDecoration(border: Border.all(color: AppColors.amber, width: 3), borderRadius: BorderRadius.circular(7))
                                : null,
                            child: say.active || (links?.call(c) ?? const []).isEmpty
                                ? CardImage(cardId: c, size: size, radius: 7)
                                : _LinkBorder(key: Key('hint-link-$c'), colors: links!(c), radius: 7, child: CardImage(cardId: c, size: size, radius: 7)),
                          ),
                          if (screen.marks[c]?.claimedBy case final who?)
                            Positioned(
                              right: -3,
                              bottom: -3,
                              child: Avatar(nickname: screen.nick(who), color: screen.colorOf(who), photoId: screen.photoOf(who), size: 16),
                            ),
                        ]),
                      ),
                    ),
                ],
              ),
      )),
    ]);
  }
}

/// Кто первым говорил в этом раунде: автор первой реплики раунда, до неё — у кого рация.
String? _firstSpeaker(GameScreenState screen) {
  final v = screen.view!;
  for (final m in screen.chat) {
    if (m.channel == 'public' && m.round == v.round && m.authorId != null) return m.authorId;
  }
  return v.isRadio ? v.radioHolder : null;
}

/// Игроки справа: фото, имя, Призрак, бот, кто говорит (зелёным) и кто говорил первым.
/// Если все не помещаются, строки сжимаются до кружков у края.
class _PlayersColumn extends StatelessWidget {
  const _PlayersColumn({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final players = [...v.players]..sort((a, b) => a.seat.compareTo(b.seat));
    final first = _firstSpeaker(screen);
    return LayoutBuilder(builder: (context, box) {
      // Строка игрока ~46 dp; не помещаются все — сжимаем до кружков у края, по несколько в ряд.
      final circles = (box.maxHeight - 8) / (players.isEmpty ? 1 : players.length) < 46;
      return SingleChildScrollView(
        key: const Key('main-players'),
        padding: const EdgeInsets.fromLTRB(4, 4, 6, 4),
        child: circles
            ? Wrap(alignment: WrapAlignment.end, spacing: 6, children: [
                for (final p in players)
                  SizedBox(width: 42, child: _SeatTile(screen: screen, player: p, first: first == p.id, compact: true, names: false)),
              ])
            : Column(children: [
                for (final p in players) _SeatTile(screen: screen, player: p, first: first == p.id, compact: false),
              ]),
      );
    });
  }
}

/// Вертикальный стол: игроки строкой сверху, лишние листаются.
class _PlayersRow extends StatelessWidget {
  const _PlayersRow({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final players = [...v.players]..sort((a, b) => a.seat.compareTo(b.seat));
    final first = _firstSpeaker(screen);
    return SizedBox(
      height: 70,
      child: ListView(
        key: const Key('main-players'),
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 8),
        children: [
          for (final p in players)
            SizedBox(width: 62, child: _SeatTile(screen: screen, player: p, first: first == p.id, compact: true)),
        ],
      ),
    );
  }
}

class _SeatTile extends StatelessWidget {
  const _SeatTile({required this.screen, required this.player, required this.first, required this.compact, this.names = true});

  final GameScreenState screen;
  final PlayerInfo player;
  final bool first;
  final bool compact;

  /// Кружок без подписи — когда игроки не помещаются (имя — во всплывающей подсказке).
  final bool names;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final p = player;
    final isMe = p.id == v.me?.id;
    final ghost = p.isGhost || p.knownRole == 'Ghost';
    final bot = screen.isBot(p.id);
    final speaking = v.currentSpeaker == p.id;
    final selected = screen.target == p.id;
    final arrested = v.finale?.arrested.contains(p.id) == true;
    final ring = selected ? AppColors.amber : (speaking ? AppColors.believed : Colors.transparent);
    final name = isMe ? 'Вы' : screen.nick(p.id);
    final meta = [
      if (ghost) 'Призрак',
      if (bot) 'бот',
      if (first) 'первым',
      if (!ghost && !isMe && p.knownRole != null) T.role(p.knownRole),
    ].join(' · ');
    final avatar = Stack(clipBehavior: Clip.none, children: [
      Container(
        padding: const EdgeInsets.all(2),
        decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: ring, width: 2)),
        child: Opacity(
          opacity: arrested ? 0.5 : 1,
          child: Avatar(
            nickname: screen.nick(p.id),
            color: screen.colorOf(p.id),
            photoId: screen.photoOf(p.id),
            size: compact ? 30 : 34,
            ring: isMe ? AppColors.amber : (ghost ? AppColors.ice : null),
          ),
        ),
      ),
      if (ghost) Positioned(left: -5, bottom: -4, child: GhostBadge(key: Key('ghost-badge-${p.id}'), size: 16)),
      if (bot)
        Positioned(
          right: -5,
          top: -4,
          child: Container(
            key: Key('bot-badge-${p.id}'),
            padding: const EdgeInsets.all(1.5),
            decoration: const BoxDecoration(color: AppColors.surface2, shape: BoxShape.circle),
            child: const Icon(Icons.smart_toy_outlined, size: 12, color: AppColors.ice),
          ),
        ),
      if (p.hasActed)
        const Positioned(right: -3, bottom: -3, child: Icon(Icons.check_circle, size: 14, color: AppColors.ice)),
      if (first)
        Positioned(left: -5, top: -4, child: CountBadge(key: Key('first-${p.id}'), text: '1', color: AppColors.green)),
      if (arrested) const Positioned(left: -4, bottom: -2, child: Icon(Icons.lock, size: 14, color: AppColors.redBright)),
      if (screen.reactionCount(p.id) case final n when n > 0)
        Positioned(
          right: -12,
          bottom: -8,
          child: Container(
            key: Key('react-badge-${p.id}'),
            padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
            decoration: BoxDecoration(color: AppColors.surface2, borderRadius: BorderRadius.circular(99), border: Border.all(color: AppColors.bg)),
            child: Text('${screen.reactions.lastWhere((r) => r.userId == p.id).emoji}${n > 1 ? '×$n' : ''}',
                style: const TextStyle(fontSize: 11)),
          ),
        ),
    ]);
    final nameStyle = TextStyle(
      fontSize: 12,
      color: speaking ? AppColors.believed : (selected ? AppColors.amber : (isMe ? AppColors.amber : AppColors.text)),
      fontWeight: speaking || selected ? FontWeight.w600 : null,
    );
    return GestureDetector(
      key: Key('player-${p.id}'),
      behavior: HitTestBehavior.opaque,
      onTap: () => screen.tapPlayer(p.id),
      child: Semantics(
        label: [name, if (meta.isNotEmpty) meta, if (speaking) 'говорит'].join(', '),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 3),
          child: compact
              ? names
                  ? Column(mainAxisSize: MainAxisSize.min, children: [
                      avatar,
                      const SizedBox(height: 2),
                      Text(name, maxLines: 1, overflow: TextOverflow.ellipsis, style: nameStyle.copyWith(fontSize: 10)),
                    ])
                  : Tooltip(message: name, child: avatar)
              : Row(children: [
                  avatar,
                  const SizedBox(width: 8),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                      Text(name, maxLines: 1, overflow: TextOverflow.ellipsis, style: nameStyle),
                      if (meta.isNotEmpty)
                        Text(meta, maxLines: 1, overflow: TextOverflow.ellipsis,
                            style: TextStyle(fontSize: 10, color: ghost ? AppColors.ice : AppColors.dim)),
                    ]),
                  ),
                ]),
        ),
      ),
    );
  }
}

/// Главная кнопка хода (или строка ожидания) и чат — под игроками.
class _Actions extends StatelessWidget {
  const _Actions({required this.screen, required this.onStatus, this.row = false});

  final GameScreenState screen;
  final VoidCallback onStatus;
  final bool row;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final cta = ActionPanel.cta(screen);
    final main = cta != null
        ? _Pulse(
            pulse: screen.turnPulse,
            child: FilledButton.icon(
              key: const Key('cta'),
              style: cta.danger ? FilledButton.styleFrom(backgroundColor: AppColors.red, foregroundColor: Colors.white) : null,
              onPressed: cta.onPressed,
              icon: Icon(cta.icon ?? Icons.arrow_forward, size: 18),
              // Узкая колонка: надпись ужимается, а не рвётся посреди слова.
              label: FittedBox(fit: BoxFit.scaleDown, child: Text(cta.label.toUpperCase(), maxLines: 1)),
            ),
          )
        : GestureDetector(onTap: onStatus, child: _StatusBar(text: actionHint(v), mine: needsMe(v), pulse: screen.turnPulse));
    // После результата можно сразу выйти: награды и разбор Призрака не держат за столом.
    final over = const {'AwardNomination', 'AwardVoting', 'Finished'}.contains(v.phase);
    return Row(children: [
      if (over) ...[
        IconButton.filledTonal(
          key: const Key('main-home'),
          tooltip: 'В главное меню',
          onPressed: () => context.go('/'),
          icon: const Icon(Icons.home_outlined),
        ),
        const SizedBox(width: 6),
      ],
      Expanded(child: main),
      SizedBox(width: row ? 10 : 6),
      _ChatButton(screen: screen),
    ]);
  }
}

/// Рука веером снизу: выбранная карта приподнята; долгое нажатие — карта крупно.
class _Fan extends StatelessWidget {
  const _Fan({required this.screen, required this.say, required this.onEmoji, required this.height, this.onDrag});

  final GameScreenState screen;
  final _Say say;
  final VoidCallback onEmoji;

  /// Карту тянут из веера (true) или отпустили (false) — стол показывает, куда её нести.
  final ValueChanged<bool>? onDrag;
  final double height;

  /// Карту можно вытащить из веера на стол — как настоящую: отпустили над столом — сыграна.
  Widget _draggable(String id, double card, {required bool enabled, required Widget child}) {
    if (!enabled) return child;
    return Draggable<String>(
      data: id,
      feedback: Transform.rotate(
        angle: -0.08,
        child: Container(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(11),
            boxShadow: const [BoxShadow(color: Colors.black87, blurRadius: 18, offset: Offset(0, 8))],
          ),
          child: CardImage(cardId: id, size: card * 1.3, radius: 11),
        ),
      ),
      childWhenDragging: Opacity(opacity: 0.25, child: child),
      onDragStarted: () => onDrag?.call(true),
      onDragEnd: (_) => onDrag?.call(false),
      child: child,
    );
  }

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final hand = isFinale(v) ? const <String>[] : v.me!.hand;
    final selectable = v.can('SendLetter') || v.can('Discard') || v.can('GiveFirstClue');
    final limit = v.can('SendLetter') ? lettersPerPlayer(v) : 1;
    final card = height - 20;
    // Во время показа на столе карта из веера — «кидал эту».
    bool raised(String c) => say.active ? say.isSource(c) : screen.selectedHand.contains(c);
    return SizedBox(
      key: const Key('main-fan'),
      height: height,
      child: LayoutBuilder(builder: (context, box) {
        final n = hand.length;
        // Справа от веера — карта-эмодзи.
        final room = box.maxWidth - card * 0.8 - 12;
        final step = n <= 1 ? 0.0 : ((room - card - 24) / (n - 1)).clamp(12.0, card + 6);
        final width = card + step * (n - 1);
        final left = ((room - width) / 2).clamp(4.0, double.infinity);
        final mid = (n - 1) / 2;
        // Призрак говорит только картами: до итогов реакций у него нет.
        final silent = v.me?.role == 'Ghost' && !const {'AwardNomination', 'AwardVoting', 'Finished'}.contains(v.phase);
        return Stack(clipBehavior: Clip.none, children: [
          if (!silent) Positioned(
            right: 8,
            top: 14,
            child: GestureDetector(
              key: const Key('fan-emoji'),
              onTap: onEmoji,
              child: Transform.rotate(
                angle: 0.12,
                child: Container(
                  width: card * 0.8,
                  height: card * 0.8,
                  decoration: BoxDecoration(
                    color: AppColors.surface2,
                    borderRadius: BorderRadius.circular(9),
                    border: Border.all(color: AppColors.border),
                  ),
                  alignment: Alignment.center,
                  child: Text('😊', style: TextStyle(fontSize: card * 0.4)),
                ),
              ),
            ),
          ),
          for (var i = 0; i < n; i++)
            Positioned(
              left: left + step * i,
              top: 12 + ((i - mid).abs() * 2.0) - (raised(hand[i]) ? 12 : 0),
              child: Transform.rotate(
                angle: (i - mid) * 0.06,
                alignment: Alignment.bottomCenter,
                child: GestureDetector(
                  key: Key('hand-${hand[i]}'),
                  onTap: say.active
                      ? () => say.pick(hand[i], hint: false)
                      : selectable
                          ? () => _toggleHand(screen, hand[i], limit)
                          : null,
                  onLongPress: () => showCardZoom(context, hand[i], caption: 'Карта на руке'),
                  child: _draggable(
                    hand[i],
                    card,
                    enabled: selectable || say.active,
                    child: Container(
                      foregroundDecoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(9),
                        border: Border.all(
                          color: raised(hand[i]) ? AppColors.amber : AppColors.border,
                          width: raised(hand[i]) ? 3 : 1,
                        ),
                      ),
                      decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(9),
                        boxShadow: const [BoxShadow(color: Colors.black54, blurRadius: 6, offset: Offset(0, 2))],
                      ),
                      child: Opacity(
                        opacity: selectable || say.active || !needsMe(v) ? 1 : 0.6,
                        child: CardImage(cardId: hand[i], size: card, radius: 9),
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ]);
      }),
    );
  }
}

/// Показ на столе, пока игрок его собирает: карты-источники (из веера — «кидал эту», подсказки — «улика»)
/// и карты поля — зелёные («указывает») и красные («не эта»), каждая связана со своим источником.
/// Источников может быть сколько угодно: можно связать со столом все подсказки Призрака.
class _Say extends ChangeNotifier {
  /// Сколько карт уходит в одно сообщение (как на сервере).
  static const maxCards = 12;

  bool active = false;
  final sources = <String>[];
  final _hints = <String>{};
  int current = -1;

  /// Карта поля → (зелёная?, индекс источника или −1).
  final marks = <String, (bool, int)>{};

  String? get source => current >= 0 && current < sources.length ? sources[current] : null;

  bool get hint => source != null && _hints.contains(source);

  bool isHint(String id) => _hints.contains(id);

  bool isSource(String id) => sources.contains(id);

  int get total => sources.length + marks.length;

  void start() {
    active = true;
    notifyListeners();
  }

  void reset() {
    active = false;
    sources.clear();
    _hints.clear();
    current = -1;
    marks.clear();
  }

  void stop() {
    reset();
    notifyListeners();
  }

  /// Нажатие на источник: новый — добавить и сделать текущим; текущий — убрать; другой — сделать текущим.
  void pick(String id, {required bool hint}) {
    final i = sources.indexOf(id);
    if (i < 0) {
      if (total >= maxCards) return;
      sources.add(id);
      if (hint) _hints.add(id);
      current = sources.length - 1;
    } else if (i == current) {
      sources.removeAt(i);
      _hints.remove(id);
      for (final e in marks.entries.toList()) {
        final (green, link) = e.value;
        marks[e.key] = (green, link == i ? -1 : link > i ? link - 1 : link);
      }
      current = sources.isEmpty ? -1 : sources.length - 1;
    } else {
      current = i;
    }
    notifyListeners();
  }

  void cycle(String id) {
    final m = marks[id];
    if (m == null) {
      if (total < maxCards) marks[id] = (true, current);
    } else if (m.$1) {
      marks[id] = (false, m.$2);
    } else {
      marks.remove(id);
    }
    notifyListeners();
  }

  List<String> get cards => [...sources, ...marks.keys];

  List<String> get notes => [
        for (final c in sources) _hints.contains(c) ? 'улика' : 'кидал',
        for (final (green, link) in marks.values) (green ? 'думаю, эта' : 'исключаю') + (link >= 0 ? ':$link' : ''),
      ];
}

/// Сообщения, собранные на столе, начинаются так — в чате они помечаются «со стола».
const tablePostPrefix = 'Со стола: ';

/// Может ли игрок сейчас показывать на столе: обсуждение, не Призрак.
bool _canSay(GameView v) => v.phase == 'Discussion' && v.me != null && v.me!.role != 'Ghost';

/// Последняя публичная реплика раунда: говорящего, а без него — любая (с картами или без).
ChatMessage? _statement(GameScreenState screen) {
  final v = screen.view!;
  if (!const {'Discussion', 'Voting', 'VoteTie'}.contains(v.phase)) return null;
  ChatMessage? last, speaker;
  for (final m in screen.chat) {
    if (m.channel != 'public' || m.authorId == null || m.round != v.round) continue;
    if (m.cardIds.isEmpty && (m.text ?? '').trim().isEmpty) continue;
    last = m;
    if (m.authorId == v.currentSpeaker) speaker = m;
  }
  // Слово у меня, а я ещё ничего не показал: строка свободна под крупную кнопку «Показать на столе»,
  // а не занята прошлой чужой репликой с маленьким значком.
  if (speaker == null && _myFloorFree(screen)) return null;
  return speaker ?? last;
}

bool _myFloorFree(GameScreenState screen) {
  final v = screen.view!;
  if (!v.isRadio || v.me == null || v.currentSpeaker != v.me!.id) return false;
  return !screen.chat.any((m) => m.channel == 'public' && m.round == v.round && m.authorId == v.me!.id);
}

/// Подпись карты поля для текста: «Мотив 2».
String _cardName(GameView v, String id) {
  for (final row in v.board) {
    final c = row.cards.indexOf(id);
    if (c >= 0) return '${T.category(row.category)} ${c + 1}';
  }
  return 'карта';
}

/// Текст показа для чата: люди читают его, боты — заметки к картам.
String _sayText(GameView v, _Say say) {
  String sourceName(int i) {
    final id = say.sources[i];
    if (!say.isHint(id)) return 'кидал эту карту';
    final round = v.hints.where((h) => h.cards.contains(id)).firstOrNull?.round;
    return round == null ? 'улика' : round == 0 ? 'зацепка' : 'улика р.$round';
  }

  String part(int link) {
    final green = [for (final e in say.marks.entries) if (e.value.$1 && e.value.$2 == link) _cardName(v, e.key)];
    final red = [for (final e in say.marks.entries) if (!e.value.$1 && e.value.$2 == link) _cardName(v, e.key)];
    final linked = link >= 0;
    final thrown = linked && !say.isHint(say.sources[link]);
    return [
      if (linked) sourceName(link),
      if (green.isNotEmpty) '${linked ? 'указывает на' : 'думаю, это'} ${green.join(', ')}',
      if (red.isNotEmpty) '${thrown ? 'её не вытащили — ' : ''}не ${red.join(', ')}',
    ].join(' ');
  }

  final parts = [for (var i = 0; i < say.sources.length; i++) part(i), if (say.marks.values.any((m) => m.$2 < 0)) part(-1)]
      .where((p) => p.isNotEmpty);
  return '$tablePostPrefix${parts.join('; ')}.';
}

/// Строка над полем: что говорящий показал на столе, а во время своего показа — источники и «Отправить».
class _SayStrip extends StatelessWidget {
  const _SayStrip({required this.screen, required this.say, required this.statement, this.typing});

  static const height = 46.0;

  final GameScreenState screen;
  final _Say say;
  final ChatMessage? statement;
  final _Typing? typing;

  Future<void> _send(BuildContext context) async {
    final v = screen.view!;
    final sent = await runAction(
      context,
      () => screen.ref.read(apiProvider).sendChat(v.gameId, _sayText(v, say), channel: 'public', cards: say.cards, cardNotes: say.notes),
    );
    if (sent != null) say.stop();
  }

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final children = <Widget>[];
    if (say.active) {
      final letter = v.me?.letters.where((l) => l.round == v.round).lastOrNull;
      children.addAll([
        for (final id in say.sources)
          Padding(
            padding: const EdgeInsets.only(right: 4),
            child: GestureDetector(
              key: Key('say-source-$id'),
              onTap: () => say.pick(id, hint: say.isHint(id)),
              child: Container(
                foregroundDecoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(6),
                  border: Border.all(color: id == say.source ? AppColors.amber : AppColors.border, width: id == say.source ? 2.5 : 1),
                ),
                child: CardImage(key: id == say.source ? const Key('say-source') : null, cardId: id, size: 36, radius: 6),
              ),
            ),
          ),
        if (say.sources.isEmpty && letter != null)
          GestureDetector(
            key: const Key('say-my-letter'),
            onTap: () => say.pick(letter.cardId, hint: false),
            child: Opacity(opacity: 0.7, child: CardImage(cardId: letter.cardId, size: 36, radius: 6)),
          ),
        const SizedBox(width: 4),
        Expanded(
          child: Text(
            say.source == null
                ? 'Улики слева или карта из веера; затем карты поля: раз — зелёная, два — красная'
                : '${say.hint ? 'Улика' : 'Кидал эту'} → карты поля: раз — указывает, два — не эта. Ещё улика — ещё связи',
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 11, color: AppColors.muted),
          ),
        ),
        IconButton(key: const Key('say-cancel'), tooltip: 'Отменить показ', icon: const Icon(Icons.close), onPressed: say.stop),
        FilledButton(
          key: const Key('say-send'),
          onPressed: say.total == 0 ? null : () => _send(context),
          child: const Text('На стол'),
        ),
      ]);
    } else {
      final m = statement;
      if (m != null) {
        final sources = [for (var i = 0; i < m.cardIds.length; i++) if (theorySource(m.noteFor(i) ?? '')) m.cardIds[i]];
        final typing = this.typing;
        children.addAll([
          Avatar(nickname: screen.nick(m.authorId), color: screen.colorOf(m.authorId), photoId: screen.photoOf(m.authorId), size: 26),
          const SizedBox(width: 6),
          for (final source in sources.take(3)) ...[
            GestureDetector(
              onTap: () => showCardZoom(context, source),
              child: CardImage(key: Key('say-shown-$source'), cardId: source, size: 36, radius: 6),
            ),
            const SizedBox(width: 4),
          ],
          Expanded(
            child: GestureDetector(
              onTap: typing?.complete,
              child: Text(
                typing?.current?.id == m.id ? _Typing.tail(screen, m, typing!.text) : _Typing.fullText(screen, m),
                key: const Key('say-statement'),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 12),
              ),
            ),
          ),
        ]);
      } else {
        children.add(const Spacer());
      }
      if (_canSay(v))
        // Рядом с чужим заявлением места мало — только значок; на пустой строке — с подписью.
        children.add(m != null
            ? IconButton(
                key: const Key('say-start'),
                tooltip: 'Показать на столе',
                onPressed: say.start,
                icon: const Icon(Icons.touch_app_outlined, color: AppColors.amber),
              )
            : TextButton.icon(
                key: const Key('say-start'),
                onPressed: say.start,
                icon: const Icon(Icons.touch_app_outlined, size: 18),
                label: const Text('Показать на столе'),
              ));
    }
    return Container(
      height: height,
      margin: const EdgeInsets.fromLTRB(6, 0, 8, 2),
      padding: const EdgeInsets.symmetric(horizontal: 6),
      decoration: BoxDecoration(
        color: say.active ? const Color(0xFF3A2B12) : AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: say.active ? AppColors.amber : AppColors.border),
      ),
      child: Row(children: children),
    );
  }
}

/// Голосование по ряду прямо на поле: какой ряд, сколько проголосовали, «Воздержаться».
class _VoteStrip extends StatelessWidget {
  const _VoteStrip({required this.screen, required this.stage});

  final GameScreenState screen;
  final VoteStage stage;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final finale = v.finale!;
    final voted = v.players.where((p) => p.hasActed).length;
    final can = v.can('CastVote');
    final label = 'Этап ${stage.index + 1} из ${finale.stagesTotal}: ${T.category(v.board[stage.row].category)}'
        '${stage.attempt > 1 ? ' · переголосование' : ''} — ${can ? 'выберите карту в подсвеченном ряду' : finale.hasMyVote ? 'ваш голос принят' : 'голосуют'}';
    return Container(
      key: const Key('vote-strip'),
      height: _SayStrip.height,
      margin: const EdgeInsets.fromLTRB(6, 0, 8, 2),
      padding: const EdgeInsets.symmetric(horizontal: 10),
      decoration: BoxDecoration(
        color: can ? const Color(0xFF3A2B12) : AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: can ? AppColors.amber : AppColors.border),
      ),
      child: Row(children: [
        const Icon(Icons.how_to_vote_outlined, size: 18, color: AppColors.amber),
        const SizedBox(width: 8),
        Expanded(child: Text(label, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12))),
        Text('$voted из ${v.players.length}', style: const TextStyle(fontSize: 11, color: AppColors.muted)),
        if (can)
          TextButton(
            key: const Key('vote-abstain'),
            onPressed: () => screen.send('CastVote', {'column': null, 'suspect': null}),
            child: const Text('Воздержаться'),
          ),
      ]),
    );
  }
}

/// Реплики за столом в человеческом темпе, как в визуальной новелле: печатаются по буквам со звуком,
/// карты подсвечиваются в тот момент, когда реплика до них доходит («Мотив 2»), остальные — по одной
/// после текста. Пришло несколько реплик подряд — идут по очереди, с паузой на чтение.
/// Разговор раунда под полем: чужие реплики слева с аватаром, свои — справа, как в мессенджере.
/// Реплику, которую ещё «печатают», видно по буквам; ждущие в очереди появятся, когда до них дойдёт.
class _Dialog extends StatelessWidget {
  const _Dialog({required this.screen, this.typing});

  final GameScreenState screen;
  final _Typing? typing;

  @override
  Widget build(BuildContext context) {
    final typing = this.typing;
    if (typing == null) return const SizedBox.shrink();
    return ListenableBuilder(
      listenable: typing,
      builder: (context, _) {
        final v = screen.view!;
        final queued = typing.queuedIds;
        final lines = [
          for (final m in screen.chat)
            if (m.channel == 'public' && m.authorId != null && m.round == v.round && !queued.contains(m.id) &&
                (m.cardIds.isNotEmpty || (m.text ?? '').trim().isNotEmpty || m.kind != 'text'))
              m,
        ];
        if (lines.isEmpty) return const SizedBox.shrink();
        return ListView.builder(
          key: const Key('main-dialog'),
          reverse: true,
          padding: const EdgeInsets.fromLTRB(10, 4, 10, 6),
          itemCount: lines.length,
          itemBuilder: (context, i) => _bubble(context, lines[lines.length - 1 - i], typing),
        );
      },
    );
  }

  Widget _bubble(BuildContext context, ChatMessage m, _Typing typing) {
    final v = screen.view!;
    final mine = m.authorId == v.me?.id;
    final typed = typing.current?.id == m.id && typing.busy;
    final text = (m.text ?? '').replaceFirst(tablePostPrefix, '');
    final Widget body = typed
        ? Text(typing.text.startsWith(_Typing.prefixOf(screen, m)) ? typing.text.substring(_Typing.prefixOf(screen, m).length) : typing.text,
            style: const TextStyle(fontSize: 13))
        : m.kind != 'text' && text.isEmpty
            ? const Text('🎤 голосовое', style: TextStyle(fontSize: 13))
            : ChatCardText(message: m, parts: ChatCardText.parse(m, v.board, v.hints), keyPrefix: 'dialog-inline');
    final bubble = Container(
      key: Key('dialog-${m.id}'),
      constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * .78),
      padding: const EdgeInsets.fromLTRB(10, 6, 10, 7),
      margin: const EdgeInsets.symmetric(vertical: 3),
      decoration: BoxDecoration(
        color: mine ? AppColors.amber.withValues(alpha: .16) : AppColors.surface2,
        border: Border.all(color: mine ? AppColors.amber.withValues(alpha: .4) : AppColors.border),
        borderRadius: BorderRadius.only(
          topLeft: const Radius.circular(12),
          topRight: const Radius.circular(12),
          bottomLeft: Radius.circular(mine ? 12 : 3),
          bottomRight: Radius.circular(mine ? 3 : 12),
        ),
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
        if (!mine)
          Text(screen.nick(m.authorId), style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: colorFromHex(screen.colorOf(m.authorId)))),
        body,
      ]),
    );
    final avatar = Avatar(nickname: screen.nick(m.authorId), color: screen.colorOf(m.authorId), photoId: screen.photoOf(m.authorId), size: 26);
    return Row(
      mainAxisAlignment: mine ? MainAxisAlignment.end : MainAxisAlignment.start,
      crossAxisAlignment: CrossAxisAlignment.end,
      children: mine
          ? [Flexible(child: bubble)]
          : [avatar, const SizedBox(width: 6), Flexible(child: bubble)],
    );
  }
}

class _Typing extends ChangeNotifier {
  _Typing(this.screen);

  final GameScreenState screen;
  static const tick = Duration(milliseconds: 40);

  ChatMessage? current;
  List<String> _letters = [];
  int shown = 0;
  final revealed = <int>{};
  final _mentionAt = <int, int>{};
  final _queue = <ChatMessage>[];
  final _seen = <String>{};
  Timer? _timer;
  int _cue = 0, _hold = 0, _pause = 0;
  bool _disposed = false;

  bool get busy => _timer != null;

  /// Реплики, до которых очередь ещё не дошла: в разговоре их пока не видно.
  Set<String> get queuedIds => {for (final m in _queue) m.id};

  /// Перед репликой — «печатает…», как в мессенджере: человек не выдаёт абзац мгновенно.
  String get text => _pause > 0 && current != null
      ? '${prefixOf(screen, current!)}печатает${'.' * (1 + (_pause ~/ 6) % 3)}'
      : _letters.take(shown).join();

  bool get thinking => _pause > 0;

  bool isRevealed(int index) => revealed.contains(index);

  static String prefixOf(GameScreenState screen, ChatMessage m) => '${screen.nick(m.authorId)}: ';

  /// В строке две строки текста: пока реплика печатается, видна строка, которую пишут сейчас (с именем),
  /// а не застывшее начало длинного объяснения.
  static String tail(GameScreenState screen, ChatMessage m, String typed) {
    final lines = typed.split('\n');
    if (lines.length < 2) return typed;
    final last = lines.last.isEmpty ? lines[lines.length - 2] : lines.last;
    return prefixOf(screen, m) + last;
  }

  static String fullText(GameScreenState screen, ChatMessage m) =>
      prefixOf(screen, m) + (m.text ?? '').replaceFirst(tablePostPrefix, '');

  void sync({bool initial = false}) {
    final v = screen.view;
    if (v == null) return;
    final all = screen.chat.where((m) => m.channel == 'public' && m.authorId != null && m.round == v.round &&
        (m.cardIds.isNotEmpty || (m.text ?? '').trim().isNotEmpty)).toList();
    final fresh = all.where((m) => _seen.add(m.id)).toList();
    if (initial) {
      _show(all.lastOrNull, animate: false);
      return;
    }
    for (final m in fresh) {
      if (m.authorId == v.me?.id) {
        // Свою реплику не ждём: показываем сразу.
        _queue.clear();
        _show(m, animate: false);
      } else {
        _queue.add(m);
      }
    }
    if (_queue.length > 6) _queue.removeRange(0, _queue.length - 6);
    if (_timer == null && _queue.isNotEmpty) _show(_queue.removeAt(0));
  }

  void _show(ChatMessage? m, {bool animate = true}) {
    _timer?.cancel();
    _timer = null;
    current = m;
    revealed.clear();
    _mentionAt.clear();
    _letters = m == null ? [] : fullText(screen, m).characters.toList();
    if (m == null) {
      _notify();
      return;
    }
    final text = _letters.join();
    final v = screen.view!;
    for (var i = 0; i < m.cardIds.length; i++) {
      final note = m.noteFor(i) ?? '';
      // Карта, которую «кидал», и улика — с первой буквы: о них реплика.
      if (theorySource(note)) {
        revealed.add(i);
        continue;
      }
      for (final row in v.board) {
        final column = row.cards.indexOf(m.cardIds[i]);
        if (column < 0) continue;
        final category = RegExp.escape(T.category(row.category));
        final match = RegExp('$category\\s*(?:[—–:,-]\\s*)?(?:карт[аыу]\\s+)?${column + 1}(?![0-9])', caseSensitive: false)
            .firstMatch(text);
        if (match != null) _mentionAt[i] = text.substring(0, match.end).characters.length;
      }
    }
    if (!animate) {
      shown = _letters.length;
      revealed.addAll(List.generate(m.cardIds.length, (i) => i));
      _notify();
      return;
    }
    shown = prefixOf(screen, m).characters.length.clamp(0, _letters.length);
    _cue = 0;
    _hold = 0;
    // Пауза «печатает…» — от длины реплики: 0,6–2 с.
    _pause = (15 + _letters.length ~/ 12).clamp(15, 50);
    _timer = Timer.periodic(tick, (_) => _step());
    _notify();
  }

  void _step() {
    final m = current;
    if (m == null || _disposed) return;
    if (_pause > 0) {
      _pause--;
      if (_pause % 6 == 0) _notify();
      return;
    }
    if (shown < _letters.length) {
      shown++;
      if (shown % 5 == 0 && _letters[shown - 1].trim().isNotEmpty) screen.ref.read(soundProvider).play(Sfx.typing);
      for (final e in _mentionAt.entries) {
        if (e.value <= shown) revealed.add(e.key);
      }
      _notify();
      return;
    }
    // Текст допечатан: оставшиеся карты — по одной, как будто игрок показывает их пальцем.
    final rest = List.generate(m.cardIds.length, (i) => i).where((i) => !revealed.contains(i)).toList();
    if (rest.isNotEmpty) {
      if (_cue++ % 15 == 0) {
        revealed.add(rest.first);
        _notify();
      }
      return;
    }
    // Пауза на чтение, затем следующая реплика.
    if (++_hold >= 60) {
      _timer?.cancel();
      _timer = null;
      if (_queue.isNotEmpty) _show(_queue.removeAt(0));
      _notify();
    }
  }

  /// Новая фаза: прежняя реплика больше не «текущая» (без уведомления — вызывается при сборке).
  void reset() {
    _pause = 0;
    _timer?.cancel();
    _timer = null;
    _queue.clear();
    current = null;
    revealed.clear();
  }

  void complete() {
    final m = current;
    if (m == null) return;
    _pause = 0;
    shown = _letters.length;
    revealed.addAll(List.generate(m.cardIds.length, (i) => i));
    _hold = 0;
    _notify();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _timer?.cancel();
    super.dispose();
  }
}

/// Выбор эмодзи — облачко над картой-смайликом: каждое нажатие — реакция, можно спамить.
class _EmojiBar extends StatelessWidget {
  const _EmojiBar({required this.screen, required this.onClose});

  final GameScreenState screen;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) => Material(
        key: const Key('emoji-bar'),
        color: AppColors.surface2,
        elevation: 8,
        borderRadius: BorderRadius.circular(16),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(6, 4, 2, 4),
          child: Row(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            SizedBox(
              width: 5 * 44,
              child: Wrap(children: [
                for (final e in Reaction.allowed)
                  InkWell(
                    key: Key('emoji-$e'),
                    borderRadius: BorderRadius.circular(99),
                    onTap: () => screen.react(e),
                    child: SizedBox(width: 44, height: 44, child: Center(child: Text(e, style: const TextStyle(fontSize: 26)))),
                  ),
              ]),
            ),
            IconButton(
              tooltip: 'Скрыть эмодзи',
              visualDensity: VisualDensity.compact,
              icon: const Icon(Icons.close, size: 18),
              onPressed: onClose,
            ),
          ]),
        ),
      );
}

/// Летящие эмодзи: каждое поднимается снизу вверх и тает за [GameScreenState.reactionLife].
class _FlyingReactions extends StatelessWidget {
  const _FlyingReactions({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) => LayoutBuilder(builder: (context, box) {
        return Stack(children: [
          for (final r in screen.reactions)
              _Flying(
                key: ValueKey('fly-${r.userId}-${r.at.microsecondsSinceEpoch}'),
                reaction: r,
                size: box.biggest,
              ),
        ]);
      });
}

class _Flying extends StatelessWidget {
  const _Flying({super.key, required this.reaction, required this.size});

  final Reaction reaction;
  final Size size;

  @override
  Widget build(BuildContext context) {
    // Дорожка по горизонтали — от отправителя и момента, чтобы спам не летел одной колонной.
    final lane = ((reaction.userId.hashCode ^ reaction.at.millisecondsSinceEpoch) % 1000) / 1000;
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: GameScreenState.reactionLife,
      builder: (context, t, child) => Positioned(
        left: 24 + lane * (size.width - 96),
        top: (size.height - 60) * (1 - t),
        child: Opacity(opacity: t < .8 ? 1 : (1 - t) * 5, child: child),
      ),
      child: Text(reaction.emoji, key: Key('flying-${reaction.emoji}'), style: const TextStyle(fontSize: 34)),
    );
  }
}

/// «Кто Убийца?»: игроки столбцами, под каждым — карты, за которые он голосовал по рядам.
/// Только выбор, без отметок верности: она закрыта до итогов. Нажатие на игрока — голос за него.
class _SuspectBoard extends StatelessWidget {
  const _SuspectBoard({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final finale = v.finale!;
    final stage = finale.currentStage!;
    final players = [...v.players]..sort((a, b) => a.seat.compareTo(b.seat));
    // Номер этапа → ряд; берём последнюю попытку голосования каждого игрока.
    final rowOfStage = {for (final o in finale.outcomes) if (o.kind == 'Row') o.stage: o.row};
    final picks = <String, Map<int, int>>{};
    final attempts = <(String, int), int>{};
    for (final vote in finale.votes) {
      final row = rowOfStage[vote.stage];
      if (row == null || vote.column == null) continue;
      if ((attempts[(vote.voter, row)] ?? -1) > vote.attempt) continue;
      attempts[(vote.voter, row)] = vote.attempt;
      (picks[vote.voter] ??= {})[row] = vote.column!;
    }
    final order = _Board.tableOrder(v);
    return LayoutBuilder(builder: (context, box) {
      final card = ((box.maxHeight - 100) / (order.isEmpty ? 1 : order.length) - 4).clamp(22.0, 56.0).floorToDouble();
      return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 4, 12, 4),
          child: Text('КТО УБИЙЦА?', style: heading(16, spacing: 1)),
        ),
        Expanded(
          child: ListView(
            key: const Key('suspect-board'),
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 8),
            children: [
              for (final p in players)
                _suspect(context, v, stage, p, picks[p.id] ?? const {}, order, card),
            ],
          ),
        ),
      ]);
    });
  }

  Widget _suspect(BuildContext context, GameView v, VoteStage stage, PlayerInfo p, Map<int, int> picks, List<int> order, double card) {
    final candidate = stage.candidateSuspects.isEmpty || stage.candidateSuspects.contains(p.id);
    final selected = screen.target == p.id;
    final ghost = p.isGhost || p.knownRole == 'Ghost';
    return Opacity(
      opacity: candidate ? 1 : 0.45,
      child: GestureDetector(
        key: Key('suspect-${p.id}'),
        behavior: HitTestBehavior.opaque,
        onTap: candidate ? () => screen.tapPlayer(p.id) : null,
        child: Container(
          width: card + 26,
          margin: const EdgeInsets.only(right: 6),
          padding: const EdgeInsets.symmetric(vertical: 6),
          decoration: BoxDecoration(
            color: selected ? const Color(0xFF3A2B12) : AppColors.surface,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: selected ? AppColors.amber : AppColors.border),
          ),
          child: Column(children: [
            Avatar(nickname: screen.nick(p.id), color: screen.colorOf(p.id), photoId: screen.photoOf(p.id), size: 30,
                ring: selected ? AppColors.amber : (ghost ? AppColors.ice : null)),
            const SizedBox(height: 2),
            Text(p.id == v.me?.id ? 'Вы' : screen.nick(p.id), maxLines: 1, overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 10, color: selected ? AppColors.amber : AppColors.text)),
            const SizedBox(height: 4),
            for (final r in order)
              Padding(padding: const EdgeInsets.only(bottom: 4), child: _vote(context, v, p, r, picks[r], card)),
          ]),
        ),
      ),
    );
  }

  Widget _vote(BuildContext context, GameView v, PlayerInfo p, int r, int? c, double card) {
    if (c == null || c >= v.board[r].cards.length) return SizedBox(width: card, height: card);
    final id = v.board[r].cards[c];
    // Нажатие по столбцу — голос за игрока; карту крупно — долгим нажатием.
    return GestureDetector(
      onLongPress: () => showCardZoom(context, id, caption: '${screen.nick(p.id)} · ${T.category(v.board[r].category)}'),
      child: CardImage(key: Key('suspect-vote-${p.id}-$r'), cardId: id, size: card, radius: 6),
    );
  }
}

/// Цвет n-й улики в показе: первая — зелёная, как «указывает сюда», дальше — различимые на тёмном столе.
Color linkColor(int n) => const [
      AppColors.believed,
      Color(0xFF4FC3F7),
      Color(0xFFF2B33D),
      Color(0xFFE57CD8),
      Color(0xFFB39DDB),
      Color(0xFFFF8A65),
    ][n % 6];

extension _OrFallback on List<Color> {
  List<Color> ifEmpty(Color fallback) => isEmpty ? [fallback] : this;
}

/// Рамка карты из одного или нескольких цветов (по цвету на каждую улику).
class _LinkBorder extends StatelessWidget {
  const _LinkBorder({super.key, required this.colors, required this.child, this.radius = 10});

  final List<Color> colors;
  final Widget child;
  final double radius;

  @override
  Widget build(BuildContext context) => colors.length == 1
      ? Container(
          foregroundDecoration: BoxDecoration(border: Border.all(color: colors.first, width: 3), borderRadius: BorderRadius.circular(radius)),
          child: child,
        )
      : CustomPaint(foregroundPainter: _SegmentsPainter(colors, radius), child: child);
}

class _SegmentsPainter extends CustomPainter {
  _SegmentsPainter(this.colors, this.radius);

  final List<Color> colors;
  final double radius;

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final n = colors.length;
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 3.5
      ..shader = SweepGradient(
        colors: [for (final c in colors) ...[c, c]],
        stops: [for (var i = 0; i < n; i++) ...[i / n, (i + 1) / n]],
      ).createShader(rect);
    canvas.drawRRect(RRect.fromRectAndRadius(rect.deflate(1.75), Radius.circular(radius)), paint);
  }

  @override
  bool shouldRepaint(_SegmentsPainter old) => old.colors.length != colors.length || !old.colors.every(colors.contains);
}
