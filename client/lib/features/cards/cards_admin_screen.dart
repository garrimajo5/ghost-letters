import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/api.dart';
import '../../core/card_catalog.dart';
import '../../core/theme.dart';
import '../../models/admin_cards.dart';
import '../../widgets/common.dart';

class CardsAdminScreen extends ConsumerStatefulWidget {
  const CardsAdminScreen({super.key});
  @override
  ConsumerState<CardsAdminScreen> createState() => _CardsAdminScreenState();
}

class _CardsAdminScreenState extends ConsumerState<CardsAdminScreen> {
  final _search = TextEditingController();
  String _set = '', _active = 'all';
  int _page = 0;
  late Future<AdminCardPage> _data;
  @override
  void initState() {
    super.initState();
    _data = _fetch();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<AdminCardPage> _fetch() => ref.read(apiProvider).adminCards(
      query: _search.text.trim(),
      setCode: _set,
      active: _active == 'all' ? null : _active == 'active',
      page: _page);
  void _reload({bool reset = false}) => setState(() {
        if (reset) _page = 0;
        _data = _fetch();
      });
  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('КАРТОЧКИ')),
        body: FutureBuilder<AdminCardPage>(
            future: _data,
            builder: (context, snapshot) {
              if (snapshot.hasError) {
                return ErrorRetry(
                    message: ApiError.from(snapshot.error!).message,
                    onRetry: _reload);
              }
              if (!snapshot.hasData) {
                return const Center(child: CircularProgressIndicator());
              }
              final data = snapshot.data!;
              return Padding(
                  padding: pageInsets(context),
                  child: Column(children: [
                    TextField(
                        key: const Key('cards-search'),
                        controller: _search,
                        maxLength: 80,
                        decoration: InputDecoration(
                            labelText: 'Название или номер карты',
                            counterText: '',
                            suffixIcon: IconButton(
                                icon: const Icon(Icons.search),
                                onPressed: () => _reload(reset: true))),
                        onSubmitted: (_) => _reload(reset: true)),
                    const SizedBox(height: 8),
                    Wrap(spacing: 12, runSpacing: 4, children: [
                      SizedBox(
                          width: 210,
                          child: DropdownButton<String>(
                              key: const Key('cards-set-filter'),
                              isExpanded: true,
                              value: _set,
                              items: [
                                const DropdownMenuItem(
                                    value: '', child: Text('Все наборы')),
                                ...data.sets.map((s) => DropdownMenuItem(
                                    value: s.code,
                                    child: Text(s.title,
                                        overflow: TextOverflow.ellipsis)))
                              ],
                              onChanged: (v) {
                                _set = v ?? '';
                                _reload(reset: true);
                              })),
                      SizedBox(
                          width: 210,
                          child: DropdownButton<String>(
                              key: const Key('cards-active-filter'),
                              isExpanded: true,
                              value: _active,
                              items: const [
                                DropdownMenuItem(
                                    value: 'all',
                                    child: Text('Любая активность')),
                                DropdownMenuItem(
                                    value: 'active', child: Text('Включённые')),
                                DropdownMenuItem(
                                    value: 'inactive',
                                    child: Text('Выключенные'))
                              ],
                              onChanged: (v) {
                                _active = v ?? 'all';
                                _reload(reset: true);
                              })),
                    ]),
                    Text('Найдено: ${data.total}',
                        style: const TextStyle(color: AppColors.muted)),
                    const SizedBox(height: 8),
                    Expanded(
                        child: data.cards.isEmpty
                            ? const Center(child: Text('Карты не найдены'))
                            : GridView.builder(
                                gridDelegate:
                                    const SliverGridDelegateWithMaxCrossAxisExtent(
                                        maxCrossAxisExtent: 220,
                                        mainAxisExtent: 280,
                                        crossAxisSpacing: 12,
                                        mainAxisSpacing: 12),
                                itemCount: data.cards.length,
                                itemBuilder: (context, i) {
                                  final card = data.cards[i];
                                  return InkWell(
                                      key: Key('admin-card-${card.id}'),
                                      onTap: () async {
                                        final saved =
                                            await Navigator.of(context)
                                                .push<bool>(MaterialPageRoute(
                                                    builder: (_) =>
                                                        CardEditorScreen(
                                                            card: card,
                                                            sets: data.sets)));
                                        if (saved == true && mounted) {
                                          ref.invalidate(cardCatalogProvider);
                                          _reload();
                                        }
                                      },
                                      child: Panel(
                                          padding: const EdgeInsets.all(8),
                                          child: Column(children: [
                                            LayoutBuilder(
                                                builder: (context,
                                                        constraints) =>
                                                    CardImage(
                                                        cardId: card.imageKey,
                                                        size: constraints
                                                            .maxWidth
                                                            .clamp(0, 144))),
                                            const SizedBox(height: 6),
                                            Text(card.title ?? card.imageKey,
                                                maxLines: 1,
                                                overflow:
                                                    TextOverflow.ellipsis),
                                            Text(card.imageKey,
                                                style: const TextStyle(
                                                    fontSize: 11,
                                                    color: AppColors.muted)),
                                            Text(
                                                card.isActive
                                                    ? 'Включена'
                                                    : 'Выключена',
                                                style: TextStyle(
                                                    color: card.isActive
                                                        ? Colors.greenAccent
                                                        : AppColors.muted)),
                                            Text(
                                                '${card.meanings.length} смыслов · ${card.details.length} деталей',
                                                style: const TextStyle(
                                                    fontSize: 11)),
                                          ])));
                                })),
                    Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                      IconButton(
                          tooltip: 'Предыдущая страница',
                          onPressed: _page > 0
                              ? () {
                                  _page--;
                                  _reload();
                                }
                              : null,
                          icon: const Icon(Icons.chevron_left)),
                      Text(
                          '${_page + 1} / ${(data.total / 40).ceil().clamp(1, 10001)}'),
                      IconButton(
                          tooltip: 'Следующая страница',
                          onPressed: (_page + 1) * 40 < data.total
                              ? () {
                                  _page++;
                                  _reload();
                                }
                              : null,
                          icon: const Icon(Icons.chevron_right)),
                    ]),
                  ]));
            }),
      );
}

