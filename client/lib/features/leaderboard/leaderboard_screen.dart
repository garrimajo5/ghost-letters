import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/session.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

/// Показывать ли ботов в таблице (галочка запоминается до перезапуска).
final showBotsProvider = StateProvider<bool>((ref) => false);

final leaderboardProvider = FutureProvider.autoDispose<List<LeaderRow>>(
  (ref) => ref.read(apiProvider).leaderboard(bots: ref.watch(showBotsProvider)),
);

/// Таблица лидеров по рейтингу. Рейтинг меняют только рейтинговые партии с Убийцей.
class LeaderboardScreen extends ConsumerWidget {
  const LeaderboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final rows = ref.watch(leaderboardProvider);
    final bots = ref.watch(showBotsProvider);
    final me = ref.watch(sessionProvider).user?.id;
    return Scaffold(
      appBar: AppBar(title: const Text('РЕЙТИНГ')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(leaderboardProvider),
        child: ListView(padding: pageInsets(context), children: [
          CheckboxListTile(
            key: const Key('show-bots'),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text('Показать ботов'),
            value: bots,
            onChanged: (v) => ref.read(showBotsProvider.notifier).state = v ?? false,
          ),
          const Text(
            'Рейтинг меняют рейтинговые партии с Убийцей. Кооператив и обычные партии — только в статистике.',
            style: TextStyle(fontSize: 12, color: AppColors.muted),
          ),
          const SizedBox(height: 12),
          ...rows.when(
            loading: () => [const Padding(padding: EdgeInsets.all(24), child: Center(child: CircularProgressIndicator()))],
            error: (e, _) => [ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(leaderboardProvider))],
            data: (list) => list.isEmpty
                ? [const Padding(padding: EdgeInsets.all(24), child: Text('Пока никто не сыграл рейтинговую партию', style: TextStyle(color: AppColors.muted)))]
                : [
                    for (final (i, r) in list.indexed)
                      Padding(
                        key: Key('leader-${r.user.id}'),
                        padding: const EdgeInsets.only(bottom: 6),
                        child: Panel(
                          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                          border: r.user.id == me ? AppColors.amber.withValues(alpha: 0.6) : null,
                          child: InkWell(
                            onTap: () => context.push('/profile/${r.user.id}'),
                            child: Row(children: [
                              SizedBox(width: 32, child: Text('${i + 1}', style: heading(16, color: i < 3 ? AppColors.amber : AppColors.muted))),
                              r.isBot
                                  ? CircleAvatar(radius: 18, backgroundColor: colorFromHex(r.user.avatarColor), child: const Icon(Icons.smart_toy_outlined, size: 18, color: Colors.white))
                                  : Avatar(nickname: r.user.nickname, color: r.user.avatarColor, photoId: r.user.avatarId, size: 36),
                              const SizedBox(width: 10),
                              Expanded(
                                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                                  Text(r.user.nickname, maxLines: 1, overflow: TextOverflow.ellipsis),
                                  Text('партий ${r.games} · побед ${r.wins}${r.isBot ? ' · бот' : ''}',
                                      style: const TextStyle(fontSize: 12, color: AppColors.muted)),
                                ]),
                              ),
                              Text('${r.rating}', style: heading(20, color: AppColors.ice)),
                            ]),
                          ),
                        ),
                      ),
                  ],
          ),
        ]),
      ),
    );
  }
}
