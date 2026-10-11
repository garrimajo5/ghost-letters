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

  GameScreenState get screen => widget.screen;

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
    }
    final size = MediaQuery.sizeOf(context);
    final landscape = size.width > size.height;
    final night = v.phase == 'Night' && (v.can('ChooseTruth') || v.can('TeamSuggest'));
    final open = _panelOpen(v);
    final center = open ? _panelView(v) : _TableArea(screen: screen, landscape: landscape);
    final hand = v.me != null && v.me!.hand.isNotEmpty && !isFinale(v);
    return ScrollConfiguration(
      // Никаких полос прокрутки: ленты листаются пальцем или мышью.
      behavior: ScrollConfiguration.of(context).copyWith(scrollbars: false, dragDevices: PointerDeviceKind.values.toSet()),
      child: Scaffold(
        key: const Key('main-table'),
        backgroundColor: night ? AppColors.night : AppColors.bg,
        body: SafeArea(
          child: Column(children: [
            const ConnectionBanner(),
            _TopBar(screen: screen, panelOpen: open, onEye: () => _toggle(v)),
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
            if (hand) _Fan(screen: screen, height: landscape ? (size.height * 0.2).clamp(56.0, 96.0) : 96),
            if (!landscape)
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 4, 12, 10),
                child: _Actions(screen: screen, row: true, onStatus: () => setState(() => _panel = true)),
              ),
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
  const _TableArea({required this.screen, required this.landscape});

  final GameScreenState screen;
  final bool landscape;

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
      final byHeight = landscape ? (box.maxHeight - 8) / rows - gap : double.infinity;
      final card = (byWidth < byHeight ? byWidth : byHeight).clamp(28.0, 150.0).floorToDouble();
      final hint = (card * 0.7).floorToDouble();
      final hintsWidth = hintCards * (hint + 4) + 10;
      final order = _Board.tableOrder(v);
      return SingleChildScrollView(
        key: const Key('main-board'),
        padding: const EdgeInsets.fromLTRB(4, 2, 8, 8),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(
            width: hintsWidth,
            child: Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              for (var i = 0; i < v.hints.length || i < order.length; i++)
                SizedBox(
                  height: card + gap,
                  child: i < v.hints.length ? _HintStep(screen: screen, hint: v.hints[i], size: hint) : null,
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
            ),
          ),
        ]),
      );
    });
  }
}

/// Ступенька лесенки: подпись раунда и его подсказки; лишние карты листаются свайпом.
class _HintStep extends StatelessWidget {
  const _HintStep({required this.screen, required this.hint, required this.size});

  final GameScreenState screen;
  final HintGroup hint;
  final double size;

  @override
  Widget build(BuildContext context) {
    final label = hint.round == 0 ? 'зацепка' : 'р. ${hint.round}';
    return Column(crossAxisAlignment: CrossAxisAlignment.end, mainAxisSize: MainAxisSize.min, children: [
      Padding(
        padding: const EdgeInsets.only(right: 4),
        child: Text(label, style: const TextStyle(fontSize: 9, color: AppColors.muted, height: 1.1)),
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
                        onTap: () => HintSheet.show(context, screen, c, hint.round),
                        onLongPress: () => showCardZoom(context, c),
                        child: Stack(clipBehavior: Clip.none, children: [
                          CardImage(cardId: c, size: size, radius: 7),
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
  const _Fan({required this.screen, required this.height});

  final GameScreenState screen;
  final double height;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final hand = v.me!.hand;
    final selectable = v.can('SendLetter') || v.can('Discard') || v.can('GiveFirstClue');
    final limit = v.can('SendLetter') ? lettersPerPlayer(v) : 1;
    final card = height - 14;
    return SizedBox(
      key: const Key('main-fan'),
      height: height,
      child: LayoutBuilder(builder: (context, box) {
        final n = hand.length;
        final step = n <= 1 ? 0.0 : ((box.maxWidth - card - 24) / (n - 1)).clamp(12.0, card + 6);
        final width = card + step * (n - 1);
        final left = (box.maxWidth - width) / 2;
        final mid = (n - 1) / 2;
        return Stack(clipBehavior: Clip.none, children: [
          for (var i = 0; i < n; i++)
            Positioned(
              left: left + step * i,
              top: 12 + ((i - mid).abs() * 2.0) - (screen.selectedHand.contains(hand[i]) ? 12 : 0),
              child: Transform.rotate(
                angle: (i - mid) * 0.06,
                alignment: Alignment.bottomCenter,
                child: GestureDetector(
                  key: Key('hand-${hand[i]}'),
                  onTap: selectable ? () => _toggleHand(screen, hand[i], limit) : null,
                  onLongPress: () => showCardZoom(context, hand[i], caption: 'Карта на руке'),
                  child: Container(
                    foregroundDecoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(9),
                      border: Border.all(
                        color: screen.selectedHand.contains(hand[i]) ? AppColors.amber : AppColors.border,
                        width: screen.selectedHand.contains(hand[i]) ? 3 : 1,
                      ),
                    ),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(9),
                      boxShadow: const [BoxShadow(color: Colors.black54, blurRadius: 6, offset: Offset(0, 2))],
                    ),
                    child: Opacity(
                      opacity: selectable || !needsMe(v) ? 1 : 0.6,
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
