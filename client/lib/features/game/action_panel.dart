import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'game_screen.dart';
import 'game_state.dart';

/// Главная кнопка хода в нижней панели экрана.
class Cta {
  const Cta(this.label, this.onPressed, {this.icon, this.danger = false});

  final String label;
  final VoidCallback? onPressed;
  final IconData? icon;
  final bool danger;
}

/// Ачивки, на которые выдвигают в конце партии.
const awardTitles = {
  'steel_balls': 'Стальные яйца',
  'sherlock': 'Шерлок',
  'best_liar': 'Лучший лжец',
  'ghost_whisperer': 'Голос Призрака',
};

const _awardHints = {
  'steel_balls': 'Самый смелый ход',
  'sherlock': 'Лучшая дедукция',
  'best_liar': 'Убедительнее всех врал',
  'ghost_whisperer': 'Лучше всех понимал Призрака',
};

const _awardIcons = {
  'steel_balls': Icons.shield_outlined,
  'sherlock': Icons.search,
  'best_liar': Icons.theater_comedy_outlined,
  'ghost_whisperer': Icons.blur_on,
};

/// Панель действий текущей фазы: что нужно сделать, выбор целей и второстепенные кнопки.
/// Главная кнопка хода живёт внизу экрана — её даёт [ActionPanel.cta].
class ActionPanel extends StatelessWidget {
  const ActionPanel({super.key, required this.screen});

  final GameScreenState screen;

  GameView get v => screen.view!;

