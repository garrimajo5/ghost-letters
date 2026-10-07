import 'package:flutter/material.dart';

import '../../models/models.dart';

/// Редактор настроек лобби. Во время партии меняются только темп и таймеры.
class SettingsSheet extends StatefulWidget {
  const SettingsSheet({super.key, required this.initial, required this.inGame});

  final LobbySettings initial;
  final bool inGame;

  static Future<LobbySettings?> show(BuildContext context, LobbySettings initial, {bool inGame = false}) =>
      showModalBottomSheet<LobbySettings>(
        context: context,
        isScrollControlled: true,
        useSafeArea: true,
        builder: (_) => SettingsSheet(initial: initial, inGame: inGame),
      );

  @override
  State<SettingsSheet> createState() => _SettingsSheetState();
}

class _SettingsSheetState extends State<SettingsSheet> {
  late LobbySettings s = widget.initial;

  static const _sets = {'original': 'Оригинальный', 'mailbox': 'Почтовый ящик', 'ritual': 'Тайный ритуал', 'mirror': 'Зеркало истины'};

  bool get _rulesLocked => widget.inGame;

  void _timer(String key, int value) => setState(() => s = s.copyWith(timers: {...s.timers, key: value}));

  @override
  Widget build(BuildContext context) {
    final roles = s.roles;
    return DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.9,
      builder: (context, scroll) => ListView(
        controller: scroll,
        padding: const EdgeInsets.all(16),
        children: [
          Text('Настройки партии', style: Theme.of(context).textTheme.titleLarge),
          if (_rulesLocked)
            const Padding(
              padding: EdgeInsets.only(top: 8),
              child: Text('Партия идёт: можно менять только темп и таймеры.'),
            ),
          const SizedBox(height: 8),
          SwitchListTile(
            title: const Text('Ряд «Тайна»'),
            subtitle: const Text('Четвёртый ряд улик из дополнения'),
            value: s.useSecretRow,
            onChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(useSecretRow: v)),
          ),
          _Stepper(
            label: 'Карт в ряду',
            value: s.columns,
            min: 4,
            max: 7,
            onChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(columns: v)),
          ),
          _Stepper(
            label: 'Раунды',
            value: s.rounds ?? 0,
            min: 0,
            max: 5,
            zeroLabel: 'по правилам',
            onChanged: _rulesLocked
                ? null
                : (v) => setState(() => s = v == 0 ? s.copyWith(clearRounds: true) : s.copyWith(rounds: v)),
          ),
          const Divider(),
          Text('Роли', style: Theme.of(context).textTheme.titleMedium),
          SwitchListTile(
            title: const Text('Убийца'),
            subtitle: const Text('Без Убийцы — кооперативная игра'),
            value: roles.killerEnabled,
            onChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(roles: roles.copyWith(killerEnabled: v))),
          ),
          SwitchListTile(
            title: const Text('Свидетель (с 7 игроков)'),
            value: roles.useWitness,
            onChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(roles: roles.copyWith(useWitness: v))),
          ),
          SwitchListTile(
            title: const Text('Эксперт (с 10 игроков)'),
            value: roles.useExpert,
            onChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(roles: roles.copyWith(useExpert: v))),
          ),
          SwitchListTile(
            title: const Text('Шантажист (с 8 игроков)'),
            value: roles.useBlackmailer,
            onChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(roles: roles.copyWith(useBlackmailer: v))),
          ),
          ListTile(
            title: const Text('Подражатель (с 5 игроков)'),
            trailing: DropdownButton<ImitatorMode>(
              value: roles.imitator,
              onChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(roles: roles.copyWith(imitator: v))),
              items: const [
                DropdownMenuItem(value: ImitatorMode.none, child: Text('Нет')),
                DropdownMenuItem(value: ImitatorMode.replaceDetective, child: Text('Вместо детектива')),
                DropdownMenuItem(value: ImitatorMode.replaceAccomplice, child: Text('Вместо сообщника')),
              ],
            ),
          ),
          const Divider(),
          Text('Обсуждение и темп', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'Radio', label: Text('Рация')),
              ButtonSegment(value: 'FreeChat', label: Text('Свободно')),
            ],
            selected: {s.discussion},
            onSelectionChanged: _rulesLocked ? null : (v) => setState(() => s = s.copyWith(discussion: v.first)),
          ),
          const SizedBox(height: 8),
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'live', label: Text('Живая')),
              ButtonSegment(value: 'turn', label: Text('Походовая')),
            ],
            selected: {s.tempo},
            onSelectionChanged: (v) => setState(() => s = s.copyWith(tempo: v.first)),
          ),
          if (s.tempo == 'turn')
            _Stepper(label: 'Часов на ход', value: s.turnHours, min: 1, max: 72, onChanged: (v) => setState(() => s = s.copyWith(turnHours: v)))
          else ...[
            _Stepper(label: 'Письмо, сек', value: s.timer('mailbox', 60), min: 15, max: 600, step: 15, onChanged: (v) => _timer('mailbox', v)),
            _Stepper(label: 'Выбор Призрака, сек', value: s.timer('ghostPick', 90), min: 15, max: 600, step: 15, onChanged: (v) => _timer('ghostPick', v)),
            _Stepper(label: 'Слово по рации, сек', value: s.timer('speakerTurn', 60), min: 15, max: 600, step: 15, onChanged: (v) => _timer('speakerTurn', v)),
            _Stepper(label: 'Свободное обсуждение, сек', value: s.timer('freeDiscussion', 180), min: 30, max: 1800, step: 30, onChanged: (v) => _timer('freeDiscussion', v)),
            _Stepper(label: 'Голосование, сек', value: s.timer('voting', 60), min: 15, max: 600, step: 15, onChanged: (v) => _timer('voting', v)),
          ],
          const Divider(),
          Text('Наборы карт', style: Theme.of(context).textTheme.titleMedium),
          for (final e in _sets.entries)
            CheckboxListTile(
              title: Text(e.value),
              value: s.cardSets.contains(e.key),
              onChanged: _rulesLocked
                  ? null
                  : (v) {
                      final sets = {...s.cardSets};
                      v == true ? sets.add(e.key) : sets.remove(e.key);
                      if (sets.isNotEmpty) setState(() => s = s.copyWith(cardSets: sets.toList()));
                    },
            ),
          const SizedBox(height: 16),
          FilledButton(onPressed: () => Navigator.pop(context, s), child: const Text('Сохранить')),
        ],
      ),
    );
  }
}

class _Stepper extends StatelessWidget {
  const _Stepper({
    required this.label,
    required this.value,
    required this.min,
    required this.max,
    required this.onChanged,
    this.step = 1,
    this.zeroLabel,
  });

  final String label;
  final int value;
  final int min;
  final int max;
  final int step;
  final String? zeroLabel;
  final ValueChanged<int>? onChanged;

  @override
  Widget build(BuildContext context) {
    final change = onChanged;
    return ListTile(
      title: Text(label),
      trailing: Row(mainAxisSize: MainAxisSize.min, children: [
        IconButton(
          onPressed: change == null || value <= min ? null : () => change((value - step).clamp(min, max)),
          icon: const Icon(Icons.remove),
        ),
        Text(value == 0 && zeroLabel != null ? zeroLabel! : '$value'),
        IconButton(
          onPressed: change == null || value >= max ? null : () => change((value + step).clamp(min, max)),
          icon: const Icon(Icons.add),
        ),
      ]),
    );
  }
}
