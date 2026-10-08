import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'sound.dart';

class SoundSettingsSheet extends ConsumerWidget {
  const SoundSettingsSheet({super.key});
  static Future<void> show(BuildContext context) => showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        useSafeArea: true,
        constraints: const BoxConstraints(maxWidth: 520),
        builder: (_) => const SoundSettingsSheet(),
      );

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(soundSettingsProvider);
    final controller = ref.read(soundSettingsProvider.notifier);
    return SingleChildScrollView(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(children: [
                const Expanded(
                    child:
                        Text('Звук и музыка', style: TextStyle(fontSize: 22))),
                IconButton(
                    tooltip: 'Закрыть',
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(Icons.close)),
              ]),
              const Text('Личные настройки на этом устройстве'),
              SwitchListTile(
                key: const Key('music-switch'),
                contentPadding: EdgeInsets.zero,
                title: const Text('Музыка'),
                value: value.music,
                onChanged: (enabled) => controller.update(music: enabled),
              ),
              Semantics(
                label: 'Громкость музыки',
                child: Slider(
                  key: const Key('music-volume'),
                  value: value.musicVolume,
                  divisions: 20,
                  label: '${(value.musicVolume * 100).round()}%',
                  onChanged: value.music
                      ? (v) => controller.update(musicVolume: v)
                      : null,
                ),
              ),
              SwitchListTile(
                key: const Key('effects-switch'),
                contentPadding: EdgeInsets.zero,
                title: const Text('Звуки'),
                value: value.effects,
                onChanged: (enabled) => controller.update(effects: enabled),
              ),
              Semantics(
                label: 'Громкость звуков',
                child: Slider(
                  key: const Key('effects-volume'),
                  value: value.effectsVolume,
                  divisions: 20,
                  label: '${(value.effectsVolume * 100).round()}%',
                  onChanged: value.effects
                      ? (v) => controller.update(effectsVolume: v)
                      : null,
                ),
              ),
            ]),
      ),
    );
  }
}