  /// Главное действие фазы для нижней кнопки; null — действие в панели (или ждать нечего нажимать).
  static Cta? cta(GameScreenState screen) {
    final v = screen.view!;
    final rows = v.board.length;
    if (v.can('AckRole')) return Cta('Понятно', () => screen.send('AckRole'), icon: Icons.check);
    if (v.can('ChooseTruth')) {
      return Cta(
        'Это истина',
        screen.truth.length == rows
            ? () => screen.send('ChooseTruth', {'columns': [for (var r = 0; r < rows; r++) screen.truth[r]]})
            : null,
        icon: Icons.nights_stay_outlined,
        danger: true,
      );
    }
    if (v.can('NameTruth')) {
      return Cta(
        'Назвать улики',
        screen.truth.length == rows
            ? () => screen.send('NameTruth', {'columns': [for (var r = 0; r < rows; r++) screen.truth[r]]})
            : null,
        icon: Icons.record_voice_over_outlined,
      );
    }
    if (v.can('GiveFirstClue')) {
      return Cta(
        'Выложить зацепку',
        screen.selectedHand.length == 1 ? () => screen.send('GiveFirstClue', {'cardId': screen.selectedHand.first}) : null,
        icon: Icons.style_outlined,
      );
    }
    if (v.can('SendLetter')) {
      final need = lettersPerPlayer(v);
      return Cta(
        'Отправить письмо',
        screen.selectedHand.length == need ? () => screen.send('SendLetter', {'cardIds': screen.selectedHand.toList()}) : null,
        icon: Icons.mail_outline,
      );
    }
    if (v.can('RevealHints')) {
      final n = screen.selectedMailbox.length;
      return Cta(
        n == 0 ? 'Ничего не открывать' : 'Открыть: $n',
        () => screen.send('RevealHints', {'cardIds': screen.selectedMailbox.toList()}),
        icon: Icons.visibility_outlined,
      );
    }
    if (v.can('Discard')) {
      return screen.selectedHand.length == 1
          ? Cta('Сбросить и добрать', () => screen.send('Discard', {'cardId': screen.selectedHand.first}), icon: Icons.autorenew)
          : Cta('Оставить руку', () => screen.send('Discard', {'cardId': null}), icon: Icons.pan_tool_alt_outlined);
    }
    if (v.can('EndTurn')) return Cta('Закончить слово', () => screen.send('EndTurn'), icon: Icons.mic_off_outlined);
    if (v.can('ReadyNextRound')) return Cta('Готов', () => screen.send('ReadyNextRound'), icon: Icons.check);
    if (v.can('CastVote')) {
      final stage = v.finale?.currentStage;
      if (stage == null) return null;
      final ready = stage.isRow ? screen.voteColumn != null : screen.target != null;
      return Cta(
        'Голосовать',
        ready ? () => screen.send('CastVote', stage.isRow ? {'column': screen.voteColumn} : {'suspect': screen.target}) : null,
        icon: Icons.how_to_vote_outlined,
      );
    }
    if (v.can('ReadyRevote')) return Cta('Готов переголосовать', () => screen.send('ReadyRevote'), icon: Icons.replay);
    if (v.can('BlackmailerPick')) {
      return Cta(
        'Это Шантажист',
        screen.target == null ? null : () => screen.send('BlackmailerPick', {'target': screen.target}),
        icon: Icons.gps_fixed,
        danger: true,
      );
    }
    final lobbyId = screen.lobbyId;
    if (v.phase == 'Finished' && lobbyId != null && v.me != null) {
      return Cta('Реванш', () => screen.openLobby(lobbyId), icon: Icons.replay);
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final body = _body(context);
    final mine = needsMe(v);
    return Panel(
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 14),
      border: mine ? AppColors.amber.withValues(alpha: 0.6) : null,
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          if (mine) ...[
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
              decoration: BoxDecoration(color: AppColors.amber, borderRadius: BorderRadius.circular(99)),
              child: Text('ВАШ ХОД', style: heading(11, color: AppColors.onAmber, spacing: 1.2)),
            ),
            const SizedBox(width: 8),
          ],
          Expanded(child: Text(actionHint(v), style: Theme.of(context).textTheme.titleMedium)),
        ]),
        if (body.isNotEmpty) const SizedBox(height: 10),
        ...body,
      ]),
    );
  }

  List<Widget> _body(BuildContext context) {
    final me = v.me;
    switch (v.phase) {
      case 'RoleReveal':
        if (me == null) return const [];
        return [RoleReveal(role: me.role)];
      case 'Night':
        if (!v.can('ChooseTruth')) {
          return [
            const _Illustration('night'),
            const SizedBox(height: 8),
            const Text('Город спит. Убийца выбирает истинные улики…', style: TextStyle(color: AppColors.muted)),
          ];
        }
        return [
          Text(
            'Нажимайте на карты поля — по одной в каждом ряду. Выбрано ${screen.truth.length} из ${v.board.length}.',
            style: const TextStyle(color: AppColors.redSoft),
          ),
        ];
      case 'FirstClue':
        if (!v.can('GiveFirstClue')) return [const Text('Призрак думает о первой зацепке…', style: TextStyle(color: AppColors.muted))];
        return [
          const Text('Выберите карту на руке — она станет первой зацепкой. Или начните без неё.'),
          const SizedBox(height: 8),
          OutlinedButton(onPressed: () => screen.send('GiveFirstClue', {'cardId': null}), child: const Text('Без зацепки')),
        ];
      case 'Mailbox':
        if (!v.can('SendLetter')) {
          return [
            Row(children: [
              const AppImage('mailbox', width: 56, height: 56, radius: 12),
              const SizedBox(width: 12),
              Expanded(child: Text('В ящике писем: ${v.mailboxCount}', style: const TextStyle(color: AppColors.muted))),
            ]),
          ];
        }
        final need = lettersPerPlayer(v);
        return [
          Row(children: [
            const AppImage('mailbox', width: 56, height: 56, radius: 12),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                'Выберите на руке ${need == 2 ? 'две карты' : 'карту'} и нажмите «Отправить письмо». '
                'Можно сказать в чат, какую улику проверяете.',
              ),
            ),
          ]),
        ];
      case 'GhostPick':
      case 'Refill':
        return [
          if (v.can('RevealHints')) ..._ghostPick(context),
          if (v.can('Discard'))
            const Text('Выберите карту на руке, чтобы сбросить её и добрать новую, или оставьте руку как есть.'),
          if (!v.can('RevealHints') && !v.can('Discard'))
            Text(
              v.phase == 'GhostPick' ? 'Призрак читает письма…' : 'Ждём остальных…',
              style: const TextStyle(color: AppColors.muted),
            ),
        ];
      case 'Discussion':
        return _discussion();
      case 'Voting':
      case 'VoteTie':
        return _voting(context);
      case 'Hunt':
      case 'BlackmailerHunt':
        return _hunt(context);
      case 'BlackmailerClaim':
        if (!v.can('NameTruth')) return [const Text('Шантажист называет улики…', style: TextStyle(color: AppColors.muted))];
        return [Text('Нажимайте на карты поля — по одной в ряду. Выбрано ${screen.truth.length} из ${v.board.length}.')];
      case 'AwardNomination':
      case 'AwardVoting':
      case 'Finished':
        return _results(context);
      default:
        return const [];
    }
  }

  List<Widget> _ghostPick(BuildContext context) {
    final mailbox = v.mailboxForGhost ?? const <String>[];
    return [
      const Text('Нажмите на письма, которые станут подсказками. Остальные исчезнут.'),
      const SizedBox(height: 10),
      Wrap(spacing: 8, runSpacing: 8, children: [
        for (final c in mailbox)
          GestureDetector(
            key: Key('mailbox-$c'),
            onLongPress: () => showCardZoom(context, c, caption: 'Письмо в ящике'),
            onTap: () {
              HapticFeedback.selectionClick();
              if (!screen.selectedMailbox.remove(c)) screen.selectedMailbox.add(c);
              screen.refresh();
            },
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 150),
              foregroundDecoration: BoxDecoration(
                borderRadius: BorderRadius.circular(10),
                border: Border.all(
                  color: screen.selectedMailbox.contains(c) ? AppColors.ice : AppColors.border,
                  width: screen.selectedMailbox.contains(c) ? 3 : 1,
                ),
              ),
              child: Opacity(
                opacity: screen.selectedMailbox.isEmpty || screen.selectedMailbox.contains(c) ? 1 : 0.6,
                child: CardImage(cardId: c, size: 68, radius: 10),
              ),
            ),
          ),
      ]),
    ];
  }

  List<Widget> _discussion() {
    if (v.isRadio) {
      final speaker = v.currentSpeaker;
      return [
        Row(children: [
          const AppImage('radio', width: 40, height: 40, circle: true),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              speaker == null
                  ? 'Обсуждение по рации'
                  : '${speaker == v.me?.id ? 'Говорите вы' : 'Говорит ${screen.nick(speaker)}'}'
                      '${v.floorGrantedTo != null ? ' · слово у ${screen.nick(v.floorGrantedTo)}' : ''}',
            ),
          ),
        ]),
        if (v.raisedHands.isNotEmpty) ...[
          const SizedBox(height: 6),
          Text(
            'Руку подняли: ${v.raisedHands.map(screen.nick).join(', ')}',
            style: const TextStyle(fontSize: 12, color: AppColors.muted),
          ),
        ],
        if (v.can('GiveFloor')) ...[
          const SizedBox(height: 10),
          Text('Передать слово:', style: sectionLabel()),
          const SizedBox(height: 6),
          PlayerPicker(
            screen: screen,
            candidates: [for (final p in v.players) if (p.id != v.me?.id && !p.isGhost) p.id],
          ),
          const SizedBox(height: 8),
          OutlinedButton(
            onPressed: screen.target == null ? null : () => screen.send('GiveFloor', {'to': screen.target}),
            child: Text(screen.target == null ? 'Дать слово' : 'Дать слово: ${screen.nick(screen.target)}'),
          ),
        ],
        if (v.can('RaiseHand')) ...[
          const SizedBox(height: 8),
          OutlinedButton.icon(
            onPressed: () => screen.send('RaiseHand', {'raised': !v.raisedHands.contains(v.me?.id)}),
            icon: const Icon(Icons.pan_tool_outlined),
            label: Text(v.raisedHands.contains(v.me?.id) ? 'Опустить руку' : 'Поднять руку'),
          ),
        ],
      ];
    }

    return [
      const Text('Обсуждайте в чате — текстом или голосом. Когда все нажмут «Готов», начнётся следующий раунд.'),
      const SizedBox(height: 8),
      OutlinedButton.icon(onPressed: screen.openChat, icon: const Icon(Icons.chat_bubble_outline), label: const Text('Открыть чат')),
    ];
  }

  List<Widget> _voting(BuildContext context) {
    final finale = v.finale;
    final stage = finale?.currentStage;
    final widgets = <Widget>[];
    if (finale != null && finale.stagesTotal > 0) widgets.add(_StageChips(screen: screen));

    if (stage != null) {
      widgets.add(const SizedBox(height: 10));
      widgets.add(Text(
        stage.isRow
            ? 'Этап ${stage.index + 1} из ${finale!.stagesTotal}: ${T.category(v.board[stage.row].category)} — какая карта истинная?'
            : 'Этап ${stage.index + 1} из ${finale!.stagesTotal}: кто Убийца?',
        style: const TextStyle(fontWeight: FontWeight.w600),
      ));
      if (stage.attempt > 1) {
        widgets.add(Text('Переголосование №${stage.attempt - 1}', style: const TextStyle(color: AppColors.amber)));
      }

      if (v.phase == 'VoteTie') {
        widgets.add(const SizedBox(height: 10));
        widgets.add(TieBreakdown(screen: screen, stage: stage));
      }

      if (v.can('CastVote')) {
        widgets.add(const SizedBox(height: 10));
        if (stage.isRow) {
          widgets.add(_ColumnPicker(screen: screen, stage: stage));
        } else {
          widgets.add(PlayerPicker(screen: screen, candidates: stage.candidateSuspects));
        }
        widgets.addAll([
          const SizedBox(height: 8),
          TextButton(onPressed: () => screen.send('CastVote', {'column': null, 'suspect': null}), child: const Text('Воздержаться')),
        ]);
      } else if (finale.hasMyVote) {
        widgets.add(const Padding(
          padding: EdgeInsets.only(top: 6),
          child: Text('Ваш голос принят. Выбор откроется, когда проголосуют все.', style: TextStyle(color: AppColors.muted)),
        ));
      }
      widgets.add(const SizedBox(height: 10));
      widgets.add(_VotersProgress(screen: screen));
    }

    widgets.addAll(_outcomes(context));
    return widgets;
  }

  List<Widget> _outcomes(BuildContext context) {
    final finale = v.finale;
    if (finale == null || finale.outcomes.isEmpty) return const [];
    return [
      const Divider(),
      Text('РЕШЕНИЯ', style: sectionLabel()),
      const SizedBox(height: 4),
      for (final o in finale.outcomes)
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Row(children: [
            if (o.kind == 'Row' && o.column != null)
              CardImage(cardId: v.board[o.row].cards[o.column!], size: 40, radius: 8)
            else
              Avatar(nickname: screen.nick(o.suspect), color: screen.colorOf(o.suspect), size: 40),
            const SizedBox(width: 10),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(o.kind == 'Row'
                    ? (o.column == null ? '${T.category(v.board[o.row].category)}: никто' : '${T.category(v.board[o.row].category)}: карта ${o.column! + 1}')
                    : 'Арестован(а) ${screen.nick(o.suspect)}${o.revealedRole != null ? ' — ${T.role(o.revealedRole)}' : ''}'),
                if (o.byLot || _votersFor(o).isNotEmpty)
                  Text(
                    [if (o.byLot) 'жребий', _votersFor(o)].where((x) => x.isNotEmpty).join(' · '),
                    style: const TextStyle(fontSize: 12, color: AppColors.muted),
                  ),
              ]),
            ),
            if (o.correct != null)
              CountBadge(icon: o.correct! ? Icons.check : Icons.close, color: o.correct! ? AppColors.green : AppColors.red, fontSize: 12),
          ]),
        ),
    ];
  }

  String _votersFor(VoteOutcome o) {
    final votes = v.finale!.votes.where((r) => r.stage == o.stage).toList();
    if (votes.isEmpty) return '';
    final last = votes.map((r) => r.attempt).reduce((a, b) => a > b ? a : b);
    final names = votes
        .where((r) => r.attempt == last && (o.kind == 'Row' ? r.column == o.column : r.suspect == o.suspect))
        .map((r) => screen.nick(r.voter));
    return names.isEmpty ? '' : 'за: ${names.join(', ')}';
  }

  List<Widget> _hunt(BuildContext context) {
    final command = v.can('HuntPick') ? 'HuntPick' : (v.can('BlackmailerPick') ? 'BlackmailerPick' : null);
    if (command == null) {
      return [
        Row(children: [
          const AppImage('hunt', width: 56, height: 56, radius: 12),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              v.phase == 'Hunt' ? 'Убийца ищет Свидетеля или Эксперта…' : 'Убийца ищет Шантажиста…',
              style: const TextStyle(color: AppColors.muted),
            ),
          ),
        ]),
        ..._outcomes(context),
      ];
    }

    final me = v.me?.id;
    final candidates = [
      for (final p in v.players)
        if (p.id != me && !p.isGhost && !isKillerTeam(p.knownRole) && v.finale?.arrested.contains(p.id) != true) p.id,
    ];
    return [
      const Text('Выберите игрока. Сообщники могут подсказать в канале команды.', style: TextStyle(color: AppColors.redSoft)),
      const SizedBox(height: 10),
      PlayerPicker(screen: screen, candidates: candidates, color: AppColors.redBright),
      if (command == 'HuntPick') ...[
        const SizedBox(height: 10),
        Row(children: [
          Expanded(
            child: FilledButton(
              key: const Key('hunt-witness'),
              style: FilledButton.styleFrom(backgroundColor: AppColors.red, foregroundColor: Colors.white),
              onPressed: screen.target == null ? null : () => screen.send(command, {'target': screen.target, 'guess': 'Witness'}),
              child: const Text('Это Свидетель'),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: OutlinedButton(
              key: const Key('hunt-expert'),
              onPressed: screen.target == null ? null : () => screen.send(command, {'target': screen.target, 'guess': 'Expert'}),
              child: const Text('Это Эксперт'),
            ),
          ),
        ]),
      ],
    ];
  }

  List<Widget> _results(BuildContext context) {
    final finale = v.finale;
    final result = finale?.result;
    final me = v.me;
    final widgets = <Widget>[];
    if (result != null) {
      final won = me != null && result.winners.contains(me.id);
      widgets.addAll([
        Container(
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: result.side == 'Detectives' ? const Color(0xFF13301F) : const Color(0xFF341714),
            borderRadius: BorderRadius.circular(14),
          ),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(T.sides[result.side] ?? result.side, style: heading(20)),
            const SizedBox(height: 4),
            Text(
              'Угадано рядов: ${result.correctRows} из ${v.board.length}${result.killerCaught ? ' · Убийца арестован' : ''}',
              style: const TextStyle(color: AppColors.muted),
            ),
            if (result.imitatorWon) const Text('Подражатель добился ареста и тоже победил!'),
            if (me != null) ...[
              const SizedBox(height: 6),
              Text(won ? 'Вы победили' : 'Вы проиграли',
                  style: heading(16, color: won ? AppColors.believed : AppColors.redSoft)),
            ],
          ]),
        ),
        if (v.truth != null) ...[
          const SizedBox(height: 12),
          Text('ИСТИННЫЕ УЛИКИ', style: sectionLabel()),
          const SizedBox(height: 6),
          _TruthRow(screen: screen),
        ],
        const SizedBox(height: 12),
        Text('ИГРОКИ', style: sectionLabel()),
        for (final p in [...v.players]..sort((a, b) => a.seat.compareTo(b.seat)))
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 4),
            child: Row(children: [
              Avatar(nickname: screen.nick(p.id), color: screen.colorOf(p.id), size: 34, highlight: p.id == me?.id),
              const SizedBox(width: 10),
              Expanded(child: Text(screen.nick(p.id))),
              Text(T.role(p.knownRole), style: TextStyle(fontSize: 13, color: isKillerTeam(p.knownRole) ? AppColors.redSoft : AppColors.muted)),
              const SizedBox(width: 8),
              Icon(
                result.winners.contains(p.id) ? Icons.emoji_events : Icons.close,
                size: 18,
                color: result.winners.contains(p.id) ? AppColors.amber : AppColors.dim,
              ),
            ]),
          ),
      ]);
    }

    if (finale != null && finale.likes.isNotEmpty && me != null) {
      widgets.addAll([
        const Divider(),
        Text('ЛАЙКИ ЗА ИГРУ', style: sectionLabel()),
        const SizedBox(height: 6),
        Wrap(spacing: 6, runSpacing: 6, children: [
          for (final l in finale.likes.where((l) => l.player != me.id))
            FilterChip(
              showCheckmark: false,
              avatar: Icon(l.likedByMe ? Icons.favorite : Icons.favorite_border, size: 16, color: AppColors.redBright),
              label: Text('${screen.nick(l.player)} · ${l.count}'),
              selected: l.likedByMe,
              onSelected: v.can('Like') ? (on) => screen.send('Like', {'to': l.player, 'on': on}) : null,
            ),
        ]),
      ]);
    }

    if (v.can('Nominate')) {
      widgets.addAll([
        const Divider(),
        Text('ВЫДВИЖЕНИЕ НА АЧИВКУ', style: sectionLabel()),
        const SizedBox(height: 4),
        const Text('Выберите ачивку, затем игрока. Одно выдвижение на игрока.', style: TextStyle(fontSize: 13, color: AppColors.muted)),
        const SizedBox(height: 8),
        for (final n in awardTitles.entries)
          Padding(
            padding: const EdgeInsets.only(bottom: 6),
            child: _AwardTile(
              key: Key('nominate-${n.key}'),
              icon: _awardIcons[n.key]!,
              title: n.value,
              subtitle: _awardHints[n.key]!,
              onTap: () => _nominate(context, n.key),
            ),
          ),
        TextButton(onPressed: () => screen.send('Nominate', {'code': null, 'nominee': null}), child: const Text('Пропустить')),
      ]);
    }

    if (finale != null && finale.awards.isNotEmpty) {
      widgets.addAll([
        const Divider(),
        Text(v.phase == 'AwardVoting' ? 'ГОЛОСОВАНИЕ ЗА АЧИВКИ' : 'АЧИВКИ', style: sectionLabel()),
        const SizedBox(height: 6),
        for (final a in finale.awards)
          Padding(
            padding: const EdgeInsets.only(bottom: 6),
            child: Container(
              padding: const EdgeInsets.fromLTRB(10, 8, 6, 8),
              decoration: BoxDecoration(
                color: a.won ? const Color(0xFF3A2B12) : AppColors.surface2,
                borderRadius: BorderRadius.circular(12),
                border: a.won ? Border.all(color: AppColors.amber) : null,
              ),
              child: Row(children: [
                Avatar(nickname: screen.nick(a.nominee), color: screen.colorOf(a.nominee), size: 34),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(awardTitles[a.code] ?? a.code, style: const TextStyle(fontWeight: FontWeight.w600)),
                    Text(
                      '${screen.nick(a.nominee)}${a.votes == null ? '' : ' · голосов: ${a.votes}'}',
                      style: const TextStyle(fontSize: 12, color: AppColors.muted),
                    ),
                  ]),
                ),
                if (a.won) const Icon(Icons.emoji_events, color: AppColors.amber),
                if (v.can('AwardVote') && !a.mine && a.nominee != me?.id)
                  TextButton(
                    key: Key('award-vote-${a.index}'),
                    onPressed: () => screen.send('AwardVote', {'entry': a.index}),
                    child: const Text('Голос'),
                  ),
              ]),
            ),
          ),
        if (v.can('AwardVote')) TextButton(onPressed: () => screen.send('AwardVote', {'entry': null}), child: const Text('Пропустить')),
      ]);
    }

    widgets.addAll(_outcomes(context));

    final lobbyId = screen.lobbyId;
    if (v.phase == 'Finished' && lobbyId != null && v.me != null) {
      widgets.addAll([
        const SizedBox(height: 8),
        OutlinedButton.icon(
          key: const Key('back-to-lobby'),
          onPressed: () => screen.openLobby(lobbyId),
          icon: const Icon(Icons.replay),
          label: const Text('В лобби — сыграть ещё'),
        ),
      ]);
    }

    return widgets;
  }

  /// Выдвинуть на ачивку: сначала ачивка, затем игрок в нижнем листе.
  Future<void> _nominate(BuildContext context, String code) async {
    final me = v.me?.id;
    final candidates = [for (final p in [...v.players]..sort((a, b) => a.seat.compareTo(b.seat))) if (p.id != me) p.id];
    final picked = await showModalBottomSheet<String>(
      context: context,
      builder: (context) => SafeArea(
        child: ListView(shrinkWrap: true, padding: const EdgeInsets.fromLTRB(16, 0, 16, 16), children: [
          Text(awardTitles[code]!.toUpperCase(), style: heading(20)),
          const SizedBox(height: 4),
          Text('Кого выдвигаете?', style: TextStyle(color: AppColors.muted.withValues(alpha: 0.9))),
          const SizedBox(height: 8),
          for (final id in candidates)
            ListTile(
              key: Key('nominee-$id'),
              contentPadding: EdgeInsets.zero,
              leading: Avatar(nickname: screen.nick(id), color: screen.colorOf(id), size: 40),
              title: Text(screen.nick(id)),
              onTap: () => Navigator.pop(context, id),
            ),
        ]),
      ),
    );
    if (picked != null) await screen.send('Nominate', {'code': code, 'nominee': picked});
  }

  static String awardTitle(String code) => awardTitles[code] ?? code;
}

