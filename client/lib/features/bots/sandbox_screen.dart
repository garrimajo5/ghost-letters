import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/api.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

const sandboxScenarios = {
  'full-game': 'Полная партия',
  'clear-hints': 'Сильные подсказки',
  'witness-reveal': 'Свидетель раскрылся'
};
String sandboxResult(Json r) {
  if (r['status'] == 'invalid-scenario') {
    return 'Расклад не подходит для задачи';
  }
  if (r['status'] != 'completed') return 'Ошибка прогона';
  if (r['passed'] == true) return 'Задача решена';
  if (r['passed'] == false) return 'Задача не решена';
  return 'Партия завершена';
}

String sideName(Object? side) => switch (side) {
      'Detectives' => 'детективы',
      'Killer' => 'чёрные',
      'Blackmailer' => 'шантажист',
      _ => '—'
    };

String sandboxAction(String value) =>
    const {
      'RoleReveal': 'Знакомство с ролями',
      'Night': 'Выбор истины',
      'FirstClue': 'Первая зацепка',
      'Mailbox': 'Письма',
      'GhostPick': 'Призрак выбирает подсказки',
      'Refill': 'Пополнение руки',
      'Discussion': 'Обсуждение',
      'Voting': 'Голосование',
      'VoteTie': 'Переголосование',
      'Hunt': 'Охота',
      'AwardNomination': 'Итоги',
      'Finished': 'Завершено',
      'AckRole': 'Посмотрел роль',
      'ChooseTruth': 'Загадал карты',
      'GiveFirstClue': 'Дал первую зацепку',
      'SendLetter': 'Отправил письмо',
      'RevealHints': 'Открыл подсказки',
      'Discard': 'Сбросил карту',
      'EndTurn': 'Закончил ход',
      'ReadyNextRound': 'Готов продолжать',
      'ReadyRevote': 'Готов переголосовать',
      'CastVote': 'Проголосовал',
      'HuntPick': 'Выбрал цель охоты',
      'TeamSuggest': 'Подсказал команде',
      'Motive': 'Мотив',
      'Place': 'Место',
      'Method': 'Способ',
      'Secret': 'Тайна',
      'Ghost': 'Призрак',
      'Killer': 'Убийца',
      'Accomplice': 'Сообщник',
      'Detective': 'Детектив',
      'Witness': 'Свидетель',
      'Expert': 'Эксперт',
    }[value] ??
    value;
String sandboxCommand(Map c, String Function(Object?) name) => [
      if (c['column'] != null) 'Карта ${(c['column'] as int) + 1}',
      if (c['columns'] is List)
        'Карты по рядам: ${(c['columns'] as List).map((n) => (n as int) + 1).join(', ')}',
      if (c['target'] != null) 'Цель: ${name(c['target'])}',
      if (c['suspect'] != null) 'Подозреваемый: ${name(c['suspect'])}',
      if (c['guess'] != null)
        'Предполагаемая роль: ${sandboxAction(c['guess'] as String)}',
    ].join(' · ');

class SandboxScreen extends ConsumerStatefulWidget {
  const SandboxScreen({super.key});
  @override
  ConsumerState<SandboxScreen> createState() => _SandboxScreenState();
}

class _SandboxScreenState extends ConsumerState<SandboxScreen> {
  final _seed = TextEditingController(text: '1000');
  String _scenario = 'full-game';
  bool _busy = false;
  String? _batchSummary;
  late Future<List<Json>> _runs;
  @override
  void initState() {
    super.initState();
    _runs = ref.read(apiProvider).sandboxRuns();
  }

  @override
  void dispose() {
    _seed.dispose();
    super.dispose();
  }

