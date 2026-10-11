part of 'game_screen.dart';

/// Новый стол по наброску владельца. Боком: меню слева сверху, лесенка подсказок
/// по рядам, поле с вертикальными подписями (Тайна, Мотив, Способ, Место), справа
/// игроки, главная кнопка и чат, снизу веер руки. Вертикально — игроки сверху,
/// кнопка и чат под веером. Ход партии (панель) открывается глазиком на месте стола;
/// выбор карт и игроков при этом сохраняется — он хранится в экране партии.
class _MainTable extends StatefulWidget {
  const _MainTable({required this.screen});

  final GameScreenState screen;

  @override
  State<_MainTable> createState() => _MainTableState();
}

class _MainTableState extends State<_MainTable> {
  /// null — по фазе: панель сама открывается там, где действие только в ней.
  bool? _panel;
  String? _phase;
  final _say = _Say();
  bool _emoji = false;

  GameScreenState get screen => widget.screen;

  @override
  void initState() {
    super.initState();
    _say.addListener(_refresh);
    screen.chatChanges.addListener(_refresh);
    screen.reactionChanges.addListener(_refresh);
  }

  void _refresh() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    screen.chatChanges.removeListener(_refresh);
    screen.reactionChanges.removeListener(_refresh);
    _say.dispose();
    super.dispose();
  }

  static bool _panelPhase(GameView v) =>
      v.can('RevealHints') || const {'VoteTie', 'AwardNomination', 'AwardVoting', 'Finished'}.contains(v.phase);

  bool _panelOpen(GameView v) => _panel ?? _panelPhase(v);

  void _toggle(GameView v) => setState(() => _panel = !_panelOpen(v));

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    if (_phase != v.phase) {
      _phase = v.phase;
      _panel = null;
      _say.reset();
    }
    final size = MediaQuery.sizeOf(context);
    final landscape = size.width > size.height;
    final night = v.phase == 'Night' && (v.can('ChooseTruth') || v.can('TeamSuggest'));
    final open = _panelOpen(v);
    final stage = v.finale?.currentStage;
    final suspectVote = stage != null && !stage.isRow;
    final center = open
        ? _panelView(v)
        : suspectVote
            ? _SuspectBoard(screen: screen)
            : _TableArea(screen: screen, landscape: landscape, say: _say);
    final hand = v.me != null;
    return ScrollConfiguration(
      // Никаких полос прокрутки: ленты листаются пальцем или мышью.
      behavior: ScrollConfiguration.of(context).copyWith(scrollbars: false, dragDevices: PointerDeviceKind.values.toSet()),
      child: Scaffold(
        key: const Key('main-table'),
        backgroundColor: night ? AppColors.night : AppColors.bg,
        body: SafeArea(
          child: Stack(children: [
            Positioned.fill(child: Column(children: [
            const ConnectionBanner(),
            _TopBar(screen: screen, panelOpen: open, onEye: () => _toggle(v)),
            if (_emoji) _EmojiBar(screen: screen, onClose: () => setState(() => _emoji = false)),
            if (landscape)
              Expanded(
                child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  Expanded(child: center),
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
              Expanded(child: center),
            ],
            if (hand) _Fan(screen: screen, say: _say, onEmoji: () => setState(() => _emoji = !_emoji), height: landscape ? (size.height * 0.2).clamp(56.0, 96.0) : 96),
            if (!landscape)
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 4, 12, 10),
                child: _Actions(screen: screen, row: true, onStatus: () => setState(() => _panel = true)),
              ),
            ])),
            // Эмодзи проплывают по столу снизу вверх и не мешают нажатиям.
            Positioned.fill(child: IgnorePointer(child: _FlyingReactions(screen: screen))),
          ]),
        ),
      ),
    );
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
  const _TopBar({required this.screen, required this.panelOpen, required this.onEye});

  final GameScreenState screen;
  final bool panelOpen;
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
            const PopupMenuItem(value: 'classic', child: Text('Классический стол')),
            const PopupMenuItem(value: 'rules', child: Text('Правила')),
            const PopupMenuItem(value: 'home', child: Text('На главную')),
          ],
        ),
        Text(
          v.round > 0 ? 'Р. ${v.round}/${v.totalRounds}' : 'ПАРТИЯ',
          key: const Key('round'),
          style: heading(15, spacing: 1),
        ),
        const SizedBox(width: 8),
        Flexible(
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
        const Spacer(),
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
  const _TableArea({required this.screen, required this.landscape, required this.say});

  final GameScreenState screen;
  final bool landscape;
  final _Say say;

  /// Отметка карты поля: свой показ (пока не отправлен) или последнее заявление говорящего.
  Color? _mark(String id, ChatMessage? statement) {
    if (say.active) {
      final m = say.marks[id];
      return m == null ? null : (m ? AppColors.believed : AppColors.redBright);
    }
    if (statement == null) return null;
    final i = statement.cardIds.indexOf(id);
    if (i < 0) return null;
    final note = statement.noteFor(i) ?? '';
    if (theorySource(note)) return null;
    return theoryNegative(note) ? AppColors.redBright : AppColors.believed;
  }

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final rows = v.board.isEmpty ? 4 : v.board.length;
    final columns = v.board.isEmpty ? 5 : v.board.first.cards.length;
    final hintCards = v.hints.fold<int>(1, (m, h) => h.cards.length > m ? h.cards.length : m).clamp(1, 2);
    return LayoutBuilder(builder: (context, box) {
      const gap = _Board.gap;
      // Ширина: подсказки (до двух карт по 0.7) + подпись + столбцы поля.
      final byWidth = (box.maxWidth - 16 - _Board.verticalLabel - gap * columns - (hintCards * 4 + 10)) / (columns + hintCards * 0.7);
      final stripHeight = say.active || _statement(screen) != null || _canSay(v) ? _SayStrip.height : 0.0;
      final byHeight = landscape ? (box.maxHeight - 8 - stripHeight) / rows - gap : double.infinity;
      final card = (byWidth < byHeight ? byWidth : byHeight).clamp(28.0, 150.0).floorToDouble();
      // Подпись ступеньки ~14 dp — карта подсказки умещается в высоту ряда.
      final hint = (card * 0.7 < card + gap - 15 ? card * 0.7 : card + gap - 15).floorToDouble();
      final hintsWidth = hintCards * (hint + 4) + 10;
      final order = _Board.tableOrder(v);
      final statement = _statement(screen);
      final strip = say.active || statement != null || _canSay(v);
      final board = SingleChildScrollView(
        key: const Key('main-board'),
        padding: const EdgeInsets.fromLTRB(4, 2, 8, 8),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(
            width: hintsWidth,
            child: Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              for (var i = 0; i < v.hints.length || i < order.length; i++)
                SizedBox(
                  height: card + gap,
                  child: i < v.hints.length ? _HintStep(screen: screen, say: say, hint: v.hints[i], size: hint) : null,
                ),
              if (v.vanishedCount > 0)
                Padding(
                  padding: const EdgeInsets.only(top: 4, right: 4),
                  child: Text('исчезло ×${v.vanishedCount}', key: const Key('main-vanished'), style: const TextStyle(fontSize: 10, color: AppColors.dim)),
                ),
            ]),
          ),
          Expanded(
            child: _Board(
              screen: screen,
              maxCard: card,
              order: order,
              vertical: true,
              decorate: (id, child) {
                final color = _mark(id, statement);
                Widget out = color == null
                    ? child
                    : Container(
                        key: Key('say-mark-$id'),
                        foregroundDecoration: BoxDecoration(
                          border: Border.all(color: color, width: 3),
                          borderRadius: BorderRadius.circular(10),
                        ),
                        child: child,
                      );
                // Показ на столе: нажатие — зелёная («улика указывает»), ещё раз — красная («не эта»), ещё — снять.
                if (say.active) out = GestureDetector(onTap: () => say.cycle(id), child: out);
                return out;
              },
            ),
          ),
        ]),
      );
      if (!strip) return board;
      return Column(children: [
        _SayStrip(screen: screen, say: say, statement: statement),
        Expanded(child: board),
      ]);
    });
  }
}