class _AwardTile extends StatelessWidget {
  const _AwardTile({super.key, required this.icon, required this.title, required this.subtitle, required this.onTap});

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
        color: AppColors.surface2,
        borderRadius: BorderRadius.circular(12),
        child: InkWell(
          borderRadius: BorderRadius.circular(12),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            child: Row(children: [
              Icon(icon, color: AppColors.amber),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
                  Text(subtitle, style: const TextStyle(fontSize: 12, color: AppColors.muted)),
                ]),
              ),
              const Icon(Icons.chevron_right, color: AppColors.dim),
            ]),
          ),
        ),
      );
}

/// Выбор игрока прямо в панели: аватары кандидатов, выбранный — с янтарной (красной для охоты) обводкой.
class PlayerPicker extends StatelessWidget {
  const PlayerPicker({super.key, required this.screen, required this.candidates, this.color = AppColors.amber});

  final GameScreenState screen;
  final List<String> candidates;
  final Color color;

  @override
  Widget build(BuildContext context) {
    if (candidates.isEmpty) return const SizedBox.shrink();
    return Wrap(spacing: 10, runSpacing: 10, children: [
      for (final id in candidates)
        GestureDetector(
          key: Key('pick-$id'),
          onTap: () {
            HapticFeedback.selectionClick();
            screen.tapPlayer(id);
          },
          child: SizedBox(
            width: 58,
            child: Column(children: [
              AnimatedContainer(
                duration: const Duration(milliseconds: 150),
                padding: const EdgeInsets.all(2),
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  border: Border.all(color: screen.target == id ? color : Colors.transparent, width: 2.5),
                ),
                child: Avatar(nickname: screen.nick(id), color: screen.colorOf(id), size: 44),
              ),
              const SizedBox(height: 3),
              Text(
                id == screen.view?.me?.id ? 'Вы' : screen.nick(id),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 11, color: screen.target == id ? color : AppColors.muted),
              ),
            ]),
          ),
        ),
    ]);
  }
}

