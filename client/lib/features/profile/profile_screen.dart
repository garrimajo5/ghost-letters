import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/avatar_picker.dart';
import '../../core/session.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'avatar_crop.dart';
import '../bots/bot_relationships.dart';
import '../auth/recovery_screen.dart';

final profileProvider = FutureProvider.autoDispose.family<Profile, String>((ref, id) => ref.read(apiProvider).profile(id));

/// Своё фото: выбрать из галереи (уменьшается до 512 px) или убрать — тогда снова круг с буквой.
Future<void> _editAvatar(BuildContext context, WidgetRef ref, User user) async {
  final action = await showModalBottomSheet<String>(
    context: context,
    builder: (context) => SafeArea(
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        ListTile(
          key: const Key('avatar-pick'),
          leading: const Icon(Icons.photo_library_outlined),
          title: const Text('Выбрать фото'),
          onTap: () => Navigator.pop(context, 'pick'),
        ),
        if (user.avatarId != null)
          ListTile(
            key: const Key('avatar-remove'),
            leading: const Icon(Icons.delete_outline),
            title: const Text('Убрать фото'),
            onTap: () => Navigator.pop(context, 'remove'),
          ),
      ]),
    ),
  );
  if (action == null || !context.mounted) return;
  final api = ref.read(apiProvider);
  final User? updated;
  if (action == 'pick') {
    final picked = await ref.read(avatarPickerProvider)();
    if (picked == null || !context.mounted) return;
    // Обрезка под круг: подвинуть и приблизить, чтобы лицо было по центру.
    final cropped = await ref.read(avatarCropperProvider)(context, picked.bytes);
    if (cropped == null || !context.mounted) return;
    updated = await runAction(context, () => api.uploadAvatar(cropped, 'avatar.png'));
  } else {
    updated = await runAction(context, api.removeAvatar);
  }
  if (updated == null) return;
  ref.read(sessionProvider.notifier).updateUser(updated);
  ref.invalidate(profileProvider(user.id));
}

/// Код для входа в этот аккаунт на другом устройстве (телефон, планшет, браузер).
Future<void> _showLinkCode(BuildContext context, WidgetRef ref) async {
  final code = await runAction(context, () => ref.read(apiProvider).createLinkCode());
  if (code == null || !context.mounted) return;
  final until = '${code.expiresAt.hour.toString().padLeft(2, '0')}:${code.expiresAt.minute.toString().padLeft(2, '0')}';
  await showDialog<void>(
    context: context,
    builder: (context) => AlertDialog(
      title: const Text('Вход на другом устройстве'),
      content: Column(mainAxisSize: MainAxisSize.min, children: [
        const Text(
          'На новом устройстве откройте игру и нажмите «Уже играю на другом устройстве — войти по коду».',
          style: TextStyle(fontSize: 13, color: AppColors.muted, height: 1.4),
        ),
        const SizedBox(height: 16),
        SelectableText(code.pretty, key: const Key('link-code'), style: heading(32, color: AppColors.amber, spacing: 4)),
        const SizedBox(height: 8),
        Text('Код одноразовый, действует до $until.', style: const TextStyle(fontSize: 12, color: AppColors.muted)),
      ]),
      actions: [
        TextButton(
          onPressed: () {
            Clipboard.setData(ClipboardData(text: code.code));
            ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Код скопирован')));
          },
          child: const Text('Скопировать'),
        ),
        FilledButton(onPressed: () => Navigator.pop(context), child: const Text('Готово')),
      ],
    ),
  );
}

/// Профиль: рейтинг, партии, победы, лайки и полка ачивок.
class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key, required this.userId});

  final String userId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final profile = ref.watch(profileProvider(userId));
    final mine = ref.watch(sessionProvider).user?.id == userId;
    return Scaffold(
      appBar: AppBar(title: const Text('ПРОФИЛЬ')),
      body: profile.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(profileProvider(userId))),
        data: (p) => ListView(padding: pageInsets(context, top: 16), children: [
          Center(
            child: Stack(clipBehavior: Clip.none, children: [
              Avatar(nickname: p.user.nickname, color: p.user.avatarColor, photoId: p.user.avatarId, size: 88, highlight: true),
              if (mine)
                Positioned(
                  right: -6,
                  bottom: -6,
                  child: IconButton.filled(
                    key: const Key('avatar-edit'),
                    tooltip: 'Сменить фото',
                    iconSize: 18,
                    onPressed: () => _editAvatar(context, ref, p.user),
                    icon: const Icon(Icons.photo_camera_outlined),
                  ),
                ),
            ]),
          ),
          const SizedBox(height: 10),
          Center(child: Text(p.user.nickname.toUpperCase(), style: heading(26, spacing: 1.5))),
          if (p.isBot) BotRelationships(botId: userId),
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
          if (mine) ...[
            const SizedBox(height: 8),
            // ID нужен, например, чтобы назначить админа кабинета ботов (переменная ADMIN_USER_IDS).
            Center(
              child: TextButton.icon(
                key: const Key('copy-user-id'),
                icon: const Icon(Icons.copy, size: 14),
                label: Text('ID игрока: ${p.user.id.length > 8 ? '${p.user.id.substring(0, 8)}…' : p.user.id}', style: const TextStyle(fontSize: 12, color: AppColors.muted)),
                onPressed: () {
                  Clipboard.setData(ClipboardData(text: p.user.id));
                  ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('ID скопирован')));
                },
              ),
            ),
            const SizedBox(height: 4),
            OutlinedButton.icon(
              key: const Key('recovery-settings'),
              icon: const Icon(Icons.key),
              label: const Text('Ключ входа — слово или три карты'),
              onPressed: () => Navigator.push<void>(context, MaterialPageRoute(builder: (_) => const RecoveryScreen(edit: true))),
            ),
            OutlinedButton.icon(
              key: const Key('link-device'),
              icon: const Icon(Icons.devices_other),
              label: const Text('Войти на другом устройстве'),
              onPressed: () => _showLinkCode(context, ref),
            ),
          ],
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
