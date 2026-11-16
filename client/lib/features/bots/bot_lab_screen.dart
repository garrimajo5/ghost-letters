import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/api.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'sandbox_screen.dart' show sandboxAction;

/// Лаборатория мыслей ботов: раздача (случайная или своя), подсказки по раундам — и что думает каждый характер.
/// Без партии, речи и озвучки: после каждой правки сервер заново читает стол теми же функциями, что за столом.
class BotLabScreen extends ConsumerStatefulWidget {
  const BotLabScreen({super.key});
  @override
  ConsumerState<BotLabScreen> createState() => _BotLabScreenState();
}

class _BotLabScreenState extends ConsumerState<BotLabScreen> {
  final _seed = TextEditingController();
  int _players = 6;
  Map<String, String> _names = {};
  List<String> _categories = [];
  List<List<String>> _rows = [];
  List<int?> _truth = [];
  List<List<String>> _hints = [];
  List<String> _pool = [];
  Json? _result;
  bool _busy = false;
  String? _error;
  int _thinking = 0;

  @override
  void initState() {
    super.initState();
    ref.read(apiProvider).labNames().then((n) {
      if (mounted) setState(() => _names = {...n, ..._names});
    }).catchError((Object _) {});
  }

  @override
  void dispose() {
    _seed.dispose();
    super.dispose();
  }

  String _name(String id) => _names[id] ?? id.replaceFirst('orig_', '#');
  Set<String> get _onBoard => {for (final r in _rows) ...r};
  Set<String> get _used => {for (final h in _hints) ...h};

  Future<void> _deal() async {
    final seed = _seed.text.trim().isEmpty ? null : int.tryParse(_seed.text.trim());
    setState(() => _busy = true);
    final deal = await runAction(context, () => ref.read(apiProvider).labDeal(_players, seed: seed));
    if (!mounted) return;
    setState(() {
      _busy = false;
      if (deal == null) return;
      _seed.text = '${deal['seed']}';
      final board = (deal['board'] as List).cast<Map>();
      _categories = [for (final r in board) r['category'] as String];
      _rows = [for (final r in board) (r['cards'] as List).cast<String>().toList()];
      _truth = [for (final t in deal['truth'] as List) t as int];
      _pool = (deal['pool'] as List).cast<String>();
      _hints = List.generate(deal['rounds'] as int, (_) => <String>[]);
      _names = {..._names, ...(deal['names'] as Map).cast<String, String>()};
      _result = null;
    });
    _think();
  }

  Json _request() => {
        'board': [
          for (var i = 0; i < _rows.length; i++) {'category': _categories[i], 'cards': _rows[i]}
        ],
        'hints': [
          for (var i = 0; i < _hints.length; i++)
            if (_hints[i].isNotEmpty) {'round': i + 1, 'cards': _hints[i]}
        ],
        if (_truth.every((t) => t != null)) 'truth': _truth,
        'rounds': _hints.length,
      };

  /// Перечитать стол после правки. Ответ на устаревший запрос не показываем.
  Future<void> _think() async {
    if (_rows.isEmpty) return;
    final ticket = ++_thinking;
    setState(() => _error = null);
    try {
      final r = await ref.read(apiProvider).labThink(_request());
      if (!mounted || ticket != _thinking) return;
      setState(() {
        _result = r;
        _names = {..._names, ...((r['names'] as Map?) ?? {}).cast<String, String>()};
      });
    } catch (e) {
      if (mounted && ticket == _thinking) setState(() => _error = ApiError.from(e).message);
    }
  }

  Future<String?> _pick({required String title, bool preferPool = false, Set<String> exclude = const {}}) =>
      showDialog<String>(
          context: context,
          builder: (_) => _CardPicker(
              title: title,
              names: _names,
              pool: preferPool ? _pool : const [],
              exclude: exclude,
              name: _name));

