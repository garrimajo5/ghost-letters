import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../core/api.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

class PresetsSheet extends ConsumerStatefulWidget {
  const PresetsSheet({super.key, required this.current, this.players});
  final LobbySettings current;
  final int? players;

  static Future<LobbySettings?> show(BuildContext context, LobbySettings current, int? players) =>
      showModalBottomSheet<LobbySettings>(context: context, isScrollControlled: true, useSafeArea: true,
          builder: (_) => PresetsSheet(current: current, players: players));

  @override
  ConsumerState<PresetsSheet> createState() => _PresetsSheetState();
}

class _PresetsSheetState extends ConsumerState<PresetsSheet> {
  late Future<List<SettingsPreset>> _presets;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _presets = ref.read(apiProvider).settingsPresets();
  }

  void _reload() => setState(() { _presets = ref.read(apiProvider).settingsPresets(); });

  Future<String?> _name(String initial) async {
    return showDialog<String>(context: context, builder: (_) => _PresetNameDialog(initial: initial));
  }

  Future<void> _save({SettingsPreset? preset, bool renameOnly = false}) async {
    final name = await _name(preset?.name ?? 'Мой пресет');
    if (name == null || !mounted) return;
    setState(() => _busy = true);
    await runAction(context, () => ref.read(apiProvider).saveSettingsPreset(
        preset?.id ?? const Uuid().v4(), name, (renameOnly ? preset!.settings : widget.current).copyWith(clearGhost: true)));
    if (mounted) { setState(() => _busy = false); _reload(); }
  }

  Future<void> _delete(SettingsPreset preset) async {
    final confirmed = await showDialog<bool>(context: context, builder: (context) => AlertDialog(
      title: Text('Удалить «${preset.name}»?'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Отмена')),
        TextButton(onPressed: () => Navigator.pop(context, true), child: const Text('Удалить')),
      ],
    ));
    if (confirmed != true || !mounted) return;
    setState(() => _busy = true);
    await runAction(context, () => ref.read(apiProvider).deleteSettingsPreset(preset.id));
    if (mounted) { setState(() => _busy = false); _reload(); }
  }

  void _apply(LobbySettings settings) => Navigator.pop(context, settings.resolveForPlayers(widget.players ?? 3));

  @override
  Widget build(BuildContext context) => SizedBox(
    height: MediaQuery.sizeOf(context).height * 0.8,
    child: Column(children: [
      const ListTile(title: Text('Пресеты настроек'), subtitle: Text('Личные пресеты сохраняются в вашем аккаунте. Применение не меняет лобби до нажатия «Сохранить».')),
      Expanded(child: ListView(children: [
        ListTile(key: const Key('preset-classic'), title: const Text('Классика'),
          subtitle: const Text('Исходные правила игры: 5 карт в ряду, раунды и роли по стандартной таблице.'),
          onTap: _busy ? null : () => _apply(const LobbySettings())),
        ListTile(key: const Key('preset-ozon'), title: const Text('Озон'),
          subtitle: const Text('3–11 игроков · 4 ряда · поле, раунды и роли по таблице Озона. На четверых Убийцы может не быть.'),
          onTap: _busy ? null : () => _apply(const LobbySettings(rulesPreset: 'ozon'))),
        const Divider(),
        Padding(padding: const EdgeInsets.symmetric(horizontal: 16), child: OutlinedButton.icon(
          key: const Key('save-new-preset'), onPressed: _busy ? null : () => _save(),
          icon: const Icon(Icons.add), label: const Text('Сохранить текущие как новый пресет'))),
        FutureBuilder<List<SettingsPreset>>(future: _presets, builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
          if (snapshot.hasError) return ErrorRetry(message: ApiError.from(snapshot.error!).message, onRetry: _reload);
          if (snapshot.data!.isEmpty) return const ListTile(title: Text('Личных пресетов пока нет'));
          return Column(children: [for (final preset in snapshot.data!) ListTile(
            key: Key('preset-${preset.id}'), title: Text(preset.name),
            subtitle: const Text('Личный пресет'), onTap: _busy ? null : () => _apply(preset.settings),
            trailing: PopupMenuButton<String>(enabled: !_busy, onSelected: (action) {
              if (action == 'delete') {
                _delete(preset);
              } else {
                _save(preset: preset, renameOnly: action == 'rename');
              }
            }, itemBuilder: (_) => const [
              PopupMenuItem(value: 'rename', child: Text('Переименовать')),
              PopupMenuItem(value: 'update', child: Text('Заменить текущими настройками')),
              PopupMenuItem(value: 'delete', child: Text('Удалить')),
            ]),
          )]);
        }),
      ])),
    ]),
  );
}

class _PresetNameDialog extends StatefulWidget {
  const _PresetNameDialog({required this.initial});
  final String initial;
  @override
  State<_PresetNameDialog> createState() => _PresetNameDialogState();
}

class _PresetNameDialogState extends State<_PresetNameDialog> {
  late final controller = TextEditingController(text: widget.initial);
  @override
  void dispose() { controller.dispose(); super.dispose(); }
  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Название пресета'),
    content: TextField(key: const Key('preset-name'), controller: controller, autofocus: true, maxLength: 40),
    actions: [
      TextButton(onPressed: () => Navigator.pop(context), child: const Text('Отмена')),
      TextButton(key: const Key('confirm-preset-name'), onPressed: () {
        if (controller.text.trim().isNotEmpty) Navigator.pop(context, controller.text.trim());
      }, child: const Text('Сохранить')),
    ],
  );
}
