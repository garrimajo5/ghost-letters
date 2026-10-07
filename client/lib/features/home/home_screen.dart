import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/session.dart';
import '../../core/texts.dart';
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

  @override
  void dispose() {
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

  @override
  Widget build(BuildContext context) {
    final user = ref.watch(sessionProvider).user;
    if (user == null) return const SizedBox.shrink();
    final games = ref.watch(myGamesProvider);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Письма призрака'),
        actions: [
          IconButton(
            tooltip: 'Профиль',
            onPressed: () => context.push('/profile/${user.id}'),
            icon: Avatar(nickname: user.nickname, color: user.avatarColor, size: 32),
          ),
          PopupMenuButton<String>(
            onSelected: (v) async {
              if (v == 'logout') {
                await ref.read(realtimeProvider).disconnect();
                ref.read(sessionProvider.notifier).signOut();
              }
            },
            itemBuilder: (_) => const [PopupMenuItem(value: 'logout', child: Text('Выйти'))],
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(myGamesProvider),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            FilledButton.icon(
              key: const Key('create-lobby'),
              onPressed: _create,
              icon: const Icon(Icons.add),
              label: const Padding(padding: EdgeInsets.all(12), child: Text('Создать лобби')),
            ),
            const SizedBox(height: 24),
            Text('Войти по коду', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            Row(children: [
              Expanded(
                child: TextField(
                  key: const Key('lobby-code'),
                  controller: _code,
                  textCapitalization: TextCapitalization.characters,
                  maxLength: 6,
                  decoration: const InputDecoration(hintText: 'ABC234', counterText: ''),
                  onSubmitted: (_) => _join(),
                ),
              ),
              const SizedBox(width: 8),
              FilledButton.tonal(onPressed: _join, child: const Text('Войти')),
            ]),
            CheckboxListTile(
              contentPadding: EdgeInsets.zero,
              value: _asTable,
              onChanged: (v) => setState(() => _asTable = v ?? false),
              title: const Text('Как экран стола'),
              subtitle: const Text('Общий экран для игры за одним столом — без тайной информации'),
            ),
            const SizedBox(height: 16),
            Text('Мои партии', style: Theme.of(context).textTheme.titleMedium),
            games.when(
              data: (list) => list.isEmpty
                  ? const Padding(padding: EdgeInsets.all(16), child: Text('Пока нет идущих партий'))
                  : Column(children: [
                      for (final g in list)
                        ListTile(
                          leading: Icon(g.yourTurn ? Icons.notifications_active : Icons.hourglass_empty,
                              color: g.yourTurn ? Theme.of(context).colorScheme.primary : null),
                          title: Text(T.phase(g.phase)),
                          subtitle: Text(g.yourTurn ? 'Ваш ход' : 'Ждём других игроков'),
                          trailing: const Icon(Icons.chevron_right),
                          onTap: () => context.go('/game/${g.gameId}'),
                        ),
                    ]),
              loading: () => const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator())),
              error: (e, _) => Padding(padding: const EdgeInsets.all(16), child: Text(ApiError.from(e).message)),
            ),
          ],
        ),
      ),
    );
  }
}
