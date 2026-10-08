import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/avatar_picker.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import '../auth/login_screen.dart' show avatarPalette;
import '../profile/avatar_crop.dart';

/// Админ ли я — показывать ли пункт «Боты» в меню.
final isAdminProvider = FutureProvider.autoDispose<bool>((ref) async {
  try {
    return await ref.read(apiProvider).isAdmin();
  } catch (_) {
    return false;
  }
});

final adminBotsProvider = FutureProvider.autoDispose<List<BotInfo>>((ref) => ref.read(apiProvider).adminBots());

/// Спектр характера: что означают края шкалы.
class Spectrum {
  const Spectrum(this.key, this.title, this.low, this.high);

  final String key;
  final String title;
  final String low;
  final String high;
}

const spectra = [
  Spectrum('negative', 'Выводы из неоткрытого', 'не достали — и ладно', 'не достали — точно не оно'),
  Spectrum('memory', 'Память о прошлых играх', 'каждая игра с чистого листа', 'был Убийцей — значит, и сейчас'),
  Spectrum('risk', 'Риск', 'не врёт и не выдаёт себя', 'блефует и обвиняет в лоб'),
  Spectrum('compromise', 'Компромисс', 'не слушает даже Эксперта', 'договорится даже с Убийцей'),
  Spectrum('variability', 'Изменчивость', 'всегда одинаковый', 'каждую партию другой'),
];

double spectrumValue(BotSpectra s, String key) => switch (key) {
      'negative' => s.negative,
      'memory' => s.memory,
      'risk' => s.risk,
      'compromise' => s.compromise,
      _ => s.variability,
    };

BotSpectra withSpectrum(BotSpectra s, String key, double v) => switch (key) {
      'negative' => s.copyWith(negative: v),
      'memory' => s.copyWith(memory: v),
      'risk' => s.copyWith(risk: v),
      'compromise' => s.copyWith(compromise: v),
      _ => s.copyWith(variability: v),
    };

/// Кабинет ботов: общий набор характеров (только для админа).
class BotsAdminScreen extends ConsumerWidget {
  const BotsAdminScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bots = ref.watch(adminBotsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('БОТЫ')),
      floatingActionButton: FloatingActionButton.extended(
        key: const Key('bot-new'),
        onPressed: () => _edit(context, ref, null),
        icon: const Icon(Icons.add),
        label: const Text('Новый бот'),
      ),
      body: bots.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(adminBotsProvider)),
        data: (list) => ListView(padding: pageInsets(context, bottom: 96), children: [
          const Text(
            'У каждого бота свой характер: как он смотрит на карты, что думает о неоткрытых письмах, '
            'помнит ли прошлые партии, рискует ли и слушает ли других. Хост выбирает бота в лобби.',
            style: TextStyle(fontSize: 13, color: AppColors.muted, height: 1.4),
          ),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            key: const Key('bot-presets'),
            icon: const Icon(Icons.auto_awesome_outlined),
            label: const Text('Добавить готовые характеры'),
            onPressed: () async {
              final created = await runAction(context, () => ref.read(apiProvider).createPresetBots());
              if (created != null) ref.invalidate(adminBotsProvider);
            },
          ),
          const SizedBox(height: 12),
          if (list.isEmpty)
            const Padding(
              padding: EdgeInsets.all(24),
              child: Text('Ботов пока нет — создайте своего или добавьте готовые характеры.', style: TextStyle(color: AppColors.muted)),
            ),
          for (final b in list)
            Padding(
              key: Key('bot-${b.id}'),
              padding: const EdgeInsets.only(bottom: 8),
              child: Panel(
                padding: const EdgeInsets.all(12),
                child: InkWell(
                  onTap: () => _edit(context, ref, b),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Row(children: [
                      Avatar(nickname: b.nickname.replaceFirst('Бот ', ''), color: b.avatarColor, photoId: b.avatarId, size: 40),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                          Text(b.nickname, style: heading(17, spacing: 0.5)),
                          Text('рейтинг ${b.rating} · партий ${b.games} · побед ${b.wins}${b.enabled ? '' : ' · выключен'}',
                              style: const TextStyle(fontSize: 12, color: AppColors.muted)),
                        ]),
                      ),
                      const Icon(Icons.chevron_right, color: AppColors.muted),
                    ]),
                    if (b.about.isNotEmpty) ...[
                      const SizedBox(height: 6),
                      Text(b.about, style: const TextStyle(fontSize: 13)),
                    ],
                    const SizedBox(height: 8),
                    _MiniSpectra(values: b.spectra),
                  ]),
                ),
              ),
            ),
        ]),
      ),
    );
  }

  Future<void> _edit(BuildContext context, WidgetRef ref, BotInfo? bot) async {
    final saved = await Navigator.of(context).push<BotInfo>(MaterialPageRoute(builder: (_) => BotEditorScreen(bot: bot)));
    if (saved != null) ref.invalidate(adminBotsProvider);
  }
}

