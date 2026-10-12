part of 'game_screen.dart';

/// Палитра сеанса из прототипа: густая фиолетовая ночь, свечи, мятный свет духа.
abstract final class _SeanceColors {
  static const bg = Color(0xFF120D1C);
  static const wood = Color(0xFF2A1D16);
  static const woodDark = Color(0xFF17100C);
  static const rim = Color(0xFF6B4A2E);
  static const candle = Color(0xFFF0B45A);
  static const flame = Color(0xFFFFD08A);
  static const wax = Color(0xFFEDE6F7);
  static const label = Color(0xFFC9BDE0);
  static const dashed = Color(0xFF5B4A72);
}

/// Свеча на границе секторов: пламя с ореолом и столбик воска.
class _Candle extends StatelessWidget {
  const _Candle({required this.size});

  final double size;

  @override
  Widget build(BuildContext context) => IgnorePointer(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Container(
            width: size * .55,
            height: size * .8,
            decoration: BoxDecoration(
              color: _SeanceColors.flame,
              borderRadius: BorderRadius.vertical(top: Radius.circular(size), bottom: Radius.circular(size * .45)),
              boxShadow: [BoxShadow(color: _SeanceColors.candle.withValues(alpha: .65), blurRadius: size * .9, spreadRadius: size * .2)],
            ),
          ),
          SizedBox(height: size * .08),
          Container(
            width: size * .55,
            height: size,
            decoration: BoxDecoration(color: _SeanceColors.wax, borderRadius: BorderRadius.circular(2)),
          ),
        ]),
      );
}

/// Сеанс («Досье»): круглый стол. По краю — места игроков; внутри — сектора подсказок
/// (зацепка, раунд 1, 2, 3…), разделённые свечами и подписанные; в центре — только
/// текущее: ящик писем (светится, пока дух читает), показ говорящего, голосование.
class _SeanceCircle extends StatelessWidget {
  const _SeanceCircle({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final players = [...v.players]..sort((a, b) => a.seat.compareTo(b.seat));
    final sectors = v.totalRounds + 1 > v.hints.length ? v.totalRounds + 1 : v.hints.length;
    final first = _firstSpeaker(screen);
    return LayoutBuilder(builder: (context, box) {
      final side = box.maxWidth < box.maxHeight ? box.maxWidth : box.maxHeight;
      // Стол чуть меньше квадрата: игроки сидят снаружи, на краю, и не закрывают подписи и подсказки секторов.
      final r = side / 2 - 44;
      final center = Offset(box.maxWidth / 2, box.maxHeight / 2);
      // Подсказки крупнее на большом экране; в тесном секторе — мельче, чтобы не налезали.
      final hint = (r * 0.26).clamp(16.0, 80.0).floorToDouble();
      Offset at(double angle, double radius) => center + Offset(math.cos(angle), math.sin(angle)) * radius;
      double sectorStart(int i) => -math.pi / 2 + 2 * math.pi * i / sectors;
      final children = <Widget>[
        Positioned.fill(child: CustomPaint(painter: _SeancePainter(sectors: sectors, radius: r, center: center))),
      ];
      // Свечи на границах секторов.
      final candle = (r * .07).clamp(10.0, 20.0);
      for (var i = 0; i < sectors; i++) {
        final p = at(sectorStart(i), r * 0.86);
        children.add(Positioned(left: p.dx - candle * .28, top: p.dy - candle, child: _Candle(size: candle)));
      }
      // Подписи и подсказки секторов.
      for (var i = 0; i < sectors; i++) {
        final mid = sectorStart(i) + math.pi / sectors;
        final label = at(mid, r * 0.8);
        final group = v.hints.where((h) => h.round == i).firstOrNull;
        children.add(Positioned(
          left: label.dx - 45,
          top: label.dy - 8,
          width: 90,
          child: IgnorePointer(
            child: Text(i == 0 ? 'зацепка' : 'раунд $i',
                key: Key('seance-sector-$i'),
                textAlign: TextAlign.center,
                style: heading((r * .045).clamp(9.0, 14.0), color: group == null ? _SeanceColors.dashed : _SeanceColors.label, spacing: 1.2)),
          ),
        ));
        final cards = group?.cards ?? const <String>[];
        for (var k = 0; k < cards.length; k++) {
          // Несколько подсказок раунда — веером вдоль дуги своего сектора.
          final spread = (2 * math.pi / sectors) * 0.5;
          final a = cards.length == 1 ? mid : mid - spread / 2 + spread * k / (cards.length - 1);
          final p = at(a, r * 0.52);
          final c = cards[k];
          final size = cards.length < 2 ? hint : math.max(16.0, math.min(hint, r * 0.55 * spread / (cards.length - 1) * 0.9)).floorToDouble();
          children.add(Positioned(
            left: p.dx - size / 2,
            top: p.dy - size / 2,
            child: GestureDetector(
              key: Key('hint-$c'),
              onTap: () => v.me == null ? showCardZoom(context, c) : HintSheet.show(context, screen, c, i),
              onLongPress: () => showCardZoom(context, c),
              child: CardImage(cardId: c, size: size, radius: 5),
            ),
          ));
        }
      }
      // Места игроков по кругу; первое место сверху.
      for (var i = 0; i < players.length; i++) {
        final p = players[i];
        final pos = at(-math.pi / 2 + 2 * math.pi * (i + 0.5) / players.length, r + 16);
        children.add(Positioned(
          left: pos.dx - 32,
          top: pos.dy - 22,
          width: 64,
          child: _SeatTile(screen: screen, player: p, first: first == p.id, compact: true, names: side >= 300),
        ));
      }
      final core = r * 0.68;
      children.add(Positioned(
        left: center.dx - core / 2,
        top: center.dy - core / 2,
        width: core,
        height: core,
        child: _SeanceCenter(screen: screen, size: core),
      ));
      return Stack(key: const Key('seance-circle'), clipBehavior: Clip.none, children: children);
    });
  }
}

class _SeancePainter extends CustomPainter {
  _SeancePainter({required this.sectors, required this.radius, required this.center});

