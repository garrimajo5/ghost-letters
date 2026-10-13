import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

final learnedPhrasesProvider = FutureProvider.autoDispose<LearnedPhrases>((ref) => ref.read(apiProvider).learnedPhrases());

/// Кабинет фраз (только админ): чему боты научились у людей. Любую можно сразу проиграть голосом бота той же
/// манеры, неудачную — запретить.
class LearnedPhrasesScreen extends ConsumerStatefulWidget {
  const LearnedPhrasesScreen({super.key});

  @override
  ConsumerState<LearnedPhrasesScreen> createState() => _LearnedPhrasesScreenState();
}

class _LearnedPhrasesScreenState extends ConsumerState<LearnedPhrasesScreen> {
  AudioPlayer? _player;
  String? _manner;
  String? _intent;
  String? _playing;
  final Map<String, PlayedPhrase> _played = {};
  final Map<String, String> _speakerFor = {};

  @override
  void dispose() {
    _player?.dispose();
    super.dispose();
  }

  Future<void> _play(LearnedPhrase p) async {
    setState(() => _playing = p.id);
    try {
      final played = await runAction(context, () => ref.read(apiProvider).playPhrase(p.id, botId: _speakerFor[p.id]));
      if (played == null || !mounted) return;
      setState(() => _played[p.id] = played);
      final audio = played.audio;
      if (audio != null) {
        final player = _player ??= AudioPlayer();
        await player.stop();
        await player.play(BytesSource(audio, mimeType: 'audio/mpeg'));
      }
    } finally {
      if (mounted) setState(() => _playing = null);
    }
  }

  Widget _chips(String label, Map<String, String> options, String? value, void Function(String?) onChanged, String prefix) => Padding(
        padding: const EdgeInsets.only(bottom: 6),
        child: Wrap(spacing: 6, runSpacing: 6, crossAxisAlignment: WrapCrossAlignment.center, children: [
          Text(label, style: const TextStyle(fontSize: 12, color: AppColors.muted)),
          ChoiceChip(key: Key('$prefix-all'), label: const Text('все'), selected: value == null, onSelected: (_) => onChanged(null)),
          for (final e in options.entries)
            ChoiceChip(key: Key('$prefix-${e.key}'), label: Text(e.value), selected: value == e.key, onSelected: (_) => onChanged(e.key)),
        ]),
      );

  @override
  Widget build(BuildContext context) {
    final data = ref.watch(learnedPhrasesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('ФРАЗЫ БОТОВ')),
      body: data.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(learnedPhrasesProvider)),
        data: (book) {
          final list = book.phrases.where((p) => (_manner == null || p.manner == _manner) && (_intent == null || p.intent == _intent)).toList();
          return ListView(padding: pageInsets(context), children: [
            const Text(
              'Боты слушают, как говорят люди за столом, выделяют в реплике мысль, эмоцию и манеру и потом говорят так же — '
              'те боты, у кого похожая манера. Вместо карты — {card}, вместо имени — {player}. ▶ — проиграть голосом бота.',
              style: TextStyle(fontSize: 13, color: AppColors.muted, height: 1.4),
            ),
            if (!book.voiceEnabled)
              const Padding(
                padding: EdgeInsets.only(top: 6),
                child: Text('Озвучка выключена на сервере — будет показан только текст.', style: TextStyle(fontSize: 12, color: AppColors.muted)),
              ),
            const SizedBox(height: 10),
            _chips('Манера:', LearnedPhrase.manners, _manner, (v) => setState(() => _manner = v), 'phrase-manner'),
            _chips('Мысль:', LearnedPhrase.intents, _intent, (v) => setState(() => _intent = v), 'phrase-intent'),
            const SizedBox(height: 6),
            if (list.isEmpty)
              const Padding(
                padding: EdgeInsets.all(24),
                child: Text('Пока ничего не выучено — сыграйте с ботами и поговорите в чате или голосом.', style: TextStyle(color: AppColors.muted)),
              ),
            for (final p in list) _phrase(p, book.speakers[p.manner] ?? const []),
          ]);
        },
      ),
    );
  }

  Widget _phrase(LearnedPhrase p, List<({String id, String nickname})> speakers) {
    final played = _played[p.id];
    final chosen = speakers.any((s) => s.id == _speakerFor[p.id]) ? _speakerFor[p.id] : null;
    return Padding(
      key: Key('phrase-${p.id}'),
      padding: const EdgeInsets.only(bottom: 8),
      child: Panel(
        padding: const EdgeInsets.fromLTRB(4, 8, 4, 8),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          IconButton(
            key: Key('phrase-play-${p.id}'),
            tooltip: 'Проиграть',
            onPressed: _playing == null ? () => _play(p) : null,
            icon: _playing == p.id
                ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2))
                : const Icon(Icons.play_circle_outline),
          ),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const SizedBox(height: 4),
              Text(p.template,
                  style: TextStyle(fontSize: 15, decoration: p.blocked ? TextDecoration.lineThrough : null,
                      color: p.blocked ? AppColors.muted : null)),
              const SizedBox(height: 4),
              Text(
                '${LearnedPhrase.intents[p.intent] ?? p.intent} · ${LearnedPhrase.emotions[p.emotion] ?? p.emotion} · '
                'манера ${LearnedPhrase.manners[p.manner] ?? p.manner} · ${p.author} · ×${p.seen}',
                style: const TextStyle(fontSize: 12, color: AppColors.muted),
              ),
              if (speakers.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Wrap(spacing: 4, runSpacing: 4, children: [
                    for (final s in speakers)
                      ChoiceChip(
                        key: Key('phrase-speaker-${p.id}-${s.id}'),
                        visualDensity: VisualDensity.compact,
                        label: Text(s.nickname.replaceFirst('Бот ', ''), style: const TextStyle(fontSize: 12)),
                        selected: chosen == s.id,
                        onSelected: (on) => setState(() => on ? _speakerFor[p.id] = s.id : _speakerFor.remove(p.id)),
                      ),
                  ]),
                ),
              if (played != null)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text('${played.speaker.replaceFirst('Бот ', '')}: «${played.text}»',
                      key: Key('phrase-said-${p.id}'), style: const TextStyle(fontSize: 13, fontStyle: FontStyle.italic)),
                ),
            ]),
          ),
          IconButton(
            key: Key('phrase-block-${p.id}'),
            tooltip: p.blocked ? 'Вернуть' : 'Запретить',
            icon: Icon(p.blocked ? Icons.undo : Icons.block),
            onPressed: () async {
              await runAction(context, () => ref.read(apiProvider).blockPhrase(p.id, !p.blocked).then((_) => true));
              ref.invalidate(learnedPhrasesProvider);
            },
          ),
        ]),
      ),
    );
  }
}
