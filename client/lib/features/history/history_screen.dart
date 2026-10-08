import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

final historyProvider = FutureProvider.autoDispose<List<MyGame>>((ref) => ref.read(apiProvider).myGames(status: 'finished'));

/// История моих партий: когда, сколько игроков, моя роль и итог. Нажатие открывает итоги партии.
class HistoryScreen extends ConsumerWidget {
  const HistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final games = ref.watch(historyProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('ИСТОРИЯ ПАРТИЙ')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(historyProvider),
        child: games.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(children: [Padding(padding: const EdgeInsets.all(24), child: Text(ApiError.from(e).message))]),
          data: (list) => list.isEmpty
              ? ListView(children: const [
                  Padding(
                    padding: EdgeInsets.all(24),
                    child: Text('Сыгранных партий пока нет', style: TextStyle(color: AppColors.muted)),
                  ),
                ])
              : LayoutBuilder(builder: (context, box) {
                  // На широком экране — карточки в две колонки по центру.
                  final insets = pageInsets(context, max: 1200, top: 8, bottom: 24);
                  final inner = box.maxWidth - insets.horizontal;
                  final columns = inner >= 900 ? 2 : 1;
                  final cardWidth = ((inner - 8 * (columns - 1)) / columns).floorToDouble();
                  return ListView(padding: insets, children: [
                    Wrap(spacing: 8, runSpacing: 8, children: [
                      for (final g in list) SizedBox(width: cardWidth, child: _HistoryCard(game: g)),
                    ]),
                  ]);
                }),
        ),
      ),
    );
  }
}

class _HistoryCard extends StatelessWidget {
  const _HistoryCard({required this.game});

  final MyGame game;

  @override
  Widget build(BuildContext context) {
    final g = game;
    final won = g.won;
    return Material(
      color: AppColors.surface,
      borderRadius: BorderRadius.circular(16),
      child: InkWell(
        key: Key('history-${g.gameId}'),
        borderRadius: BorderRadius.circular(16),
        onTap: () => context.push('/game/${g.gameId}'),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(children: [
            AppImage(roleImage(g.role), width: 44, height: 62, radius: 6),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(g.title ?? 'Партия', style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 16)),
                const SizedBox(height: 2),
                Text(
                  [
                    T.when(g.finishedAt ?? g.startedAt),
                    if (g.players > 0) T.players(g.players),
                    if (g.role != null) T.role(g.role),
                  ].join(' · '),
                  style: const TextStyle(fontSize: 13, color: AppColors.muted),
                ),
              ]),
            ),
            if (won != null)
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(
                  color: won ? AppColors.green : AppColors.surface2,
                  borderRadius: BorderRadius.circular(99),
                ),
                child: Text(won ? 'Победа' : 'Поражение',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: won ? Colors.white : AppColors.muted)),
              ),
          ]),
        ),
      ),
    );
  }
}