  final int sectors;
  final double radius;
  final Offset center;

  @override
  void paint(Canvas canvas, Size size) {
    final table = Rect.fromCircle(center: center, radius: radius);
    canvas.drawCircle(
      center,
      radius,
      Paint()
        ..shader = const RadialGradient(colors: [_SeanceColors.wood, _SeanceColors.woodDark]).createShader(table),
    );
    canvas.drawCircle(center, radius, Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..color = _SeanceColors.rim);
    // Пунктирный круг в центре — место духа.
    final inner = radius * 0.36;
    final dash = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = _SeanceColors.dashed;
    const dashes = 48;
    for (var i = 0; i < dashes; i++) {
      final a = 2 * math.pi * i / dashes;
      canvas.drawArc(Rect.fromCircle(center: center, radius: inner), a, math.pi / dashes, false, dash);
    }
    final line = Paint()
      ..color = AppColors.amber.withValues(alpha: 0.18)
      ..strokeWidth = 1;
    for (var i = 0; i < sectors; i++) {
      final a = -math.pi / 2 + 2 * math.pi * i / sectors;
      final dir = Offset(math.cos(a), math.sin(a));
      canvas.drawLine(center + dir * radius * 0.36, center + dir * radius * 0.84, line);
    }
  }

  @override
  bool shouldRepaint(_SeancePainter old) => old.sectors != sectors || old.radius != radius || old.center != center;
}

/// Центр стола: показывает только то, что происходит сейчас.
class _SeanceCenter extends StatelessWidget {
  const _SeanceCenter({required this.screen, required this.size});