  Future<void> _boardCard(int row, int col) async {
    final card = _rows[row][col];
    final action = await showModalBottomSheet<String>(
        context: context,
        builder: (c) => SafeArea(
                child: Column(mainAxisSize: MainAxisSize.min, children: [
              ListTile(title: Text(_name(card)), subtitle: Text('${sandboxAction(_categories[row])}, карта ${col + 1}')),
              ListTile(
                  key: const Key('lab-mark-truth'),
                  leading: const Icon(Icons.star_outline),
                  title: const Text('Это истина ряда'),
                  onTap: () => Navigator.pop(c, 'truth')),
              ListTile(
                  key: const Key('lab-replace'),
                  leading: const Icon(Icons.swap_horiz),
                  title: const Text('Заменить карту'),
                  onTap: () => Navigator.pop(c, 'replace')),
              ListTile(
                  leading: const Icon(Icons.zoom_in),
                  title: const Text('Увеличить'),
                  onTap: () => Navigator.pop(c, 'zoom')),
            ])));
    if (!mounted || action == null) return;
    if (action == 'zoom') return showCardZoom(context, card, caption: _name(card));
    if (action == 'truth') {
      setState(() => _truth[row] = col);
    } else {
      final next = await _pick(title: 'Заменить «${_name(card)}»', exclude: {..._onBoard, ..._used});
      if (next == null || !mounted) return;
      setState(() => _rows[row][col] = next);
    }
    _think();
  }

  Future<void> _addHint(int round) async {
    final card = await _pick(title: 'Подсказка, раунд ${round + 1}', preferPool: true, exclude: {..._onBoard, ..._used});
    if (card == null || !mounted) return;
    setState(() => _hints[round].add(card));
    _think();
  }

  Future<void> _suggest(int round) async {
    if (_truth.any((t) => t == null)) return;
    final candidates = _pool.where((c) => !_used.contains(c) && !_onBoard.contains(c)).toList();
    final request = {
      ..._request(),
      'hints': [
        for (var i = 0; i < round; i++)
          if (_hints[i].isNotEmpty) {'round': i + 1, 'cards': _hints[i]}
      ],
      'candidates': candidates,
      'round': round + 1,
    };
    final list = await runAction(context, () => ref.read(apiProvider).labSuggest(request));
    if (list == null || !mounted) return;
    final chosen = {for (final s in list) if (s['pick'] == true) s['card'] as String};
    final ok = await showDialog<bool>(
        context: context,
        builder: (c) => StatefulBuilder(
            builder: (c, set) => AlertDialog(
                    title: Text('Призрак-бот, раунд ${round + 1}'),
                    content: SizedBox(
                        width: 420,
                        child: ListView(shrinkWrap: true, children: [
                          const Text('Призрак-бот знает истину и выбирает из писем раздачи. Отмеченные — что открыл бы он.',
                              style: TextStyle(color: AppColors.muted, fontSize: 13)),
                          for (final s in list)
                            CheckboxListTile(
                                key: Key('lab-suggest-${s['card']}'),
                                contentPadding: EdgeInsets.zero,
                                value: chosen.contains(s['card']),
                                onChanged: (v) => set(() => v == true ? chosen.add(s['card'] as String) : chosen.remove(s['card'])),
                                secondary: CardImage(cardId: s['card'] as String, size: 44),
                                title: Text(_name(s['card'] as String)),
                                subtitle: Text(
                                    'к истине ${_pct(s['trueMatch'])} · к ложной ${_pct(s['falseMatch'])} · ${s['reason']}',
                                    style: const TextStyle(fontSize: 12))),
                        ])),
                    actions: [
                      TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Отмена')),
                      FilledButton(
                          key: const Key('lab-suggest-apply'),
                          onPressed: () => Navigator.pop(c, true),
                          child: const Text('Открыть отмеченные')),
                    ])));
    if (ok != true || !mounted) return;
    setState(() => _hints[round].addAll(chosen.where((c) => !_hints[round].contains(c))));
    _think();
  }

  static String _pct(Object? v) => '${(((v as num?) ?? 0) * 100).round()} %';

