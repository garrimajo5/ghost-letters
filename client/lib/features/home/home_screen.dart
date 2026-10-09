import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api.dart';
import '../../core/sound_settings_sheet.dart';
import '../../core/app_version.dart';
import '../../core/config.dart';
import '../../core/realtime.dart';
import '../../core/session.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import '../bots/bots_admin_screen.dart';
import '../lobby/settings_sheet.dart';

import 'watch_games_sheet.dart';

final myGamesProvider = FutureProvider.autoDispose<List<MyGame>>((ref) => ref.read(apiProvider).myGames(status: 'active'));

/// Главная: создать лобби, войти по коду, продолжить свои партии.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  final _code = TextEditingController();
  String _joinMode = 'player';
  Timer? _refresh;
  late final AppLifecycleListener _lifecycle;

  @override
  void initState() {
    super.initState();
    // Список партий сам обновляется: в походовой игре так видно, что настал ваш ход.
    _refresh = Timer.periodic(const Duration(seconds: 30), (_) => ref.invalidate(myGamesProvider));
    _lifecycle = AppLifecycleListener(onResume: () => ref.invalidate(myGamesProvider));
  }

  @override
  void dispose() {
    _refresh?.cancel();
    _lifecycle.dispose();
    _code.dispose();
    super.dispose();
  }

  Future<void> _create() async {
    final user = ref.read(sessionProvider).user!;
    final lobby = await runAction(context, () => ref.read(apiProvider).createLobby('Стол ${user.nickname}', const LobbySettings()));
    if (lobby != null && mounted) context.go('/lobby/${lobby.id}');
  }

  Future<void> _join({String? watchCode}) async {
    final code = (watchCode ?? _code.text).trim().toUpperCase();
    if (code.length != 6) return;
    final lobby = await runAction(context, () => ref.read(apiProvider).joinLobby(code,
        table: watchCode == null && _joinMode == 'table', spectator: watchCode != null || _joinMode == 'spectator'));
    if (lobby == null || !mounted) return;
    if (lobby.currentGameId != null && lobby.status == 'in_game') {
      context.go('/game/${lobby.currentGameId}');
    } else {
      context.go('/lobby/${lobby.id}');
    }
  }

  String get _greeting {
    final h = DateTime.now().hour;
    if (h >= 5 && h < 12) return 'Доброе утро,';
    if (h >= 12 && h < 18) return 'Добрый день,';
    if (h >= 18 && h < 23) return 'Добрый вечер,';
    return 'Доброй ночи,';
  }

  @override
  Widget build(BuildContext context) {
    final user = ref.watch(sessionProvider).user;
    if (user == null) return const SizedBox.shrink();
    final games = ref.watch(myGamesProvider);
    final isAdmin = ref.watch(isAdminProvider).valueOrNull == true;
    return Scaffold(
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(myGamesProvider),
          child: LayoutBuilder(builder: (context, box) {
            final actions = <Widget>[
              Align(alignment: Alignment.centerRight, child: IconButton(tooltip: 'Звук и музыка', icon: const Icon(Icons.volume_up_outlined), onPressed: () => SoundSettingsSheet.show(context))),
              Row(children: [
                GestureDetector(
                  onTap: () => context.push('/profile/${user.id}'),
                  child: Tooltip(
                    message: 'Профиль',
                    child: Avatar(nickname: user.nickname, color: user.avatarColor, photoId: user.avatarId, size: 48, highlight: true),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(_greeting, style: const TextStyle(fontSize: 13, color: AppColors.muted)),
                    Text(user.nickname.toUpperCase(), maxLines: 1, overflow: TextOverflow.ellipsis, style: heading(24, spacing: 1)),
                  ]),
                ),
                PopupMenuButton<String>(
                  icon: const Icon(Icons.more_horiz, color: AppColors.muted),
                  onSelected: (v) async {
                    if (v == 'presets') {
                      await SettingsSheet.show(context, const LobbySettings(), personal: true);
                    } else if (v == 'rules') {
                      context.push('/rules');
                    } else if (v == 'history') {
                      context.push('/history');
                    } else if (v == 'bots') {
                      context.push('/admin/bots');
                    } else if (v == 'leaderboard') {
                      context.push('/leaderboard');
                    } else if (v == 'profile') {
                      context.push('/profile/${user.id}');
                    } else if (v == 'logout') {
                      final confirmed = await showDialog<bool>(context: context, builder: (dialogContext) => AlertDialog(
                        title: const Text('Выйти из аккаунта?'),
                        content: const Text('Чтобы вернуться в этот аккаунт, нужен код с другого устройства, где вы уже вошли. Если это единственное устройство, сначала подключите второе через профиль.'),
                        actions: [
                          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Остаться')),
                          TextButton(onPressed: () => Navigator.pop(dialogContext, true), child: const Text('Выйти')),
                        ],
                      ));
                      if (confirmed != true || !context.mounted) return;
                      await runAction(context, () async {
                        await ref.read(apiProvider).logout();
                        await ref.read(realtimeProvider).disconnect();
                      });
                    }
                  },
                  itemBuilder: (_) => [
                    const PopupMenuItem(value: 'presets', child: Text('Мои пресеты настроек')),
                    if (isAdmin) const PopupMenuItem(value: 'bots', child: Text('Боты (кабинет)')),
                    const PopupMenuItem(value: 'history', child: Text('История партий')),
                    const PopupMenuItem(value: 'leaderboard', child: Text('Рейтинг игроков')),
                    const PopupMenuItem(value: 'rules', child: Text('Правила')),
                    const PopupMenuItem(value: 'profile', child: Text('Профиль и рейтинг')),
                    const PopupMenuItem(value: 'logout', child: Text('Выйти')),
                  ],
                ),
              ]),
              const _UpdateBanner(),
              const SizedBox(height: 20),
              FilledButton.icon(
                key: const Key('create-lobby'),
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(64),
                  alignment: Alignment.centerLeft,
                  padding: const EdgeInsets.symmetric(horizontal: 18),
                  shape: const RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(16))),
                  textStyle: heading(20, spacing: 2),
                ),
                onPressed: _create,
                icon: const Icon(Icons.add, size: 26),
                label: const Text('СОЗДАТЬ ИГРУ'),
              ),
              const SizedBox(height: 12),
              Row(children: [
                Expanded(
                  child: TextField(
                    key: const Key('lobby-code'),
                    controller: _code,
                    textCapitalization: TextCapitalization.characters,
                    maxLength: 6,
                    style: heading(18, spacing: 3),
                    decoration: const InputDecoration(hintText: 'Код: ABC234', counterText: ''),
                    onSubmitted: (_) => _join(),
                  ),
                ),
                const SizedBox(width: 10),
                OutlinedButton(
                  key: const Key('join'),
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size(0, 56),
                    foregroundColor: AppColors.amber,
                    side: const BorderSide(color: AppColors.amber),
                  ),
                  onPressed: _join,
                  child: const Text('ВОЙТИ'),
                ),
              ]),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                key: const Key('join-mode'),
                initialValue: _joinMode,
                decoration: const InputDecoration(labelText: 'Как подключиться'),
                items: const [
                  DropdownMenuItem(value: 'player', child: Text('Игрок')),
                  DropdownMenuItem(value: 'spectator', child: Text('Зритель')),
                  DropdownMenuItem(value: 'table', child: Text('Экран стола')),
                ],
                onChanged: (v) => setState(() => _joinMode = v ?? 'player'),
              ),
              if (_joinMode != 'player')
                const Padding(
                  padding: EdgeInsets.only(top: 8),
                  child: Text('Только публичная информация и общий чат. Можно войти и во время партии.',
                      style: TextStyle(fontSize: 12, color: AppColors.muted)),
                ),
              TextButton.icon(
                key: const Key('watch-games'),
                onPressed: () async {
                  final code = await showModalBottomSheet<String>(
                    context: context, isScrollControlled: true,
                    builder: (_) => const WatchGamesSheet(),
                  );
                  if (code != null && mounted) await _join(watchCode: code);
                },
                icon: const Icon(Icons.visibility_outlined),
                label: const Text('Смотреть идущие партии'),
              ),
            ];
            final gamesList = <Widget>[
              Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
                Expanded(child: Text('МОИ ПАРТИИ', style: heading(18, color: AppColors.ice, spacing: 2))),
                if (games.valueOrNull case final list? when list.isNotEmpty)
                  Text('${list.length} активн${list.length == 1 ? 'ая' : 'ые'}', style: const TextStyle(fontSize: 13, color: AppColors.muted)),
              ]),
              const SizedBox(height: 10),
              games.when(
                data: (list) => list.isEmpty
                    ? const Padding(
                        padding: EdgeInsets.symmetric(vertical: 16),
                        child: Text('Пока нет идущих партий', style: TextStyle(color: AppColors.muted)),
                      )
                    : Column(children: [for (final g in list) _GameCard(game: g)]),
                loading: () => const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator())),
                error: (e, _) => ErrorRetry(message: ApiError.from(e).message, onRetry: () => ref.invalidate(myGamesProvider)),
              ),
              const SizedBox(height: 24),
              Text(
                AppVersion.current.label,
                key: const Key('app-version'),
                textAlign: TextAlign.center,
                style: const TextStyle(fontSize: 12, color: AppColors.dim),
              ),
            ];
            // Компьютер: слева создание и вход, справа партии.
            if (box.maxWidth >= 1100) {
              final side = ((box.maxWidth - 1160) / 2).clamp(24.0, double.infinity);
              return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                SizedBox(width: side),
                SizedBox(
                  width: 440,
                  child: ListView(padding: const EdgeInsets.fromLTRB(0, 24, 0, 24), children: actions),
                ),
                const SizedBox(width: 40),
                Expanded(
                  child: ListView(key: const Key('home-games-column'), padding: const EdgeInsets.fromLTRB(0, 24, 0, 24), children: gamesList),
                ),
                SizedBox(width: side),
              ]);
            }
            return ListView(
              padding: pageInsets(context, max: 640, side: 20, top: 16, bottom: 24),
              children: [...actions, const SizedBox(height: 12), ...gamesList],
            );
          }),
        ),
      ),
    );
  }
}