/// Голосование по ряду: карты-кандидаты с номерами — не нужно искать ряд на поле.
class _ColumnPicker extends StatelessWidget {
  const _ColumnPicker({required this.screen, required this.stage});

  final GameScreenState screen;
  final VoteStage stage;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final row = v.board[stage.row];
    final selected = screen.voteColumn ?? v.finale?.myVoteColumn;
    return LayoutBuilder(builder: (context, box) {
      final n = stage.candidateColumns.length;
      final size = ((box.maxWidth - 8 * (n - 1)) / n).clamp(36.0, 72.0).floorToDouble();
      return Wrap(spacing: 8, runSpacing: 8, children: [
        for (final c in stage.candidateColumns)
          GestureDetector(
            key: Key('vote-col-$c'),
            onTap: () => screen.tapCard(stage.row, c, row.cards[c]),
            child: Column(children: [
              BoardCard(cardId: row.cards[c], size: size, mark: screen.marks[row.cards[c]], chosen: selected == c),
              const SizedBox(height: 2),
              Text('${c + 1}', style: heading(12, color: selected == c ? AppColors.amber : AppColors.dim, spacing: 0)),
            ]),
          ),
      ]);
    });
  }
}

/// Этапы финала: ряды и «Убийца», пройденные — с итогом.
class _StageChips extends StatelessWidget {
  const _StageChips({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final finale = v.finale!;
    final current = finale.currentStage?.index;
    final labels = [for (final r in v.board) T.category(r.category), if (finale.stagesTotal > v.board.length) 'Убийца'];
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(children: [
        for (var i = 0; i < labels.length && i < finale.stagesTotal; i++)
          Padding(
            padding: const EdgeInsets.only(right: 6),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
              decoration: BoxDecoration(
                color: i == current ? AppColors.amber : AppColors.surface2,
                borderRadius: BorderRadius.circular(99),
              ),
              child: Row(mainAxisSize: MainAxisSize.min, children: [
                Text(
                  labels[i],
                  style: TextStyle(fontSize: 12, color: i == current ? AppColors.onAmber : AppColors.muted, fontWeight: i == current ? FontWeight.w600 : null),
                ),
                if (finale.outcomes.any((o) => o.stage == i)) ...[
                  const SizedBox(width: 4),
                  const Icon(Icons.check, size: 14, color: AppColors.believed),
                ],
              ]),
            ),
          ),
      ]),
    );
  }
}