  @override
  Widget build(BuildContext context) => Scaffold(
      appBar: AppBar(title: const Text('ЛАБОРАТОРИЯ МЫСЛЕЙ')),
      body: ListView(padding: pageInsets(context, max: 1100, bottom: 32), children: [
        const Text(
            'Раздай поле или собери своё, открывай подсказки по раундам — и смотри, во что верит каждый характер. '
            'Без партии и озвучки; мнение пересчитывается после каждой правки.',
            style: TextStyle(color: AppColors.muted)),
        const SizedBox(height: 12),
        Wrap(spacing: 12, runSpacing: 8, crossAxisAlignment: WrapCrossAlignment.center, children: [
          DropdownButton<int>(
              key: const Key('lab-players'),
              value: _players,
              items: [for (var n = 4; n <= 11; n++) DropdownMenuItem(value: n, child: Text('$n игроков'))],
              onChanged: _busy ? null : (v) => setState(() => _players = v!)),
          SizedBox(
              width: 150,
              child: TextField(
                  key: const Key('lab-seed'),
                  controller: _seed,
                  keyboardType: const TextInputType.numberWithOptions(signed: true),
                  decoration: const InputDecoration(labelText: 'Номер раздачи', hintText: 'случайный'))),
          FilledButton.icon(
              key: const Key('lab-deal'),
              onPressed: _busy ? null : _deal,
              icon: const Icon(Icons.casino_outlined),
              label: Text(_busy ? 'Раздаю…' : 'Раздать (Озон)')),
        ]),
        if (_rows.isNotEmpty) ...[
          const SizedBox(height: 16),
          Text('ПОЛЕ · нажми карту: истина или замена', style: sectionLabel()),
          const SizedBox(height: 6),
          for (var r = 0; r < _rows.length; r++) _boardRow(r),
          const SizedBox(height: 12),
          Row(children: [
            Expanded(child: Text('ПОДСКАЗКИ ПРИЗРАКА', style: sectionLabel())),
            TextButton.icon(
                key: const Key('lab-add-round'),
                onPressed: _hints.length >= 6 ? null : () => setState(() => _hints.add([])),
                icon: const Icon(Icons.add),
                label: const Text('Раунд')),
          ]),
          for (var i = 0; i < _hints.length; i++) _hintRound(i),
          const SizedBox(height: 16),
          if (_error != null) Text(_error!, style: const TextStyle(color: AppColors.redSoft)),
          if (_result != null) ..._thoughts(),
        ],
      ]));