/// Короткая сводка характера: полоски спектров.
class _MiniSpectra extends StatelessWidget {
  const _MiniSpectra({required this.values});

  final BotSpectra values;

  @override
  Widget build(BuildContext context) {
    final (m, s, c) = values.attentionPercent;
    final items = <(String, String, double?)>[
      ('смысл/форма/цвет', '$m/$s/$c', null),
      for (final sp in spectra) (sp.title.toLowerCase(), '', spectrumValue(values, sp.key)),
    ];
    return Wrap(spacing: 10, runSpacing: 6, children: [
      for (final (label, text, value) in items)
        SizedBox(
          width: 150,
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(text.isEmpty ? label : '$label $text', style: const TextStyle(fontSize: 11, color: AppColors.muted)),
            if (value != null)
              Padding(
                padding: const EdgeInsets.only(top: 2),
                child: LinearProgressIndicator(value: value, minHeight: 4, borderRadius: BorderRadius.circular(2)),
              ),
          ]),
        ),
    ]);
  }
}

/// Редактор бота: имя, цвет, описание и спектры характера.
class BotEditorScreen extends ConsumerStatefulWidget {
  const BotEditorScreen({super.key, this.bot});

  final BotInfo? bot;

  @override
  ConsumerState<BotEditorScreen> createState() => _BotEditorScreenState();
}

class _BotEditorScreenState extends ConsumerState<BotEditorScreen> {
  late final _name = TextEditingController(text: widget.bot?.nickname.replaceFirst('Бот ', '') ?? '');
  late final _about = TextEditingController(text: widget.bot?.about ?? '');
  late BotSpectra _s = widget.bot?.spectra ?? const BotSpectra();
  late String _color = widget.bot?.avatarColor ?? avatarPalette.first;
  late bool _enabled = widget.bot?.enabled ?? true;
  bool _busy = false;

  /// Новое фото (уже обрезанное) или «убрать фото» — применяются при сохранении,
  /// чтобы у нового бота фото тоже можно было выбрать до создания.
  Uint8List? _newPhoto;
  bool _removePhoto = false;

  /// Новый бот уже создан, а фото не загрузилось — повторное «Сохранить» не плодит второго.
  String? _createdId;

  bool get _hasPhoto => _newPhoto != null || (widget.bot?.avatarId != null && !_removePhoto);

  Future<void> _pickPhoto() async {
    final picked = await ref.read(avatarPickerProvider)();
    if (picked == null || !mounted) return;
    final cropped = await ref.read(avatarCropperProvider)(context, picked.bytes);
    if (cropped == null || !mounted) return;
    setState(() {
      _newPhoto = cropped;
      _removePhoto = false;
    });
  }

