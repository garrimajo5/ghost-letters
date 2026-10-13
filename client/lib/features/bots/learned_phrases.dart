import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

final learnedPhrasesProvider = FutureProvider.autoDispose<List<LearnedPhrase>>((ref) => ref.read(apiProvider).learnedPhrases());

/// Фразы, которым боты научились у людей. Неудачные можно запретить — боты перестанут так говорить.
class LearnedPhrasesScreen extends ConsumerWidget {
  const LearnedPhrasesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final phrases = ref.watch(learnedPhrasesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('ФРАЗЫ')),
      body: phrases.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(learnedPhrasesProvider)),
        data: (list) => ListView(padding: pageInsets(context), children: [
          const Text(
            'Боты слушают, как говорят люди за столом, выделяют в реплике мысль, эмоцию и манеру и потом говорят так же — '
            'те боты, у кого похожий характер. Вместо карты — {card}, вместо имени — {player}.',
            style: TextStyle(fontSize: 13, color: AppColors.muted, height: 1.4),
          ),
          const SizedBox(height: 12),
          if (list.isEmpty)
            const Padding(
              padding: EdgeInsets.all(24),
              child: Text('Пока ничего не выучено — сыграйте с ботами и поговорите в чате или голосом.', style: TextStyle(color: AppColors.muted)),
            ),
          for (final p in list)
            Padding(
              key: Key('phrase-${p.id}'),
              padding: const EdgeInsets.only(bottom: 8),
              child: Panel(
                padding: const EdgeInsets.all(12),
                child: Row(children: [
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(p.template,
                          style: TextStyle(fontSize: 15, decoration: p.blocked ? TextDecoration.lineThrough : null,
                              color: p.blocked ? AppColors.muted : null)),
                      const SizedBox(height: 4),
                      Text(
                        '${LearnedPhrase.intents[p.intent] ?? p.intent} · ${LearnedPhrase.emotions[p.emotion] ?? p.emotion} · '
                        'манера ${LearnedPhrase.manners[p.manner] ?? p.manner} · ${p.author} · ×${p.seen}',
                        style: const TextStyle(fontSize: 12, color: AppColors.muted),
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
            ),
        ]),
      ),
    );
  }
}