  final GameScreenState screen;
  final double size;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final stage = v.finale?.currentStage;
    // В обсуждении шепчет только тот, у кого слово: чужая старая мысль под именем говорящего путает.
    final whisper = screen.thoughts
        .where((t) =>
            t.round == v.round &&
            (t.text ?? '').isNotEmpty &&
            (v.phase != 'Discussion' || (v.currentSpeaker != null && t.authorId == v.currentSpeaker)))
        .lastOrNull;
    Widget body;
    switch (v.phase) {
      case 'Mailbox' || 'GhostPick':
        final reading = v.phase == 'GhostPick';
        body = Column(mainAxisSize: MainAxisSize.min, children: [
          _MailboxGlow(glowing: reading, size: size * 0.42),
          const SizedBox(height: 4),
          Text(reading ? 'Дух читает письма' : 'Письма: ${v.mailboxCount}',
              key: const Key('seance-center-text'), textAlign: TextAlign.center, style: const TextStyle(fontSize: 11, color: AppColors.ice)),
        ]);
      case 'Discussion':
        final m = _statement(screen);
        final source = m != null && m.cardNotes.isNotEmpty && theorySource(m.cardNotes.first) ? m.cardIds.first : null;
        body = Column(mainAxisSize: MainAxisSize.min, children: [
          if (source != null)
            CardImage(key: Key('seance-shown-$source'), cardId: source, size: size * 0.4, radius: 6)
          else
            Icon(Icons.forum_outlined, size: size * 0.3, color: AppColors.ice),
          const SizedBox(height: 4),
          Text(m == null ? 'Обсуждение' : screen.nick(m.authorId),
              key: const Key('seance-center-text'), maxLines: 1, overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 11, color: AppColors.believed)),
        ]);
      case 'Voting' || 'VoteTie':
        final label = stage == null
            ? 'Голосование'
            : stage.isRow && stage.row >= 0 && stage.row < v.board.length
                ? 'Голосуем: ${T.category(v.board[stage.row].category)}'
                : 'Кто Убийца?';
        body = Column(mainAxisSize: MainAxisSize.min, children: [
          Icon(Icons.how_to_vote_outlined, size: size * 0.3, color: AppColors.amber),
          Text(label, key: const Key('seance-center-text'), textAlign: TextAlign.center, style: heading(12, spacing: 0.5)),
        ]);
      default:
        body = Text(T.shortPhase(v.phase), key: const Key('seance-center-text'), textAlign: TextAlign.center, style: heading(13, spacing: 1));
    }
    return Center(
      child: FittedBox(
        fit: BoxFit.scaleDown,
        child: SizedBox(
          width: size,
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            body,
            // Шёпот ботов — их ход мысли у стола, вполголоса.
            if (whisper != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('«${whisper.text}»',
                    key: const Key('seance-whisper'),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 11, fontStyle: FontStyle.italic, color: AppColors.ice)),
              ),
          ]),
        ),
      ),
    );
  }
}

/// Ящик писем: пока дух читает — потустороннее свечение.
class _MailboxGlow extends StatefulWidget {
  const _MailboxGlow({required this.glowing, required this.size});

  final bool glowing;
  final double size;

  @override
  State<_MailboxGlow> createState() => _MailboxGlowState();
}

class _MailboxGlowState extends State<_MailboxGlow> with SingleTickerProviderStateMixin {
  late final _pulse = AnimationController(vsync: this, duration: const Duration(milliseconds: 1600));

  @override
  void initState() {
    super.initState();
    _sync();
  }

  @override
  void didUpdateWidget(_MailboxGlow old) {
    super.didUpdateWidget(old);
    _sync();
  }

  void _sync() {
    if (widget.glowing && !_pulse.isAnimating) {
      _pulse.repeat(reverse: true);
    } else if (!widget.glowing && _pulse.isAnimating) {
      _pulse.stop();
    }
  }

  @override
  void dispose() {
    _pulse.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
        animation: _pulse,
        builder: (context, child) {
          final t = widget.glowing ? 0.4 + 0.6 * _pulse.value : 0.0;
          return Container(
            key: Key(widget.glowing ? 'seance-mailbox-glow' : 'seance-mailbox'),
            width: widget.size,
            height: widget.size,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              boxShadow: widget.glowing
                  ? [BoxShadow(color: const Color(0xFF8FE3FF).withValues(alpha: 0.55 * t), blurRadius: 24 * t, spreadRadius: 6 * t)]
                  : null,
            ),
            child: child,
          );
        },
        child: Icon(Icons.markunread_mailbox_outlined, size: widget.size * 0.8, color: widget.glowing ? const Color(0xFFBFF0FF) : AppColors.muted),
      );
}