/// Ступенька лесенки: подпись раунда и его подсказки; лишние карты листаются свайпом.
class _HintStep extends StatelessWidget {
  const _HintStep({required this.screen, required this.say, required this.hint, required this.size});

  final GameScreenState screen;
  final _Say say;
  final HintGroup hint;
  final double size;

  @override
  Widget build(BuildContext context) {
    final label = hint.round == 0 ? 'зацепка' : 'р. ${hint.round}';
    return Column(crossAxisAlignment: CrossAxisAlignment.end, mainAxisSize: MainAxisSize.min, children: [
      SizedBox(
        height: 14,
        child: Padding(
          padding: const EdgeInsets.only(right: 4),
          child: Text(label, maxLines: 1, style: const TextStyle(fontSize: 9, color: AppColors.muted, height: 1.2)),
        ),
      ),
      SizedBox(
        height: size,
        child: hint.cards.isEmpty
            ? Container(
                width: size,
                margin: const EdgeInsets.only(right: 4),
                decoration: BoxDecoration(borderRadius: BorderRadius.circular(8), border: Border.all(color: AppColors.border)),
                alignment: Alignment.center,
                child: const Text('пусто', style: TextStyle(fontSize: 9, color: AppColors.dim)),
              )
            : ListView(
                scrollDirection: Axis.horizontal,
                reverse: true,
                shrinkWrap: true,
                children: [
                  for (final c in hint.cards.reversed)
                    Padding(
                      padding: const EdgeInsets.only(right: 4),
                      child: GestureDetector(
                        key: Key('hint-$c'),
                        onTap: () => say.active ? say.pick(c, hint: true) : HintSheet.show(context, screen, c, hint.round),
                        onLongPress: () => showCardZoom(context, c),
                        child: Stack(clipBehavior: Clip.none, children: [
                          Container(
                            foregroundDecoration: say.active && say.source == c
                                ? BoxDecoration(border: Border.all(color: AppColors.amber, width: 3), borderRadius: BorderRadius.circular(7))
                                : null,
                            child: CardImage(cardId: c, size: size, radius: 7),
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
      ),
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
              label: Text(cta.label.toUpperCase(), maxLines: 2, overflow: TextOverflow.ellipsis, textAlign: TextAlign.center),
            ),
          )
        : GestureDetector(onTap: onStatus, child: _StatusBar(text: actionHint(v), mine: needsMe(v), pulse: screen.turnPulse));
    return Row(children: [Expanded(child: main), SizedBox(width: row ? 10 : 6), _ChatButton(screen: screen)]);
  }
}

/// Рука веером снизу: выбранная карта приподнята; долгое нажатие — карта крупно.
class _Fan extends StatelessWidget {
  const _Fan({required this.screen, required this.say, required this.onEmoji, required this.height});

  final GameScreenState screen;
  final _Say say;
  final VoidCallback onEmoji;
  final double height;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final hand = isFinale(v) ? const <String>[] : v.me!.hand;
    final selectable = v.can('SendLetter') || v.can('Discard') || v.can('GiveFirstClue');
    final limit = v.can('SendLetter') ? lettersPerPlayer(v) : 1;
    final card = height - 14;
    // Во время показа на столе карта из веера — «кидал эту».
    bool raised(String c) => say.active ? say.source == c : screen.selectedHand.contains(c);
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
        return Stack(clipBehavior: Clip.none, children: [
          Positioned(
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
        ]);
      }),
    );
  }
}

/// Показ на столе, пока игрок его собирает: карта, которую «кидал» (из веера или письмо),
/// или улика-подсказка, и до четырёх карт поля — зелёные («указывает») и красные («не эта»).
class _Say extends ChangeNotifier {
  bool active = false;
  String? source;
  bool hint = false;
  final marks = <String, bool>{};

  void start() {
    active = true;
    notifyListeners();
  }

  void reset() {
    active = false;
    source = null;
    marks.clear();
  }

  void stop() {
    reset();
    notifyListeners();
  }

  void pick(String id, {required bool hint}) {
    if (source == id) {
      source = null;
    } else {
      source = id;
      this.hint = hint;
    }
    notifyListeners();
  }

  void cycle(String id) {
    final m = marks[id];
    if (m == null) {
      if (marks.length < 4) marks[id] = true;
    } else if (m) {
      marks[id] = false;
    } else {
      marks.remove(id);
    }
    notifyListeners();
  }
}

/// Сообщения, собранные на столе, начинаются так — в чате они помечаются «со стола».
const tablePostPrefix = 'Со стола: ';

/// Может ли игрок сейчас показывать на столе: обсуждение, не Призрак.
bool _canSay(GameView v) => v.phase == 'Discussion' && v.me != null && v.me!.role != 'Ghost';

/// Последнее публичное заявление с картами в этом раунде: говорящего, а без него — любое.
ChatMessage? _statement(GameScreenState screen) {
  final v = screen.view!;
  if (!const {'Discussion', 'Voting', 'VoteTie'}.contains(v.phase)) return null;
  ChatMessage? last, speaker;
  for (final m in screen.chat) {
    if (m.channel != 'public' || m.authorId == null || m.cardIds.isEmpty || m.round != v.round) continue;
    last = m;
    if (m.authorId == v.currentSpeaker) speaker = m;
  }
  return speaker ?? last;
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
  final green = [for (final e in say.marks.entries) if (e.value) _cardName(v, e.key)];
  final red = [for (final e in say.marks.entries) if (!e.value) _cardName(v, e.key)];
  final source = say.source != null;
  final parts = <String>[
    if (source) say.hint ? 'эта улика' : 'кидал эту карту',
    if (green.isNotEmpty) '${source ? 'указывает на' : 'думаю, это'} ${green.join(', ')}',
    if (red.isNotEmpty) '${source && !say.hint ? 'её не вытащили — ' : ''}не ${red.join(', ')}',
  ];
  return '$tablePostPrefix${parts.join('; ')}.';
}

/// Строка над полем: что говорящий показал на столе, а во время своего показа — подсказка и «Отправить».
class _SayStrip extends StatelessWidget {
  const _SayStrip({required this.screen, required this.say, required this.statement});

  static const height = 46.0;

  final GameScreenState screen;
  final _Say say;
  final ChatMessage? statement;

  Future<void> _send(BuildContext context) async {
    final v = screen.view!;
    final link = say.source != null ? ':0' : '';
    final sent = await runAction(
      context,
      () => screen.ref.read(apiProvider).sendChat(
            v.gameId,
            _sayText(v, say),
            channel: 'public',
            cards: [if (say.source != null) say.source!, ...say.marks.keys],
            cardNotes: [
              if (say.source != null) say.hint ? 'улика' : 'кидал',
              for (final green in say.marks.values) (green ? 'думаю, эта' : 'исключаю') + link,
            ],
          ),
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
        if (say.source != null)
          CardImage(key: const Key('say-source'), cardId: say.source!, size: 36, radius: 6)
        else if (letter != null)
          GestureDetector(
            key: const Key('say-my-letter'),
            onTap: () => say.pick(letter.cardId, hint: false),
            child: Opacity(opacity: 0.7, child: CardImage(cardId: letter.cardId, size: 36, radius: 6)),
          ),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            say.source == null
                ? 'Карта, которую кидали (веер), или улика слева; затем карты поля: раз — зелёная, два — красная'
                : '${say.hint ? 'Улика' : 'Кидал эту'} → нажмите карты поля: раз — указывает, два — не эта',
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 11, color: AppColors.muted),
          ),
        ),
        IconButton(key: const Key('say-cancel'), tooltip: 'Отменить показ', icon: const Icon(Icons.close), onPressed: say.stop),
        FilledButton(
          key: const Key('say-send'),
          onPressed: say.source == null && say.marks.isEmpty ? null : () => _send(context),
          child: const Text('На стол'),
        ),
      ]);
    } else {
      final m = statement;
      if (m != null) {
        final source = m.cardNotes.isNotEmpty && theorySource(m.cardNotes.first) ? m.cardIds.first : null;
        children.addAll([
          Avatar(nickname: screen.nick(m.authorId), color: screen.colorOf(m.authorId), photoId: screen.photoOf(m.authorId), size: 26),
          const SizedBox(width: 6),
          if (source != null) ...[
            GestureDetector(
              onTap: () => showCardZoom(context, source),
              child: CardImage(key: Key('say-shown-$source'), cardId: source, size: 36, radius: 6),
            ),
            const SizedBox(width: 6),
          ],
          Expanded(
            child: Text(
              '${screen.nick(m.authorId)}: ${(m.text ?? '').replaceFirst(tablePostPrefix, '')}',
              key: const Key('say-statement'),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 12),
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

/// Полоска эмодзи над столом: каждое нажатие — реакция, можно спамить.
class _EmojiBar extends StatelessWidget {
  const _EmojiBar({required this.screen, required this.onClose});

  final GameScreenState screen;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) => SizedBox(
        key: const Key('emoji-bar'),
        height: 44,
        child: Row(children: [
          Expanded(
            child: ListView(scrollDirection: Axis.horizontal, padding: const EdgeInsets.symmetric(horizontal: 8), children: [
              for (final e in Reaction.allowed)
                InkWell(
                  key: Key('emoji-$e'),
                  borderRadius: BorderRadius.circular(99),
                  onTap: () => screen.react(e),
                  child: Padding(padding: const EdgeInsets.all(6), child: Text(e, style: const TextStyle(fontSize: 24))),
                ),
            ]),
          ),
          IconButton(tooltip: 'Скрыть эмодзи', icon: const Icon(Icons.close, size: 18), onPressed: onClose),
        ]),
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
    return GestureDetector(
      onTap: () => showCardZoom(context, id, caption: '${screen.nick(p.id)} · ${T.category(v.board[r].category)}'),
      child: CardImage(key: Key('suspect-vote-${p.id}-$r'), cardId: id, size: card, radius: 6),
    );
  }
}