  void _reload() => setState(() {
        _runs = ref.read(apiProvider).sandboxRuns();
      });
  Future<void> _run({int count = 1}) async {
    final seed = int.tryParse(_seed.text);
    if (seed == null || seed < -2147483648 || seed > 2147483647 - count + 1) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text(
              'Введите целый номер расклада от −2147483648 до 2147483647.')));
      return;
    }
    setState(() => _busy = true);
    final completed = <Json>[];
    for (var i = 0; i < count; i++) {
      final result = await runAction(
          context, () => ref.read(apiProvider).runSandbox(_scenario, seed + i));
      if (!mounted) return;
      if (result == null) break;
      completed.add(result);
    }
    setState(() {
      _busy = false;
      if (count > 1) {
        final scored = completed
            .where((r) => r['scenario'] != 'full-game' && r['passed'] != null)
            .toList();
        final rows =
            completed.fold<int>(0, (n, r) => n + (r['totalRows'] as int));
        final correct =
            completed.fold<int>(0, (n, r) => n + (r['correctRows'] as int));
        _batchSummary = 'Серия: ${completed.length} из $count прогонов. '
            'Детективы победили: ${completed.where((r) => r['winner'] == 'Detectives').length}. '
            'Чёрные: ${completed.where((r) => r['winner'] == 'Killer').length}. '
            'Ошибки: ${completed.where((r) => r['status'] != 'completed' && r['status'] != 'invalid-scenario').length}. '
            'Ряды: $correct/$rows. '
            'Учебные задачи: ${scored.where((r) => r['passed'] == true).length}/${scored.length}.';
      }
    });
    _reload();
    if (count == 1 && completed.isNotEmpty) {
      _open(completed.single['id'] as String);
    }
  }

  void _open(String id) => Navigator.of(context).push(
      MaterialPageRoute<void>(builder: (_) => SandboxReplayScreen(id: id)));
  @override
  Widget build(BuildContext context) => Scaffold(
      appBar: AppBar(title: const Text('ПЕСОЧНИЦА БОТОВ'), actions: [
        IconButton(
            tooltip: 'Обновить прогоны',
            onPressed: _busy ? null : _reload,
            icon: const Icon(Icons.refresh))
      ]),
      body: ListView(padding: pageInsets(context, bottom: 32), children: [
        const Text(
            'Изолированные игры без рейтинга и изменения памяти или симпатий. В первой версии используются готовые характеры с фиксированными настройками. Сохранённую игру можно просмотреть по ходам.'),
        const SizedBox(height: 16),
        DropdownButtonFormField<String>(
            key: const Key('sandbox-scenario'),
            initialValue: _scenario,
            isExpanded: true,
            items: sandboxScenarios.entries
                .map(
                    (e) => DropdownMenuItem(value: e.key, child: Text(e.value)))
                .toList(),
            onChanged: _busy ? null : (v) => setState(() => _scenario = v!)),
        const SizedBox(height: 8),
        Text(_scenario == 'clear-hints'
            ? 'Учебный финал: кооператив, реальные карты, подсказки с выраженной связью. Успех — угадать все ряды.'
            : _scenario == 'witness-reveal'
                ? 'Учебная охота: дело уже раскрыто, Свидетель публично называет себя и Убийцу. Успех — найти Свидетеля.'
                : 'Полная партия шести ботов: от раздачи ролей до определения победителя, с обсуждениями.'),
        const SizedBox(height: 12),
        TextField(
            key: const Key('sandbox-seed'),
            controller: _seed,
            enabled: !_busy,
            keyboardType: const TextInputType.numberWithOptions(signed: true),
            decoration: const InputDecoration(
                labelText: 'Номер расклада',
                helperText:
                    'Одинаковый номер повторяет расклад при той же версии ботов и разметке.')),
        const SizedBox(height: 12),
        FilledButton(
            key: const Key('sandbox-run'),
            onPressed: _busy ? null : () => _run(),
            child: Text(_busy ? 'Боты играют…' : 'Запустить')),
        const SizedBox(height: 24),
        OutlinedButton(
            key: const Key('sandbox-batch'),
            onPressed: _busy ? null : () => _run(count: 5),
            child: const Text('Серия из 5 раскладов')),
        if (_batchSummary != null)
          Padding(
              padding: const EdgeInsets.symmetric(vertical: 12),
              child: Text(_batchSummary!)),
        Text('СОХРАНЁННЫЕ ПРОГОНЫ', style: sectionLabel()),
        FutureBuilder<List<Json>>(
            future: _runs,
            builder: (context, snapshot) {
              if (snapshot.hasError) {
                return ErrorRetry(
                    message: ApiError.from(snapshot.error!).message,
                    onRetry: _reload);
              }
              if (!snapshot.hasData) {
                return const Center(child: CircularProgressIndicator());
              }
              if (snapshot.data!.isEmpty) {
                return const Text('Пока нет прогонов.');
              }
              return Column(children: [
                for (final r in snapshot.data!)
                  ListTile(
                    key: Key('sandbox-run-${r['id']}'),
                    contentPadding: EdgeInsets.zero,
                    title: Text(
                        '${sandboxScenarios[r['scenario']]} · ${r['seed']}'),
                    subtitle: Text(
                        '${sandboxResult(r)} · ряды ${r['correctRows']}/${r['totalRows']} · победили: ${sideName(r['winner'])}'),
                    onTap: () => _open(r['id'] as String),
                    trailing: IconButton(
                        tooltip: 'Удалить прогон',
                        icon: const Icon(Icons.delete_outline),
                        onPressed: () async {
                          final confirmed = await showDialog<bool>(
                              context: context,
                              builder: (c) => AlertDialog(
                                      title: const Text(
                                          'Удалить сохранённый прогон?'),
                                      actions: [
                                        TextButton(
                                            onPressed: () =>
                                                Navigator.pop(c, false),
                                            child: const Text('Отмена')),
                                        TextButton(
                                            onPressed: () =>
                                                Navigator.pop(c, true),
                                            child: const Text('Удалить'))
                                      ]));
                          if (confirmed != true || !context.mounted) return;
                          await runAction(context, () async {
                            await ref
                                .read(apiProvider)
                                .deleteSandbox(r['id'] as String);
                            return true;
                          });
                          if (mounted) _reload();
                        }),
                  )
              ]);
            }),
      ]));
}

