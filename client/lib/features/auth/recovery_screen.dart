import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/card_catalog.dart';
import '../../core/session.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

// The login catalog does not require authentication and includes disabled gameplay cards.
final recoveryCardCatalogProvider = FutureProvider<List<CardSetInfo>>((ref) async =>
  parseCardCatalog(await rootBundle.loadString('assets/cards/cards.json')));

/// Keys stay only in this screen's memory, never in preferences or chat.
class RecoveryScreen extends ConsumerStatefulWidget {
  const RecoveryScreen({super.key, this.edit = false, this.nickname, this.color});
  final bool edit;
  final String? nickname;
  final String? color;

  @override
  ConsumerState<RecoveryScreen> createState() => _RecoveryScreenState();
}

class _RecoveryScreenState extends ConsumerState<RecoveryScreen> {
  final _login = TextEditingController();
  final _key = GlobalKey<_SecretEditorState>();
  final _old = GlobalKey<_SecretEditorState>();
  bool _busy = false;
  bool _loading = false;
  String? _loadError;
  String? _oldKind;
  bool get _creating => widget.nickname != null;
  bool get _setting => widget.edit || _creating;

  @override
  void initState() {
    super.initState();
    if (widget.edit) _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _loadError = null; });
    try {
      final info = await ref.read(apiProvider).get('/me/recovery') as Map;
      if (!mounted) return;
      _login.text = info['login'] as String? ?? '';
      setState(() => _oldKind = info['kind'] as String?);
    } catch (e) {
      if (mounted) setState(() => _loadError = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  void dispose() { _login.dispose(); super.dispose(); }

  Future<void> _submit() async {
    if (_busy) return;
    final login = _login.text.trim().toLowerCase();
    if (!RegExp(r'^[a-z0-9_-]{3,32}$').hasMatch(login)) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Логин: 3–32 латинские буквы, цифры, дефис или подчёркивание')));
      return;
    }
    final key = _key.currentState?.validated();
    final old = _oldKind == null ? null : _old.currentState?.validated();
    if (key == null || (_oldKind != null && old == null)) return;
    setState(() => _busy = true);
    final api = ref.read(apiProvider);
    final session = ref.read(sessionProvider.notifier);
    final payload = {'login': login, 'key': key, if (old != null) 'currentKey': old};
    final result = await runAction(context, () async {
      final dynamic json;
      if (_creating) {
        json = await api.post('/auth/guest', {
          'deviceId': session.deviceId, 'nickname': widget.nickname,
          'avatarColor': widget.color, 'recovery': payload,
        });
      } else if (widget.edit) {
        json = await api.put('/me/recovery', payload);
      } else {
        json = await api.post('/auth/key-login', payload);
      }
      return AuthTokens.fromJson(Map<String, dynamic>.from(json as Map));
    });
    if (!mounted) return;
    setState(() => _busy = false);
    if (result != null) {
      // Pop before notifying the router that login state changed.
      Navigator.pop(context);
      session.signIn(result);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(widget.edit ? 'Ключ входа' : _creating ? 'Защитите аккаунт' : 'Войти по ключу')),
    body: _loading ? const Center(child: CircularProgressIndicator()) : _loadError != null
      ? ErrorRetry(message: _loadError!, onRetry: _load)
      : Center(child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 560), child: ListView(
        padding: const EdgeInsets.all(20), children: [
          Text(_setting
            ? 'Запомните логин и ключ: с ними можно войти, даже если вышли на всех устройствах. Логин не зависит от имени в игре.'
            : 'Введите логин и слово или выберите свои три карты в том же порядке.'),
          const SizedBox(height: 16),
          TextField(key: const Key('recovery-login'), controller: _login, enabled: !_busy,
            autocorrect: false, enableSuggestions: false, maxLength: 32,
            decoration: const InputDecoration(labelText: 'Логин', hintText: 'Например, watson_7')),
          if (_oldKind != null) ...[
            const Text('Прежний ключ'),
            SecretEditor(key: _old, initialKind: _oldKind!, enabled: !_busy),
            const Divider(height: 32),
          ],
          Text(_setting ? 'Новый ключ' : 'Ключ'),
          SecretEditor(key: _key, confirm: _setting, enabled: !_busy),
          const SizedBox(height: 20),
          if (widget.edit) const Padding(padding: EdgeInsets.only(bottom: 12), child: Text(
            'При смене ключа другие устройства попросят войти снова после окончания текущей сессии.')),
          FilledButton(key: const Key('recovery-submit'), onPressed: _busy ? null : _submit,
            child: Text(_busy ? 'Подождите…' : _setting ? 'Сохранить и продолжить' : 'Войти')),
        ],
      ))),
  );
}

class SecretEditor extends ConsumerStatefulWidget {
  const SecretEditor({super.key, this.initialKind = 'word', this.confirm = false, this.enabled = true});
  final String initialKind;
  final bool confirm;
  final bool enabled;
  @override
  ConsumerState<SecretEditor> createState() => _SecretEditorState();
}

class _SecretEditorState extends ConsumerState<SecretEditor> {
  final _word = TextEditingController();
  final _repeat = TextEditingController();
  late String _kind = widget.initialKind;
  final List<String> _cards = [];
  bool _remember = false;
  String? _error;

