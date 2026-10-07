import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
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
      appBar: AppBar(title: const Text('Профиль')),
      body: profile.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(child: Text(ApiError.from(e).message)),
        data: (p) => ListView(padding: const EdgeInsets.all(16), children: [
          Center(child: Avatar(nickname: p.user.nickname, color: p.user.avatarColor, size: 88)),
          const SizedBox(height: 8),
          Center(child: Text(p.user.nickname, style: Theme.of(context).textTheme.headlineSmall)),
          const SizedBox(height: 16),
          Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: [
            _Stat('Рейтинг', '${p.rating}'),
            _Stat('Партии', '${p.games}'),
            _Stat('Победы', '${p.wins}'),
            _Stat('Лайки', '${p.likes}'),
          ]),
          const SizedBox(height: 24),
          Text('Ачивки', style: Theme.of(context).textTheme.titleMedium),
          if (p.achievements.isEmpty) const Padding(padding: EdgeInsets.all(8), child: Text('Пока нет — всё впереди')),
          for (final a in p.achievements)
            ListTile(leading: const Icon(Icons.emoji_events, color: Colors.amber), title: Text(a.title), trailing: Text('×${a.count}')),
        ]),
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Column(children: [
        Text(value, style: Theme.of(context).textTheme.titleLarge),
        Text(label, style: Theme.of(context).textTheme.bodySmall),
      ]);
}