/// «Вышла новая версия» — в Android-сборке, если на сервере APK новее установленного.
class _UpdateBanner extends ConsumerWidget {
  const _UpdateBanner();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final latest = ref.watch(latestAndroidVersionProvider).valueOrNull;
    if (!shouldOfferUpdate(AppVersion.current, latest)) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 16),
      child: Material(
        color: AppColors.surface,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14), side: const BorderSide(color: AppColors.amber)),
        child: InkWell(
          key: const Key('update-banner'),
          borderRadius: BorderRadius.circular(14),
          onTap: () => launchUrl(Uri.parse(AppConfig.downloadPage), mode: LaunchMode.externalApplication),
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Row(children: [
              const Icon(Icons.system_update, color: AppColors.amber),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  const Text('Вышла новая версия', style: TextStyle(fontWeight: FontWeight.w600)),
                  Text(latest!.label, style: const TextStyle(fontSize: 12, color: AppColors.muted)),
                ]),
              ),
              const Text('ОБНОВИТЬ', style: TextStyle(color: AppColors.amber, fontWeight: FontWeight.w700)),
            ]),
          ),
        ),
      ),
    );
  }
}

/// Карточка идущей партии; если ждут вашего хода — янтарная рамка и плашка «Ваш ход».
class _GameCard extends StatelessWidget {
  const _GameCard({required this.game});