  Widget _boardRow(int r) => Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(width: 64, child: Text(sandboxAction(_categories[r]), style: const TextStyle(fontSize: 12, color: AppColors.muted))),
        Expanded(
            child: Wrap(spacing: 6, runSpacing: 6, children: [
          for (var c = 0; c < _rows[r].length; c++)
            InkWell(
                key: Key('lab-board-$r-$c'),
                onTap: () => _boardCard(r, c),
                child: Container(
                    padding: const EdgeInsets.all(2),
                    decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(10),
                        border: Border.all(color: _truth[r] == c ? AppColors.amber : Colors.transparent, width: 2)),
                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                      CardImage(cardId: _rows[r][c], size: 60),
                      SizedBox(
                          width: 60,
                          child: Text(_name(_rows[r][c]),
                              maxLines: 1, overflow: TextOverflow.ellipsis, textAlign: TextAlign.center, style: const TextStyle(fontSize: 10))),
                    ]))),
        ])),
      ]));

  Widget _hintRound(int i) => Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(crossAxisAlignment: CrossAxisAlignment.center, children: [
        SizedBox(width: 64, child: Text('Раунд ${i + 1}', style: const TextStyle(fontSize: 12, color: AppColors.muted))),
        Expanded(
            child: Wrap(spacing: 6, runSpacing: 6, crossAxisAlignment: WrapCrossAlignment.center, children: [
          for (final h in _hints[i])
            Tooltip(
                message: '${_name(h)} — нажми, чтобы убрать',
                child: InkWell(
                    key: Key('lab-hint-$h'),
                    onTap: () {
                      setState(() => _hints[i].remove(h));
                      _think();
                    },
                    onLongPress: () => showCardZoom(context, h, caption: _name(h)),
                    child: CardImage(cardId: h, size: 48))),
          IconButton.outlined(
              key: Key('lab-add-hint-$i'), tooltip: 'Добавить подсказку', onPressed: () => _addHint(i), icon: const Icon(Icons.add)),
          if (_pool.isNotEmpty && _truth.every((t) => t != null))
            TextButton.icon(
                key: Key('lab-suggest-round-$i'),
                onPressed: () => _suggest(i),
                icon: const Icon(Icons.auto_awesome_outlined, size: 18),
                label: const Text('Как Призрак-бот')),
        ])),
      ]));

  List<Widget> _thoughts() {
    final bots = ((_result!['bots'] as List?) ?? const []).cast<Map>();
    if (bots.isEmpty) return const [Text('Нет мнений.')];
    return [
      Text('ГОЛОСА ПО РЯДАМ', style: sectionLabel()),
      const SizedBox(height: 6),
      SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: DataTable(
              key: const Key('lab-votes'),
              columnSpacing: 12,
              headingRowHeight: 32,
              dataRowMinHeight: 56,
              dataRowMaxHeight: 60,
              columns: [
                const DataColumn(label: Text('Бот')),
                for (final c in _categories) DataColumn(label: Text(sandboxAction(c))),
                if (_truth.every((t) => t != null)) const DataColumn(label: Text('Угадал')),
              ],
              rows: [
                for (final b in bots)
                  DataRow(cells: [
                    DataCell(Text(b['bot'] as String)),
                    for (final row in (b['rows'] as List).cast<Map>())
                      DataCell(Row(mainAxisSize: MainAxisSize.min, children: [
                        CardImage(cardId: ((row['cards'] as List)[row['vote'] as int] as Map)['card'] as String, size: 40),
                        if (row['correct'] != null)
                          Icon(row['correct'] == true ? Icons.check : Icons.close,
                              size: 16, color: row['correct'] == true ? AppColors.believed : AppColors.redSoft),
                      ])),
                    if (_truth.every((t) => t != null)) DataCell(Text('${b['correct']}/${_rows.length}')),
                  ])
              ])),
      const SizedBox(height: 12),
      Text('ЧТО ДУМАЕТ КАЖДЫЙ · нажми карту — почему', style: sectionLabel()),
      for (final b in bots) _bot(b),
    ];
  }

  Widget _bot(Map b) {
    final unexplained = ((b['unexplained'] as List?) ?? const []).cast<String>();
    return ExpansionTile(
        key: Key('lab-bot-${b['bot']}'),
        tilePadding: EdgeInsets.zero,
        title: Text('${b['bot']}${b['correct'] == null ? '' : ' · угадал ${b['correct']}/${_rows.length}'}'),
        subtitle: Text(b['about'] as String? ?? '', style: const TextStyle(fontSize: 12, color: AppColors.muted)),
        children: [
          for (final row in (b['rows'] as List).cast<Map>()) _thoughtRow(row),
          if (unexplained.isNotEmpty)
            Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Text('Не объяснил ни один ряд: ${unexplained.map(_name).join(', ')}',
                    style: const TextStyle(fontSize: 12, color: AppColors.muted))),
        ]);
  }

  Widget _thoughtRow(Map row) {
    final cards = (row['cards'] as List).cast<Map>();
    final r = _categories.indexOf(row['category'] as String);
    return Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(width: 64, child: Text(sandboxAction(row['category'] as String), style: const TextStyle(fontSize: 12, color: AppColors.muted))),
          Expanded(
              child: Wrap(spacing: 8, runSpacing: 8, children: [
            for (var i = 0; i < cards.length; i++)
              InkWell(
                  onTap: () => _why(cards[i]),
                  child: SizedBox(
                      width: 64,
                      child: Column(children: [
                        Container(
                            padding: const EdgeInsets.all(2),
                            decoration: BoxDecoration(
                                borderRadius: BorderRadius.circular(10),
                                border: Border.all(
                                    color: row['vote'] == i
                                        ? AppColors.ice
                                        : r >= 0 && _truth[r] == i
                                            ? AppColors.amber.withValues(alpha: .6)
                                            : Colors.transparent,
                                    width: 2)),
                            child: CardImage(cardId: cards[i]['card'] as String, size: 56)),
                        const SizedBox(height: 3),
                        ClipRRect(
                            borderRadius: BorderRadius.circular(3),
                            child: LinearProgressIndicator(
                                value: ((cards[i]['belief'] as num?) ?? 0).toDouble().clamp(0, 1),
                                minHeight: 5,
                                backgroundColor: AppColors.surface2,
                                color: row['vote'] == i ? AppColors.ice : AppColors.dim)),
                        Text(_pct(cards[i]['belief']), style: const TextStyle(fontSize: 11)),
                      ]))),
          ])),
        ]));
  }

  void _why(Map card) {
    final links = ((card['links'] as List?) ?? const []).cast<Map>();
    showDialog<void>(
        context: context,
        builder: (c) => AlertDialog(
                title: Text(_name(card['card'] as String)),
                content: SizedBox(
                    width: 420,
                    child: ListView(shrinkWrap: true, children: [
                      Text(
                          'Верит: ${_pct(card['belief'])} · по ряду отдельно: ${_pct(card['rowwise'])}'
                          '${card['theory'] == null ? '' : ' · общая теория: ${_pct(card['theory'])}'}',
                          style: const TextStyle(fontSize: 13)),
                      const SizedBox(height: 8),
                      if (links.isEmpty) const Text('Ни одна подсказка с этой картой не связана.', style: TextStyle(color: AppColors.muted)),
                      for (final l in links)
                        ListTile(
                            contentPadding: EdgeInsets.zero,
                            leading: CardImage(cardId: l['hint'] as String, size: 40),
                            title: Text('${_name(l['hint'] as String)} · сходство ${_pct(l['similarity'])}'),
                            subtitle: Text(l['why'] as String? ?? '', style: const TextStyle(fontSize: 12))),
                    ])),
                actions: [TextButton(onPressed: () => Navigator.pop(c), child: const Text('Закрыть'))]));
  }
}

