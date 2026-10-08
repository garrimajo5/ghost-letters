import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

final profileProvider = FutureProvider.autoDispose.family<Profile, String>((ref, id) => ref.read(apiProvider).profile(id));

/// Профиль: рейтинг, партии, победы, лайки и полка ачивок.
class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key, required this.userId});

  final String userId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final profile = ref.watch(profileProvider(userId));
    return Scaffold(
      appBar: AppBar(title: const Text('ПРОФИЛЬ')),
      body: profile.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(profileProvider(userId))),
        data: (p) => ListView(padding: pageInsets(context, top: 16), children: [
          Center(child: Avatar(nickname: p.user.nickname, color: p.user.avatarColor, size: 88, highlight: true)),
          const SizedBox(height: 10),
          Center(child: Text(p.user.nickname.toUpperCase(), style: heading(26, spacing: 1.5))),
          const SizedBox(height: 18),
          Panel(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: [
              _Stat('Рейтинг', '${p.rating}', color: AppColors.ice),
              _Stat('Партии', '${p.games}'),
              _Stat('Победы', '${p.wins}'),
              _Stat('Лайки', '${p.likes}', color: AppColors.redSoft),
            ]),
          ),
          const SizedBox(height: 24),
          Text('АЧИВКИ', style: heading(18, color: AppColors.ice, spacing: 2)),
          const SizedBox(height: 8),
          if (p.achievements.isEmpty)
            const Padding(padding: EdgeInsets.all(8), child: Text('Пока нет — всё впереди', style: TextStyle(color: AppColors.muted))),
          for (final a in p.achievements)
            Padding(
              padding: const EdgeInsets.only(bottom: 6),
              child: Panel(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                child: Row(children: [
                  const Icon(Icons.emoji_events, color: AppColors.amber),
                  const SizedBox(width: 12),
                  Expanded(child: Text(a.title, style: const TextStyle(fontWeight: FontWeight.w600))),
                  Text('×${a.count}', style: heading(18, color: AppColors.amber, spacing: 0)),
                ]),
              ),
            ),
        ]),
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat(this.label, this.value, {this.color = AppColors.text});

  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) => Column(children: [
        Text(value, style: heading(22, color: color, spacing: 0.5)),
        const SizedBox(height: 2),
        Text(label, style: const TextStyle(fontSize: 12, color: AppColors.muted)),
      ]);
}