  @override
  void dispose() {
    _name.dispose();
    _about.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_name.text.trim().length < 2) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Имя — хотя бы 2 буквы')));
      return;
    }
    setState(() => _busy = true);
    final api = ref.read(apiProvider);
    final saved = await runAction(context, () async {
      var bot = await api.saveBot(
        id: widget.bot?.id ?? _createdId,
        nickname: _name.text.trim(),
        color: _color,
        about: _about.text.trim(),
        spectra: _s,
        enabled: _enabled,
      );
      _createdId = bot.id;
      final photo = _newPhoto;
      if (photo != null) {
        bot = await api.uploadBotAvatar(bot.id, photo, 'avatar.png');
      } else if (_removePhoto && bot.avatarId != null) {
        bot = await api.removeBotAvatar(bot.id);
      }
      return bot;
    });
    if (!mounted) return;
    setState(() => _busy = false);
    if (saved != null) Navigator.of(context).pop(saved);
  }

  Widget _slider(String key, String title, String low, String high, double value, ValueChanged<double> onChanged) => Padding(
        padding: const EdgeInsets.only(top: 10),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
          Slider(key: Key('spectrum-$key'), value: value.clamp(0, 1), divisions: 20, onChanged: onChanged),
          Row(children: [
            Expanded(child: Text(low, style: const TextStyle(fontSize: 11, color: AppColors.muted))),
            const SizedBox(width: 8),
            Expanded(child: Text(high, textAlign: TextAlign.end, style: const TextStyle(fontSize: 11, color: AppColors.muted))),
          ]),
        ]),
      );

  @override
  Widget build(BuildContext context) {
    final (m, sh, c) = _s.attentionPercent;
    return Scaffold(
      appBar: AppBar(title: Text(widget.bot == null ? 'НОВЫЙ БОТ' : 'ХАРАКТЕР БОТА')),
      body: ListView(padding: pageInsets(context, bottom: 32), children: [
        TextField(
          key: const Key('bot-name'),
          controller: _name,
          maxLength: 16,
          decoration: const InputDecoration(labelText: 'Имя (к нему добавится «Бот»)', counterText: ''),
        ),
        const SizedBox(height: 8),
        TextField(
          key: const Key('bot-about'),
          controller: _about,
          maxLength: 300,
          maxLines: 2,
          decoration: const InputDecoration(labelText: 'Пара слов о характере — видно хосту в лобби'),
        ),
        const SizedBox(height: 8),
        Row(children: [
          Avatar(
            key: const Key('bot-avatar'),
            nickname: _name.text.isEmpty ? '?' : _name.text,
            color: _color,
            photoId: _removePhoto ? null : widget.bot?.avatarId,
            photoBytes: _newPhoto,
            size: 72,
            highlight: true,
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Wrap(spacing: 8, runSpacing: 8, children: [
              OutlinedButton.icon(
                key: const Key('bot-photo-pick'),
                icon: const Icon(Icons.photo_library_outlined),
                label: Text(_hasPhoto ? 'Сменить фото' : 'Фото'),
                onPressed: _busy ? null : _pickPhoto,
              ),
              if (_hasPhoto)
                TextButton.icon(
                  key: const Key('bot-photo-remove'),
                  icon: const Icon(Icons.delete_outline),
                  label: const Text('Убрать'),
                  onPressed: _busy
                      ? null
                      : () => setState(() {
                            _newPhoto = null;
                            _removePhoto = true;
                          }),
                ),
            ]),
          ),
        ]),
        const SizedBox(height: 12),
        const Text('Цвет — если фото нет', style: TextStyle(fontSize: 12, color: AppColors.muted)),
        const SizedBox(height: 6),
        Wrap(spacing: 8, runSpacing: 8, children: [
          for (final col in avatarPalette)
            GestureDetector(
              onTap: () => setState(() => _color = col),
              child: Avatar(nickname: _name.text.isEmpty ? '?' : _name.text, color: col, size: 36, highlight: col == _color),
            ),
        ]),
        SwitchListTile(
          key: const Key('bot-enabled'),
          contentPadding: EdgeInsets.zero,
          title: const Text('Предлагать в лобби'),
          value: _enabled,
          onChanged: (v) => setState(() => _enabled = v),
        ),
        const Divider(),
        Text('КАК СМОТРИТ НА КАРТЫ', style: sectionLabel(size: 13)),
        Text('Смысл $m% · форма $sh% · цвет $c%', key: const Key('attention-total'), style: const TextStyle(fontSize: 13, color: AppColors.muted)),
        _slider('meaning', 'Смысл', 'не замечает', 'главное — что изображено', _s.meaning, (v) => setState(() => _s = _s.copyWith(meaning: v))),
        _slider('shape', 'Форма', 'не замечает', 'длинное к длинному, круглое к круглому', _s.shape, (v) => setState(() => _s = _s.copyWith(shape: v))),
        _slider('color', 'Цвет', 'не замечает', 'красное к красному', _s.color, (v) => setState(() => _s = _s.copyWith(color: v))),
        const Divider(height: 32),
        Text('ХАРАКТЕР', style: sectionLabel(size: 13)),
        for (final sp in spectra)
          _slider(sp.key, sp.title, sp.low, sp.high, spectrumValue(_s, sp.key), (v) => setState(() => _s = withSpectrum(_s, sp.key, v))),
        const SizedBox(height: 20),
        FilledButton(
          key: const Key('bot-save'),
          onPressed: _busy ? null : _save,
          child: const Text('Сохранить'),
        ),
      ]),
    );
  }
}