class SandboxReplayScreen extends ConsumerStatefulWidget {
  const SandboxReplayScreen({super.key, required this.id});
  final String id;
  @override
  ConsumerState<SandboxReplayScreen> createState() =>
      _SandboxReplayScreenState();
}

class _SandboxReplayScreenState extends ConsumerState<SandboxReplayScreen> {
  late Future<Json> _report;
  late Future<Json> _frame;
  int _index = 0;
  String _viewer = '';
  bool _reveal = false;
  @override
  void initState() {
    super.initState();
    _report = ref.read(apiProvider).sandboxReplay(widget.id);
    _frame = _load();
  }

  Future<Json> _load() => ref.read(apiProvider).sandboxStep(widget.id, _index,
      viewer: _viewer.isEmpty ? null : _viewer, reveal: _reveal);
  void _refresh() => setState(() {
        _frame = _load();
      });
  Widget _cards(List<String> ids,
          {int? truth, List<String> notes = const []}) =>
      Wrap(spacing: 6, runSpacing: 6, children: [
        for (var i = 0; i < ids.length; i++)
          InkWell(
              onTap: () => showCardZoom(context, ids[i],
                  caption: i < notes.length ? notes[i] : null),
              child: Container(
                  padding: const EdgeInsets.all(3),
                  decoration: BoxDecoration(
                      border: Border.all(
                          color: truth == i ? Colors.amber : Colors.transparent,
                          width: 2)),
                  child: Column(mainAxisSize: MainAxisSize.min, children: [
                    CardImage(cardId: ids[i], size: 72),
                    Text('${i + 1}', style: const TextStyle(fontSize: 11)),
                    if (i < notes.length)
                      SizedBox(
                          width: 72,
                          child: Text(notes[i],
                              textAlign: TextAlign.center,
                              style: const TextStyle(fontSize: 11))),
                  ])))
      ]);
  @override
  Widget build(BuildContext context) => Scaffold(
      appBar: AppBar(title: const Text('ПРОСМОТР ПРОГОНА')),
      body: FutureBuilder<Json>(
          future: _report,
          builder: (context, report) {
            if (report.hasError) {
              return ErrorRetry(
                  message: ApiError.from(report.error!).message,
                  onRetry: () => setState(() {
                        _report =
                            ref.read(apiProvider).sandboxReplay(widget.id);
                      }));
            }
            if (!report.hasData) {
              return const Center(child: CircularProgressIndicator());
            }
            final summary = report.data!['summary'] as Map;
            final seats = (report.data!['seats'] as List).cast<Map>();
            String name(Object? id) =>
                seats.where((s) => s['id'] == id).firstOrNull?['name']
                    as String? ??
                'Система';
            final total = summary['steps'] as int;
            return ListView(
                padding: pageInsets(context, bottom: 32),
                children: [
                  Text(
                      '${sandboxScenarios[summary['scenario']]} · ${sandboxResult(Map<String, dynamic>.from(summary))}',
                      style: heading(20)),
                  Text(
                      'Победили: ${sideName(summary['winner'])} · ряды ${summary['correctRows']}/${summary['totalRows']} · сообщений ${summary['messages']} · ${summary['elapsedMs']} мс'),
                  if (summary['error'] != null)
                    Text(summary['error'] as String,
                        style: const TextStyle(color: Colors.orangeAccent)),
                  const SizedBox(height: 16),
                  DropdownButtonFormField<String>(
                      key: const Key('sandbox-viewer'),
                      initialValue: _viewer,
                      isExpanded: true,
                      decoration: const InputDecoration(
                          labelText: 'Чьими глазами смотреть'),
                      items: [
                        const DropdownMenuItem(
                            value: '', child: Text('Общий стол')),
                        ...seats.map((s) => DropdownMenuItem(
                            value: s['id'] as String,
                            child: Text(s['name'] as String)))
                      ],
                      onChanged: (v) {
                        _viewer = v!;
                        _refresh();
                      }),
                  SwitchListTile(
                      key: const Key('sandbox-reveal'),
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Показать роли и истину для разбора'),
                      subtitle:
                          const Text('Эта информация не передаётся ботам.'),
                      value: _reveal,
                      onChanged: (v) {
                        _reveal = v;
                        _refresh();
                      }),
                  Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                    IconButton(
                        key: const Key('sandbox-prev'),
                        onPressed: _index > 0
                            ? () {
                                _index--;
                                _refresh();
                              }
                            : null,
                        icon: const Icon(Icons.chevron_left)),
                    Text('Шаг ${_index + 1} / $total'),
                    IconButton(
                        key: const Key('sandbox-next'),
                        onPressed: _index + 1 < total
                            ? () {
                                _index++;
                                _refresh();
                              }
                            : null,
                        icon: const Icon(Icons.chevron_right))
                  ]),
                  if (total > 1)
                    Slider(
                        value: _index.toDouble(),
                        min: 0,
                        max: (total - 1).toDouble(),
                        divisions: total - 1,
                        onChanged: (v) => setState(() => _index = v.round()),
                        onChangeEnd: (_) => _refresh()),
                  FutureBuilder<Json>(
                      future: _frame,
                      builder: (context, snapshot) {
                        if (snapshot.hasError) {
                          return ErrorRetry(
                              message: ApiError.from(snapshot.error!).message,
                              onRetry: _refresh);
                        }
                        if (!snapshot.hasData ||
                            snapshot.connectionState ==
                                ConnectionState.waiting) {
                          return const Center(
                              child: CircularProgressIndicator());
                        }
                        final f = snapshot.data!;
                        final view = GameView.fromJson(
                            Map<String, dynamic>.from(f['view'] as Map));
                        final truth =
                            (f['truth'] as List?)?.cast<int>() ?? view.truth;
                        final roles = f['roles'] as Map?;
                        final action = sandboxAction(f['action'] as String);
                        final observation = f['observation'] as Map?;
                        return Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Text('${name(f['actor'])}: $action',
                                  style: sectionLabel()),
                              Text(
                                  'Раунд ${view.round}/${view.totalRounds} · ${sandboxAction(view.phase)}'),
                              if (f['command'] != null) ...[
                                Text(sandboxCommand(f['command'] as Map, name)),
                                _cards([
                                  if ((f['command'] as Map)['cardId'] is String)
                                    (f['command'] as Map)['cardId'] as String,
                                  ...(((f['command'] as Map)['cardIds']
                                              as List?) ??
                                          [])
                                      .cast<String>()
                                ])
                              ],
                              if (roles != null)
                                Padding(
                                    padding:
                                        const EdgeInsets.symmetric(vertical: 8),
                                    child: Text(seats
                                        .map((s) =>
                                            '${s['name']}: ${sandboxAction(roles[s['id']] as String)}')
                                        .join('\n'))),
                              for (var r = 0; r < view.board.length; r++) ...[
                                const SizedBox(height: 12),
                                Text(sandboxAction(view.board[r].category)),
                                _cards(view.board[r].cards,
                                    truth: truth != null && r < truth.length
                                        ? truth[r]
                                        : null),
                              ],
                              const SizedBox(height: 16),
                              Text('ОТКРЫТЫЕ ПОДСКАЗКИ', style: sectionLabel()),
                              for (final h in view.hints) ...[
                                Text('Раунд ${h.round}'),
                                _cards(h.cards)
                              ],
                              if (view.me != null) ...[
                                const SizedBox(height: 16),
                                Text(
                                    'Своя роль: ${sandboxAction(view.me!.role)}'),
                                _cards(view.me!.hand)
                              ],
                              if (observation != null)
                                ExpansionTile(
                                    title: const Text(
                                        'Что знал автор перед этим ходом'),
                                    children: [
                                      Text(
                                          'Своя роль: ${sandboxAction((observation['me'] as Map?)?['role'] as String? ?? '—')}'),
                                      Text(
                                          'Известные роли: ${(observation['players'] as List).where((p) => p['knownRole'] != null).map((p) => '${name(p['id'])}: ${sandboxAction(p['knownRole'] as String)}').join(', ')}'),
                                      Text(
                                          'Истинные улики: ${(observation['truth'] as List?)?.map((c) => (c as int) + 1).join(', ') ?? 'неизвестны'}'),
                                      _cards(((observation['me']
                                                  as Map?)?['hand'] as List? ??
                                              [])
                                          .cast<String>()),
                                    ]),
                              const SizedBox(height: 20),
                              Text('ОБСУЖДЕНИЕ', style: sectionLabel()),
                              for (final m
                                  in (f['messages'] as List).cast<Map>())
                                Padding(
                                    padding: const EdgeInsets.only(top: 12),
                                    child: Panel(
                                        child: Column(
                                            crossAxisAlignment:
                                                CrossAxisAlignment.start,
                                            children: [
                                          Text(
                                              '${name(m['author'])} · раунд ${m['round']}',
                                              style: const TextStyle(
                                                  color: AppColors.muted)),
                                          Text(m['text'] as String),
                                          _cards((m['cards'] as List)
                                              .cast<String>()),
                                        ]))),
                            ]);
                      }),
                ]);
          }));
}