/// Кто уже проголосовал: аватары по кругу, проголосовавшие — с галочкой.
class _VotersProgress extends StatelessWidget {
  const _VotersProgress({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final voters = v.players.where((p) => !p.isGhost).toList()..sort((a, b) => a.seat.compareTo(b.seat));
    if (voters.isEmpty) return const SizedBox.shrink();
    final done = voters.where((p) => p.hasActed).length;
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text('Проголосовали $done из ${voters.length}', style: const TextStyle(fontSize: 12, color: AppColors.muted)),
      const SizedBox(height: 6),
      Wrap(spacing: 4, runSpacing: 4, children: [
        for (final p in voters)
          Opacity(
            opacity: p.hasActed ? 1 : 0.35,
            child: Avatar(nickname: screen.nick(p.id), color: screen.colorOf(p.id), size: 26),
          ),
      ]),
    ]);
  }
}

/// Итог по рядам: истинная карта каждого ряда и угадали ли её.
class _TruthRow extends StatelessWidget {
  const _TruthRow({required this.screen});

  final GameScreenState screen;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final truth = v.truth!;
    final outcomes = v.finale?.outcomes ?? const <VoteOutcome>[];
    return Wrap(spacing: 10, runSpacing: 10, children: [
      for (var r = 0; r < v.board.length && r < truth.length; r++)
        Column(children: [
          Stack(clipBehavior: Clip.none, children: [
            CardImage(cardId: v.board[r].cards[truth[r]], size: 64, radius: 10),
            for (final o in outcomes.where((o) => o.kind == 'Row' && o.row == r && o.correct != null))
              Positioned(
                right: -4,
                top: -4,
                child: CountBadge(icon: o.correct! ? Icons.check : Icons.close, color: o.correct! ? AppColors.green : AppColors.red, fontSize: 12),
              ),
          ]),
          const SizedBox(height: 3),
          Text(T.category(v.board[r].category), style: const TextStyle(fontSize: 11, color: AppColors.ice)),
        ]),
    ]);
  }
}

