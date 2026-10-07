import 'package:flutter/material.dart';

import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'game_screen.dart';
import 'game_state.dart';

/// Панель действий текущей фазы: что можно сделать и кнопки команд.
class ActionPanel extends StatelessWidget {
  const ActionPanel({super.key, required this.screen});

  final GameScreenState screen;

  GameView get v => screen.view!;

  @override
  Widget build(BuildContext context) {
    final children = <Widget>[
      Text(actionHint(v), style: Theme.of(context).textTheme.titleMedium),
      const SizedBox(height: 8),
      ..._body(context),
    ];
    return Card(child: Padding(padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: children)));
  }

  List<Widget> _body(BuildContext context) {
    final me = v.me;
    switch (v.phase) {
      case 'RoleReveal':
        if (me == null) return const [];
        return [
          Text(T.role(me.role), style: Theme.of(context).textTheme.headlineSmall),
          Text(T.roleHints[me.role] ?? ''),
          const SizedBox(height: 8),
          if (v.can('AckRole')) FilledButton(onPressed: () => screen.send('AckRole'), child: const Text('Понятно')),
        ];
      case 'Night':
        if (!v.can('ChooseTruth')) return [const Text('Убийца выбирает истинные улики…')];
        return [
          Text('Выбрано ${screen.truth.length} из ${v.board.length}. Нажимайте на карты поля.'),
          const SizedBox(height: 8),
          FilledButton(
            onPressed: screen.truth.length == v.board.length
                ? () => screen.send('ChooseTruth', {'columns': [for (var r = 0; r < v.board.length; r++) screen.truth[r]]})
                : null,
            child: const Text('Это истина'),
          ),
        ];
      case 'FirstClue':
        if (!v.can('GiveFirstClue')) return [const Text('Призрак думает о первой зацепке…')];
        return [
          const Text('Выберите карту на руке или начните без зацепки.'),
          const SizedBox(height: 8),
          Row(children: [
            Expanded(
              child: FilledButton(
                onPressed: screen.selectedHand.length == 1
                    ? () => screen.send('GiveFirstClue', {'cardId': screen.selectedHand.first})
                    : null,
                child: const Text('Выложить'),
              ),
            ),
            const SizedBox(width: 8),
            Expanded(child: OutlinedButton(onPressed: () => screen.send('GiveFirstClue', {'cardId': null}), child: const Text('Без зацепки'))),
          ]),
        ];
      case 'Mailbox':
        if (!v.can('SendLetter')) return [Text('В ящике писем: ${v.mailboxCount}')];
        final need = lettersPerPlayer(v);
        return [
          Text('Выберите на руке карт: $need. Можно сказать в чат, что проверяете.'),
          const SizedBox(height: 8),
          FilledButton(
            onPressed: screen.selectedHand.length == need ? () => screen.send('SendLetter', {'cardIds': screen.selectedHand.toList()}) : null,
            child: const Text('Отправить письмо'),
          ),
        ];
      case 'GhostPick':
      case 'Refill':
        return [
          if (v.can('RevealHints')) ..._ghostPick(context),
          if (v.can('Discard')) ..._discard(),
          if (!v.can('RevealHints') && !v.can('Discard')) const Text('Ждём остальных…'),
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
        if (!v.can('NameTruth')) return [const Text('Шантажист называет улики…')];
        return [
          Text('Выбрано ${screen.truth.length} из ${v.board.length}. Нажимайте на карты поля.'),
          FilledButton(
            onPressed: screen.truth.length == v.board.length
                ? () => screen.send('NameTruth', {'columns': [for (var r = 0; r < v.board.length; r++) screen.truth[r]]})
                : null,
            child: const Text('Назвать'),
          ),
        ];
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
      const SizedBox(height: 8),
      Wrap(spacing: 6, runSpacing: 6, children: [
        for (final c in mailbox)
          GestureDetector(
            onTap: () {
              if (!screen.selectedMailbox.remove(c)) screen.selectedMailbox.add(c);
              screen.refresh();
            },
            child: Container(
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(8),
                border: Border.all(color: screen.selectedMailbox.contains(c) ? AppTheme.accent : Colors.transparent, width: 3),
              ),
              child: CardImage(cardId: c, size: 64),
            ),
          ),
      ]),
      const SizedBox(height: 8),
      FilledButton(
        onPressed: () => screen.send('RevealHints', {'cardIds': screen.selectedMailbox.toList()}),
        child: Text(screen.selectedMailbox.isEmpty ? 'Ничего не открывать' : 'Открыть: ${screen.selectedMailbox.length}'),
      ),
      const SizedBox(height: 12),
    ];
  }

  List<Widget> _discard() => [
        const Text('Можно сбросить одну карту с руки и добрать новую.'),
        const SizedBox(height: 8),
        Row(children: [
          Expanded(
            child: OutlinedButton(
              onPressed: screen.selectedHand.length == 1 ? () => screen.send('Discard', {'cardId': screen.selectedHand.first}) : null,
              child: const Text('Сбросить выбранную'),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(child: FilledButton(onPressed: () => screen.send('Discard', {'cardId': null}), child: const Text('Оставить руку'))),
        ]),
      ];

  List<Widget> _discussion() {
    if (v.isRadio) {
      final speaker = v.currentSpeaker;
      return [
        Text(speaker == null ? 'Обсуждение' : 'Говорит: ${screen.nick(speaker)}'
            '${v.floorGrantedTo != null ? ' · слово у ${screen.nick(v.floorGrantedTo)}' : ''}'),
        const SizedBox(height: 8),
        if (v.can('EndTurn'))
          Row(children: [
            Expanded(child: FilledButton(onPressed: () => screen.send('EndTurn'), child: const Text('Закончить слово'))),
            const SizedBox(width: 8),
            Expanded(
              child: OutlinedButton(
                onPressed: screen.target == null ? null : () => screen.send('GiveFloor', {'to': screen.target}),
                child: Text(screen.target == null ? 'Дать слово (выберите игрока)' : 'Дать слово ${screen.nick(screen.target)}'),
              ),
            ),
          ]),
        if (v.can('RaiseHand'))
          OutlinedButton.icon(
            onPressed: () => screen.send('RaiseHand', {'raised': !v.raisedHands.contains(v.me?.id)}),
            icon: const Icon(Icons.pan_tool_outlined),
            label: Text(v.raisedHands.contains(v.me?.id) ? 'Опустить руку' : 'Поднять руку'),
          ),
      ];
    }

    return [
      const Text('Обсуждайте в чате. Когда все будут готовы, начнётся следующий раунд.'),
      const SizedBox(height: 8),
      if (v.can('ReadyNextRound')) FilledButton(onPressed: () => screen.send('ReadyNextRound'), child: const Text('Готов')),
    ];
  }

  List<Widget> _voting(BuildContext context) {
    final finale = v.finale;
    final stage = finale?.currentStage;
    final widgets = <Widget>[];
    if (stage != null) {
      widgets.add(Text(
        stage.isRow
            ? 'Этап ${stage.index + 1} из ${finale!.stagesTotal}: ${T.category(v.board[stage.row].category)} — нажмите на карту ряда'
            : 'Этап ${stage.index + 1} из ${finale!.stagesTotal}: кто Убийца? Выберите игрока вверху',
      ));
      if (stage.attempt > 1) widgets.add(Text('Переголосование №${stage.attempt - 1}', style: const TextStyle(color: Colors.amber)));
    }

    if (v.can('CastVote') && stage != null) {
      final ready = stage.isRow ? screen.voteColumn != null : screen.target != null;
      widgets.addAll([
        const SizedBox(height: 8),
        Row(children: [
          Expanded(
            child: FilledButton(
              onPressed: ready
                  ? () => screen.send('CastVote', stage.isRow ? {'column': screen.voteColumn} : {'suspect': screen.target})
                  : null,
              child: const Text('Голосовать'),
            ),
          ),
          const SizedBox(width: 8),
          OutlinedButton(onPressed: () => screen.send('CastVote', {'column': null, 'suspect': null}), child: const Text('Воздержаться')),
        ]),
      ]);
    } else if (finale?.hasMyVote == true) {
      widgets.add(const Text('Ваш голос принят. Ждём остальных — голоса откроются, когда проголосуют все.'));
    }

    if (v.can('ReadyRevote')) {
      widgets.add(FilledButton(onPressed: () => screen.send('ReadyRevote'), child: const Text('Готов переголосовать')));
    }

    widgets.addAll(_outcomes(context));
    return widgets;
  }

  List<Widget> _outcomes(BuildContext context) {
    final finale = v.finale;
    if (finale == null || finale.outcomes.isEmpty) return const [];
    return [
      const Divider(),
      Text('Решения', style: Theme.of(context).textTheme.titleSmall),
      for (final o in finale.outcomes)
        ListTile(
          dense: true,
          leading: Icon(o.correct == null ? Icons.help_outline : (o.correct! ? Icons.check : Icons.close),
              color: o.correct == null ? null : (o.correct! ? AppTheme.ok : AppTheme.danger)),
          title: Text(o.kind == 'Row'
              ? '${T.category(v.board[o.row].category)}: карта ${(o.column ?? 0) + 1}'
              : 'Арестован(а) ${screen.nick(o.suspect)}${o.revealedRole != null ? ' — ${T.role(o.revealedRole)}' : ''}'),
          subtitle: Text([
            if (o.byLot) 'жребий',
            _votersFor(o),
          ].where((x) => x.isNotEmpty).join(' · ')),
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
      return [Text(v.phase == 'Hunt' ? 'Убийца ищет Свидетеля или Эксперта…' : 'Убийца ищет Шантажиста…'), ..._outcomes(context)];
    }

    final needGuess = command == 'HuntPick';
    return [
      const Text('Выберите игрока вверху. Сообщники могут подсказать в канале команды.'),
      const SizedBox(height: 8),
      if (needGuess)
        Row(children: [
          Expanded(
            child: FilledButton(
              onPressed: screen.target == null ? null : () => screen.send(command, {'target': screen.target, 'guess': 'Witness'}),
              child: const Text('Это Свидетель'),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: OutlinedButton(
              onPressed: screen.target == null
                  ? null
                  : () => screen.send(command, {'target': screen.target, 'guess': 'Expert'}),
              child: const Text('Это Эксперт'),
            ),
          ),
        ])
      else
        FilledButton(
          onPressed: screen.target == null ? null : () => screen.send(command, {'target': screen.target}),
          child: const Text('Это Шантажист'),
        ),
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
        Text(T.sides[result.side] ?? result.side, style: Theme.of(context).textTheme.titleLarge),
        Text('Угадано рядов: ${result.correctRows} из ${v.board.length}${result.killerCaught ? ', Убийца арестован' : ''}'),
        if (result.imitatorWon) const Text('Подражатель добился ареста и тоже победил!'),
        if (me != null) Text(won ? 'Вы победили' : 'Вы проиграли', style: TextStyle(color: won ? AppTheme.ok : AppTheme.danger)),
        const SizedBox(height: 8),
        Text('Победители: ${result.winners.map(screen.nick).join(', ')}'),
      ]);
    }

    if (finale != null && finale.likes.isNotEmpty && me != null) {
      widgets.addAll([
        const Divider(),
        Text('Лайки', style: Theme.of(context).textTheme.titleSmall),
        Wrap(spacing: 6, children: [
          for (final l in finale.likes.where((l) => l.player != me.id))
            FilterChip(
              label: Text('${screen.nick(l.player)} · ${l.count}'),
              selected: l.likedByMe,
              avatar: const Icon(Icons.favorite, size: 16),
              onSelected: v.can('Like') ? (on) => screen.send('Like', {'to': l.player, 'on': on}) : null,
            ),
        ]),
      ]);
    }

    if (v.can('Nominate')) {
      widgets.addAll([
        const Divider(),
        const Text('Выберите игрока вверху и номинацию. Одно выдвижение на игрока.'),
        Wrap(spacing: 6, children: [
          for (final n in const {'steel_balls': 'Стальные яйца', 'sherlock': 'Шерлок', 'best_liar': 'Лучший лжец', 'ghost_whisperer': 'Голос Призрака'}.entries)
            ActionChip(
              label: Text(n.value),
              onPressed: screen.target == null ? null : () => screen.send('Nominate', {'code': n.key, 'nominee': screen.target}),
            ),
        ]),
        TextButton(onPressed: () => screen.send('Nominate', {'code': null, 'nominee': null}), child: const Text('Пропустить')),
      ]);
    }

    if (finale != null && finale.awards.isNotEmpty) {
      widgets.addAll([
        const Divider(),
        Text('Выдвижения', style: Theme.of(context).textTheme.titleSmall),
        for (final a in finale.awards)
          ListTile(
            dense: true,
            leading: Icon(a.won ? Icons.emoji_events : Icons.star_border, color: a.won ? Colors.amber : null),
            title: Text('${_awardTitle(a.code)} — ${screen.nick(a.nominee)}'),
            subtitle: a.votes == null ? null : Text('Голосов: ${a.votes}'),
            trailing: v.can('AwardVote') && !a.mine && a.nominee != me?.id
                ? TextButton(onPressed: () => screen.send('AwardVote', {'entry': a.index}), child: const Text('Голос'))
                : null,
          ),
        if (v.can('AwardVote')) TextButton(onPressed: () => screen.send('AwardVote', {'entry': null}), child: const Text('Пропустить')),
      ]);
    }

    widgets.addAll(_outcomes(context));
    return widgets;
  }

  static String _awardTitle(String code) => const {
        'steel_balls': 'Стальные яйца',
        'sherlock': 'Шерлок',
        'best_liar': 'Лучший лжец',
        'ghost_whisperer': 'Голос Призрака',
      }[code] ??
      code;
}
