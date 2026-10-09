import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'bots_admin_screen.dart' show isAdminProvider;

const socialTraits = [
  (key: 'expectedActivity', title: 'Ожидает активности', low: 'любит молчаливых', high: 'любит разговорчивых', initial: .5),
  (key: 'expectedAgreement', title: 'Ожидает согласия', low: 'ценит самостоятельность', high: 'ценит единомышленников', initial: .5),
  (key: 'similarity', title: 'Сходство стиля общения', low: 'любит противоположности', high: 'любит похожих на себя', initial: .5),
  (key: 'skillRespect', title: 'Чужое мастерство', low: 'завидует сильным', high: 'уважает сильных', initial: .75),
  (key: 'reciprocity', title: 'Благодарность за лайки', low: 'не учитывает', high: 'очень ценит', initial: .5),
  (key: 'honesty', title: 'Честность о письмах', low: 'прощает обман', high: 'ценит честность', initial: .7),
  (key: 'familiarity', title: 'Привязанность к знакомым', low: 'не учитывает', high: 'ценит общие партии', initial: .5),
  (key: 'forgiveness', title: 'Обидчивость', low: 'долго помнит обиды', high: 'быстро прощает', initial: .5),
  (key: 'influence', title: 'Влияние симпатии на игру', low: 'не учитывает', high: 'сильно учитывает', initial: .35),
];

const _componentNames = {
  'expectations': 'Совпадение с ожиданиями', 'similarity': 'Сходство стиля общения',
  'skill': 'Отношение к мастерству', 'reciprocity': 'Благодарность',
  'cooperation': 'Согласие в голосовании', 'honesty': 'Честность о письмах', 'familiarity': 'Знакомство',
};

final botRelationshipsProvider = FutureProvider.autoDispose.family<List<BotRelationship>, ({String id, bool details})>(
  (ref, key) => ref.read(apiProvider).botRelationships(key.id, details: key.details),
);

class BotRelationships extends ConsumerWidget {
  const BotRelationships({super.key, required this.botId});
  final String botId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final admin = ref.watch(isAdminProvider).valueOrNull ?? false;
    final key = (id: botId, details: admin);
    final relationships = ref.watch(botRelationshipsProvider(key));
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text('СИМПАТИИ К ИГРОКАМ', style: sectionLabel(size: 14)),
        const SizedBox(height: 8),
        const Text('От −100 до +100. Меняются после совместных партий. Симпатия не означает, что бот знает роль игрока.',
          style: TextStyle(color: AppColors.muted, fontSize: 12)),
        const SizedBox(height: 8),
        relationships.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(botRelationshipsProvider(key))),
          data: (rows) => rows.isEmpty
            ? const Text('Пока нет совместных завершённых партий. К новым игрокам бот относится нейтрально.')
            : Column(children: [for (final row in rows) _RelationshipTile(row: row, admin: admin)]),
        ),
      ]),
    );
  }
}

class _RelationshipTile extends StatelessWidget {
  const _RelationshipTile({required this.row, required this.admin});
  final BotRelationship row;
  final bool admin;

  @override
  Widget build(BuildContext context) {
    final player = row.player;
    final summary = ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 4),
      leading: Avatar(nickname: player.nickname, color: player.avatarColor, photoId: player.avatarId, size: 36),
      title: Text(player.nickname, maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Text('${row.attitude} · общих партий: ${row.sharedGames}'),
      trailing: Text('${row.score > 0 ? '+' : ''}${row.score}', style: heading(22, color: row.score < 0 ? AppColors.redSoft : AppColors.ice)),
      onTap: () => context.push('/profile/${player.id}'),
    );
    return Column(children: [
      summary,
      if (admin && row.components != null)
        ExpansionTile(
          key: Key('affinity-details-${player.id}'),
          title: const Text('Причины симпатии · только администратору', style: TextStyle(fontSize: 12)),
          children: [
            const Padding(padding: EdgeInsets.all(8), child: Text('Вклад накопленных впечатлений в общий балл. Свежие партии влияют сильнее.', style: TextStyle(fontSize: 12))),
            for (final entry in row.components!.entries)
              ListTile(dense: true, title: Text(_componentNames[entry.key] ?? entry.key),
                trailing: Text('${entry.value > 0 ? '+' : ''}${entry.value.toStringAsFixed(1)}')),
          ],
        ),
      const Divider(),
    ]);
  }
}