  final MyGame game;

  @override
  Widget build(BuildContext context) {
    final g = game;
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Material(
        color: AppColors.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(color: g.yourTurn ? AppColors.amber : AppColors.border),
        ),
        child: InkWell(
          borderRadius: BorderRadius.circular(16),
          onTap: () => context.go('/game/${g.gameId}'),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
            child: Row(children: [
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(g.title ?? T.phase(g.phase), style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 16)),
                  const SizedBox(height: 4),
                  Text(
                    [
                      if (g.round > 0 && g.totalRounds > 0) 'Раунд ${g.round} из ${g.totalRounds}' else T.phase(g.phase),
                      if (g.players > 0) T.players(g.players),
                      if (g.startedAt != null) 'начата ${T.when(g.startedAt)}',
                    ].join(' · '),
                    style: const TextStyle(fontSize: 13, color: AppColors.muted),
                  ),
                  if (g.title != null)
                    Text(
                      '${T.phase(g.phase)} · ${g.yourTurn ? 'ваш ход' : 'ждём других'}',
                      style: TextStyle(fontSize: 12, color: g.yourTurn ? AppColors.amberLight : AppColors.dim),
                    ),
                ]),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                decoration: BoxDecoration(
                  color: g.yourTurn ? AppColors.amber : null,
                  border: g.yourTurn ? null : Border.all(color: AppColors.border),
                  borderRadius: BorderRadius.circular(99),
                ),
                child: Text(
                  g.yourTurn ? 'Ваш ход' : 'Ждём',
                  style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: g.yourTurn ? AppColors.onAmber : AppColors.muted),
                ),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}