class _Illustration extends StatelessWidget {
  const _Illustration(this.name);

  final String name;

  @override
  Widget build(BuildContext context) => AppImage(name, height: 140, width: double.infinity, radius: 12);
}

/// Знакомство с ролью: карта рубашкой вверх, по нажатию переворачивается.
class RoleReveal extends StatefulWidget {
  const RoleReveal({super.key, required this.role, this.width = 170, this.footer});

  final String role;
  final double width;

  /// Что показать под открытой картой: кто Призрак, кто в команде.
  final Widget? footer;

  @override
  State<RoleReveal> createState() => _RoleRevealState();
}

class _RoleRevealState extends State<RoleReveal> {
  bool _open = false;

  @override
  Widget build(BuildContext context) {
    final killer = isKillerTeam(widget.role);
    final w = widget.width;
    return Column(mainAxisSize: MainAxisSize.min, children: [
      GestureDetector(
        key: const Key('role-card'),
        onTap: () => setState(() => _open = !_open),
        child: AnimatedSwitcher(
          duration: const Duration(milliseconds: 400),
          transitionBuilder: (child, animation) => ScaleTransition(
            scale: Tween<double>(begin: 0.85, end: 1).animate(animation),
            child: FadeTransition(opacity: animation, child: child),
          ),
          child: Container(
            key: ValueKey(_open),
            foregroundDecoration: BoxDecoration(
              borderRadius: BorderRadius.circular(18),
              border: _open ? Border.all(color: killer ? AppColors.redBright : AppColors.ice, width: 2) : null,
            ),
            child: AppImage(_open ? roleImage(widget.role) : 'role_back', width: w, height: w * 1.42, radius: 18),
          ),
        ),
      ),
      const SizedBox(height: 16),
      if (!_open) ...[
        Text('ВАША РОЛЬ', style: heading(24, color: AppColors.ice, spacing: 2)),
        const SizedBox(height: 6),
        const Text(
          'Убедитесь, что никто не смотрит в ваш экран, и нажмите на карту',
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 15, color: AppColors.muted, height: 1.5),
        ),
      ] else ...[
        Text(T.role(widget.role).toUpperCase(), style: heading(32, color: killer ? AppColors.redSoft : AppColors.ice, spacing: 3)),
        const SizedBox(height: 6),
        Text(T.roleHints[widget.role] ?? '', textAlign: TextAlign.center, style: const TextStyle(fontSize: 15, height: 1.5)),
        if (widget.footer != null) ...[const SizedBox(height: 14), widget.footer!],
      ],
    ]);
  }
}