class _FeatureDraft {
  _FeatureDraft(CardFeature feature)
      : tag = TextEditingController(text: feature.tag),
        label = TextEditingController(text: feature.label),
        weight = feature.weight;
  final key = UniqueKey();
  final TextEditingController tag, label;
  double weight;
  CardFeature get value => CardFeature(
      tag: tag.text.trim(), label: label.text.trim(), weight: weight);
  void dispose() {
    tag.dispose();
    label.dispose();
  }
}

class CardEditorScreen extends ConsumerStatefulWidget {
  const CardEditorScreen({super.key, required this.card, required this.sets});
  final AdminCard card;
  final List<AdminCardSet> sets;
  @override
  ConsumerState<CardEditorScreen> createState() => _CardEditorScreenState();
}

class _CardEditorScreenState extends ConsumerState<CardEditorScreen> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _title, _tags;
  late final List<_FeatureDraft> _meanings, _details;
  late String _set;
  late bool _active;
  bool _busy = false;
  final List<_FeatureDraft> _removed = [];
  @override
  void initState() {
    super.initState();
    final c = widget.card;
    _title = TextEditingController(text: c.title ?? '');
    _tags = TextEditingController(text: c.tags.join(', '));
    _meanings = c.meanings.map(_FeatureDraft.new).toList();
    _details = c.details.map(_FeatureDraft.new).toList();
    _set = c.setCode;
    _active = c.isActive;
  }

  @override
  void dispose() {
    _title.dispose();
    _tags.dispose();
    for (final f in [..._meanings, ..._details, ..._removed]) {
      f.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;
    setState(() => _busy = true);
    final c = widget.card;
    final saved = await runAction(
        context,
        () => ref.read(apiProvider).saveCard(AdminCard(
            id: c.id,
            imageKey: c.imageKey,
            title: _title.text.trim(),
            setCode: _set,
            isActive: _active,
            version: c.version,
            tags: _tags.text
                .split(',')
                .map((t) => t.trim())
                .where((t) => t.isNotEmpty)
                .toList(),
            meanings: _meanings.map((m) => m.value).toList(),
            details: _details.map((d) => d.value).toList())));
    if (!mounted) return;
    if (saved != null) {
      Navigator.pop(context, true);
    } else {
      setState(() => _busy = false);
    }
  }

  Widget _features(String title, String id, List<_FeatureDraft> values) =>
      Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text(title, style: heading(20)),
        for (final f in values)
          Padding(
              key: f.key,
              padding: const EdgeInsets.only(top: 12),
              child: Panel(
                  padding: const EdgeInsets.all(12),
                  child: Column(children: [
                    TextFormField(
                        controller: f.label,
                        maxLength: 80,
                        decoration: const InputDecoration(
                            labelText: 'Подпись', counterText: ''),
                        validator: (v) => v == null || v.trim().isEmpty
                            ? 'Добавьте подпись'
                            : null),
                    const SizedBox(height: 6),
                    Row(children: [
                      Expanded(
                          child: TextFormField(
                              controller: f.tag,
                              maxLength: 64,
                              decoration: const InputDecoration(
                                  labelText: 'Ключ связи, например weapon',
                                  counterText: ''),
                              validator: (v) => v == null || v.trim().isEmpty
                                  ? 'Добавьте ключ'
                                  : null)),
                      IconButton(
                          tooltip: 'Удалить признак',
                          onPressed: _busy
                              ? null
                              : () => setState(() {
                                    values.remove(f);
                                    _removed.add(f);
                                  }),
                          icon: const Icon(Icons.delete_outline)),
                    ]),
                    Row(children: [
                      Text('Вес ${f.weight.toStringAsFixed(2)}'),
                      Expanded(
                          child: Slider(
                              value: f.weight,
                              min: 0.01,
                              max: 1,
                              divisions: 99,
                              onChanged: _busy
                                  ? null
                                  : (v) => setState(() => f.weight = v)))
                    ]),
                  ]))),
        TextButton.icon(
            key: Key('add-$id'),
            onPressed: _busy || values.length >= 60
                ? null
                : () => setState(() => values.add(_FeatureDraft(
                    const CardFeature(tag: '', label: '', weight: 1)))),
            icon: const Icon(Icons.add),
            label: Text('Добавить: ${title.toLowerCase()}')),
      ]);

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(widget.card.imageKey)),
        body: Form(
            key: _form,
            child:
                ListView(padding: pageInsets(context, bottom: 32), children: [
              Center(child: CardImage(cardId: widget.card.imageKey, size: 240)),
              const SizedBox(height: 16),
              TextFormField(
                  key: const Key('card-title'),
                  controller: _title,
                  maxLength: 64,
                  decoration:
                      const InputDecoration(labelText: 'Название карты')),
              SwitchListTile(
                  key: const Key('card-active'),
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Участвует в новых партиях'),
                  value: _active,
                  onChanged: _busy ? null : (v) => setState(() => _active = v)),
              DropdownButtonFormField<String>(
                  key: const Key('card-set'),
                  initialValue: _set,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Набор'),
                  items: widget.sets
                      .map((s) => DropdownMenuItem(
                          value: s.code,
                          child:
                              Text(s.title, overflow: TextOverflow.ellipsis)))
                      .toList(),
                  onChanged: _busy ? null : (v) => setState(() => _set = v!)),
              const SizedBox(height: 16),
              TextFormField(
                  key: const Key('card-tags'),
                  controller: _tags,
                  minLines: 1,
                  maxLines: 4,
                  decoration: const InputDecoration(
                      labelText: 'Цвет и форма — через запятую',
                      helperText: 'Например: red, blue, shape-round')),
              const SizedBox(height: 20),
              const Text(
                  'Вес 1 — основной смысл. Меньший вес — второстепенный: насколько бот замечает его, задаётся в характере бота. Одинаковые ключи связывают карты.',
                  style: TextStyle(color: AppColors.muted)),
              const SizedBox(height: 12),
              _features('Смыслы', 'meaning', _meanings),
              const SizedBox(height: 16),
              _features('Мелкие детали', 'detail', _details),
              const SizedBox(height: 20),
              FilledButton(
                  key: const Key('card-save'),
                  onPressed: _busy ? null : _save,
                  child: Text(_busy ? 'Сохраняю…' : 'Сохранить карту')),
            ])),
      );
}