  @override
  void dispose() { _word.dispose(); _repeat.dispose(); super.dispose(); }

  Map<String, dynamic>? validated() {
    final error = _kind == 'word'
      ? _word.text.trim().isEmpty || _word.text.length < 5 || _word.text.length > 128
        ? 'Введите слово или фразу: 5–128 символов'
        : widget.confirm && _word.text != _repeat.text ? 'Слова не совпадают' : null
      : _cards.length != 3 ? 'Выберите три разные карты'
        : widget.confirm && !_remember ? 'Подтвердите, что запомнили порядок карт' : null;
    setState(() => _error = error);
    return error != null ? null : {'kind': _kind, if (_kind == 'word') 'word': _word.text else 'cards': List.of(_cards)};
  }

  Future<void> _pick() async {
    final sets = await runAction(context, () => ref.read(recoveryCardCatalogProvider.future));
    if (!mounted || sets == null || sets.isEmpty) return;
    final selected = await showDialog<List<String>>(context: context, builder: (_) => _CardKeyPicker(sets: sets, selected: _cards));
    if (selected != null && mounted) setState(() { _cards..clear()..addAll(selected); _remember = false; });
  }

  @override
  Widget build(BuildContext context) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Wrap(spacing: 8, children: [
      for (final kind in ['word', 'cards']) ChoiceChip(label: Text(kind == 'word' ? 'Слово' : 'Три карты'),
        selected: _kind == kind, onSelected: widget.enabled ? (_) => setState(() { _kind = kind; _error = null; }) : null),
    ]),
    const SizedBox(height: 8),
    if (_kind == 'word') ...[
      TextField(key: const Key('recovery-word'), controller: _word, obscureText: true,
        autocorrect: false, enableSuggestions: false, enabled: widget.enabled, maxLength: 128,
        decoration: const InputDecoration(labelText: 'Секретное слово или фраза', helperText: 'Регистр и пробелы имеют значение. Лучше длинная фраза.')),
      if (widget.confirm) TextField(key: const Key('recovery-repeat'), controller: _repeat, obscureText: true,
        autocorrect: false, enableSuggestions: false, enabled: widget.enabled, maxLength: 128,
        decoration: const InputDecoration(labelText: 'Повторите ключ')),
    ] else ...[
      const Text('Важен порядок: первая → вторая → третья. Карты ключа не связаны с вашей рукой в игре.'),
      Wrap(spacing: 8, children: [for (var i = 0; i < _cards.length; i++) Column(children: [
        CardImage(cardId: _cards[i], size: 80), Text('${i + 1}'),
      ])]),
      OutlinedButton(onPressed: widget.enabled ? _pick : null, child: const Text('Выбрать карты')),
      if (widget.confirm) CheckboxListTile(contentPadding: EdgeInsets.zero, value: _remember,
        onChanged: widget.enabled ? (value) => setState(() => _remember = value ?? false) : null,
        title: const Text('Я запомнил три карты и их порядок')),
    ],
    if (_error != null) Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
  ]);
}

class _CardKeyPicker extends StatefulWidget {
  const _CardKeyPicker({required this.sets, required this.selected});
  final List<CardSetInfo> sets;
  final List<String> selected;
  @override
  State<_CardKeyPicker> createState() => _CardKeyPickerState();
}

class _CardKeyPickerState extends State<_CardKeyPicker> {
  late final List<String> _selected = List.of(widget.selected);
  late CardSetInfo _set = widget.sets.first;
  @override
  Widget build(BuildContext context) => Dialog(child: SizedBox(width: 620, height: 620, child: Padding(
    padding: const EdgeInsets.all(16), child: Column(children: [
      Text('Три карты по порядку · выбрано ${_selected.length}/3'),
      DropdownButton<CardSetInfo>(value: _set, isExpanded: true,
        items: [for (final s in widget.sets) DropdownMenuItem(value: s, child: Text(s.title))],
        onChanged: (s) { if (s != null) setState(() => _set = s); }),
      Expanded(child: GridView.builder(gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
        maxCrossAxisExtent: 110, crossAxisSpacing: 6, mainAxisSpacing: 6), itemCount: _set.cards.length,
        itemBuilder: (context, i) {
          final id = _set.cards[i];
          final order = _selected.indexOf(id);
          return Semantics(label: 'Карта $id${order >= 0 ? ', номер ${order + 1}' : ''}', button: true,
            child: InkWell(onTap: () => setState(() {
              if (order >= 0) { _selected.remove(id); } else if (_selected.length < 3) { _selected.add(id); }
            }), child: Stack(fit: StackFit.expand, children: [
              CardImage(cardId: id, size: 110),
              if (order >= 0) Align(alignment: Alignment.topRight, child: CircleAvatar(radius: 14, child: Text('${order + 1}'))),
            ])));
        })),
      Row(mainAxisAlignment: MainAxisAlignment.end, children: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Отмена')),
        FilledButton(onPressed: _selected.length == 3 ? () => Navigator.pop(context, _selected) : null, child: const Text('Готово')),
      ]),
    ]),
  )));
}
