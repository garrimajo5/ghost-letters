import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/session.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

final myGamesProvider = FutureProvider.autoDispose<List<MyGame>>((ref) => ref.read(apiProvider).myGames(status: 'active'));

/// Главная: создать лобби, войти по коду, продолжить свои партии.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  final _code = TextEditingController();
  bool _asTable = false;
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

  Future<void> _join() async {
    final code = _code.text.trim().toUpperCase();
    if (code.length != 6) return;
    final lobby = await runAction(context, () => ref.read(apiProvider).joinLobby(code, table: _asTable));
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
    return Scaffold(
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(myGamesProvider),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 24),
            children: [
              Row(children: [
                GestureDetector(
                  onTap: () => context.push('/profile/${user.id}'),
                  child: Tooltip(
                    message: 'Профиль',
                    child: Avatar(nickname: user.nickname, color: user.avatarColor, size: 48, highlight: true),
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
                    if (v == 'rules') {
                      context.push('/rules');
                    } else if (v == 'profile') {
                      context.push('/profile/${user.id}');
                    } else if (v == 'logout') {
                      await ref.read(realtimeProvider).disconnect();
                      ref.read(sessionProvider.notifier).signOut();
                    }
                  },
                  itemBuilder: (_) => const [
                    PopupMenuItem(value: 'rules', child: Text('Правила')),
                    PopupMenuItem(value: 'profile', child: Text('Профиль и рейтинг')),
                    PopupMenuItem(value: 'logout', child: Text('Выйти')),
                  ],
                ),
              ]),
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
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                value: _asTable,
                onChanged: (v) => setState(() => _asTable = v ?? false),
                title: const Text('Как экран стола'),
                subtitle: const Text('Общий экран для игры за одним столом — без тайной информации'),
              ),
              const SizedBox(height: 12),
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
                error: (e, _) => Padding(padding: const EdgeInsets.all(16), child: Text(ApiError.from(e).message)),
              ),
            ],
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
                  Text(T.phase(g.phase), style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 16)),
                  const SizedBox(height: 4),
                  Text(g.yourTurn ? 'Ваш ход' : 'Ждём других игроков', style: const TextStyle(fontSize: 13, color: AppColors.muted)),
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