/// Ничья: голоса открыты — у каждого варианта видно, кто за него голосовал.
class TieBreakdown extends StatelessWidget {
  const TieBreakdown({super.key, required this.screen, required this.stage});

  final GameScreenState screen;
  final VoteStage stage;

  @override
  Widget build(BuildContext context) {
    final v = screen.view!;
    final votes = v.finale!.votes.where((r) => r.stage == stage.index).toList();
    if (votes.isEmpty) return const SizedBox.shrink();
    final last = votes.map((r) => r.attempt).reduce((a, b) => a > b ? a : b);
    final current = votes.where((r) => r.attempt == last).toList();
    final groups = <Object?, List<String>>{};
    for (final r in current) {
      groups.putIfAbsent(stage.isRow ? r.column : r.suspect, () => []).add(r.voter);
    }
    final entries = groups.entries.toList()..sort((a, b) => b.value.length.compareTo(a.value.length));
    final top = entries.isEmpty ? 0 : entries.first.value.length;
    final tied = entries.where((e) => e.value.length == top && e.key != null).length;

    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Text('НИЧЬЯ ${List.filled(tied < 2 ? 2 : tied, top).join(' : ')}', style: heading(26, color: AppColors.amber, spacing: 2)),
      const Text('Голоса открыты: всем видно, кто за что голосовал', style: TextStyle(fontSize: 12, color: AppColors.muted)),
      const SizedBox(height: 8),
      for (final e in entries)
        Padding(
          padding: const EdgeInsets.only(bottom: 6),
          child: Container(
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: AppColors.surface2,
              borderRadius: BorderRadius.circular(12),
              border: e.value.length == top && e.key != null ? Border.all(color: AppColors.amber) : null,
            ),
            child: Row(children: [
              if (e.key == null)
                const SizedBox(width: 44, child: Icon(Icons.block, color: AppColors.dim))
              else if (stage.isRow)
                CardImage(cardId: v.board[stage.row].cards[e.key! as int], size: 44, radius: 8)
              else
                Avatar(nickname: screen.nick(e.key! as String), color: screen.colorOf(e.key! as String), size: 44),
              const SizedBox(width: 10),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(
                    e.key == null ? 'Воздержались' : (stage.isRow ? '№${(e.key! as int) + 1}' : screen.nick(e.key! as String)),
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  Text(e.value.map(screen.nick).join(', '), style: const TextStyle(fontSize: 12, color: AppColors.muted)),
                ]),
              ),
              Text('${e.value.length}', style: heading(22, spacing: 0)),
            ]),
          ),
        ),
      if (stage.attempt > 1)
        Text(
          stage.isRow
              ? 'Голосуем снова только между ${stage.candidateColumns.map((c) => '№${c + 1}').join(' и ')}. '
                  'Переголосование ${stage.attempt - 1} из 3 — если ничья повторится, решит жребий.'
              : 'Переголосование ${stage.attempt - 1} из 3 — если ничья повторится, решит жребий.',
          style: const TextStyle(fontSize: 13, color: AppColors.muted),
        ),
    ]);
  }
}