/// Выбор карты по названию: сначала письма раздачи (если есть), можно искать по всей колоде.
class _CardPicker extends StatefulWidget {
  const _CardPicker({required this.title, required this.names, required this.pool, required this.exclude, required this.name});
  final String title;
  final Map<String, String> names;
  final List<String> pool;
  final Set<String> exclude;
  final String Function(String) name;
  @override
  State<_CardPicker> createState() => _CardPickerState();
}

class _CardPickerState extends State<_CardPicker> {
  String _query = '';
  late bool _poolOnly = widget.pool.isNotEmpty;

  @override
  Widget build(BuildContext context) {
    final source = _poolOnly ? widget.pool : widget.names.keys.toList();
    final q = _query.trim().toLowerCase();
    final cards = source
        .where((c) => !widget.exclude.contains(c))
        .where((c) => q.isEmpty || widget.name(c).toLowerCase().contains(q) || c.contains(q))
        .take(240)
        .toList();
    return AlertDialog(
        title: Text(widget.title),
        content: SizedBox(
            width: 560,
            height: 480,
            child: Column(children: [
              TextField(
                  key: const Key('lab-picker-search'),
                  autofocus: true,
                  decoration: const InputDecoration(prefixIcon: Icon(Icons.search), hintText: 'Название карты'),
                  onChanged: (v) => setState(() => _query = v)),
              if (widget.pool.isNotEmpty)
                SwitchListTile(
                    key: const Key('lab-picker-pool'),
                    contentPadding: EdgeInsets.zero,
                    dense: true,
                    title: const Text('Только письма раздачи (руки игроков)'),
                    value: _poolOnly,
                    onChanged: (v) => setState(() => _poolOnly = v)),
              const SizedBox(height: 6),
              Expanded(
                  child: cards.isEmpty
                      ? const Center(child: Text('Ничего не нашлось.'))
                      : GridView.extent(maxCrossAxisExtent: 84, mainAxisSpacing: 6, crossAxisSpacing: 6, childAspectRatio: .78, children: [
                          for (final c in cards)
                            InkWell(
                                key: Key('lab-pick-$c'),
                                onTap: () => Navigator.pop(context, c),
                                child: Column(children: [
                                  CardImage(cardId: c, size: 64),
                                  Text(widget.name(c), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 10)),
                                ])),
                        ])),
            ])),
        actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Отмена'))]);
  }
}
