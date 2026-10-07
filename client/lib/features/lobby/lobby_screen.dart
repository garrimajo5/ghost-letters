import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/session.dart';
import '../../core/texts.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'settings_sheet.dart';

/// Лобби: код для друзей, участники, готовность, настройки и старт (хост).
class LobbyScreen extends ConsumerStatefulWidget {
  const LobbyScreen({super.key, required this.lobbyId});

  final String lobbyId;

  @override
  ConsumerState<LobbyScreen> createState() => _LobbyScreenState();
}

class _LobbyScreenState extends ConsumerState<LobbyScreen> {
  Lobby? _lobby;
  String? _error;
  final _subs = <StreamSubscription<Object?>>[];
  late final Realtime _realtime;

  @override
  void initState() {
    super.initState();
    final realtime = _realtime = ref.read(realtimeProvider);
    _subs.add(realtime.lobbyUpdates.listen((l) {
      if (l.id == widget.lobbyId && mounted) setState(() => _lobby = l);
    }));
    _subs.add(realtime.gameStarted.listen((e) {
      if (e.lobbyId == widget.lobbyId && mounted) context.go('/game/${e.gameId}');
    }));
    _load();
  }

  Future<void> _load() async {
    try {
      final lobby = await _realtime.subscribeLobby(widget.lobbyId);
      if (!mounted) return;
      setState(() => _lobby = lobby);
      if (lobby.status == 'in_game' && lobby.currentGameId != null) context.go('/game/${lobby.currentGameId}');
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    }
  }

  @override
  void dispose() {
    for (final s in _subs) {
      s.cancel();
    }
    _realtime.unsubscribeLobby(widget.lobbyId);
    super.dispose();
  }

  Future<void> _apply(Future<Lobby> Function(Api api) action) async {
    final lobby = await runAction(context, () => action(ref.read(apiProvider)));
    if (lobby != null && mounted) setState(() => _lobby = lobby);
  }

  @override
  Widget build(BuildContext context) {
    final lobby = _lobby;
    final me = ref.watch(sessionProvider).user;
    if (lobby == null || me == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Лобби')),
        body: Center(
          child: _error == null
              ? const CircularProgressIndicator()
              : Column(mainAxisSize: MainAxisSize.min, children: [
                  Padding(padding: const EdgeInsets.all(16), child: Text(_error!, textAlign: TextAlign.center)),
                  FilledButton.tonal(
                    onPressed: () {
                      setState(() => _error = null);
                      _load();
                    },
                    child: const Text('Повторить'),
                  ),
                ]),
        ),
      );
    }

    final isHost = lobby.hostUserId == me.id;
    final mine = lobby.members.where((m) => m.userId == me.id).firstOrNull;
    final players = lobby.players;
    final tables = lobby.members.where((m) => m.isTable).toList();
    final allReady = players.every((p) => p.userId == lobby.hostUserId || p.isReady);

    return Scaffold(
      appBar: AppBar(
        title: Text(lobby.title),
        actions: [
          if (isHost)
            IconButton(
              tooltip: 'Настройки',
              icon: const Icon(Icons.tune),
              onPressed: () async {
                final s = await SettingsSheet.show(context, lobby.settings, inGame: lobby.status == 'in_game');
                if (s != null) await _apply((api) => api.saveSettings(lobby.id, s));
              },
            ),
          IconButton(
            tooltip: 'Выйти из лобби',
            icon: const Icon(Icons.logout),
            onPressed: () async {
              await runAction(context, () => ref.read(apiProvider).leaveLobby(lobby.id));
              if (context.mounted) context.go('/');
            },
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: ListTile(
              title: const Text('Код для друзей'),
              subtitle: Text(lobby.code, style: Theme.of(context).textTheme.headlineMedium?.copyWith(letterSpacing: 6)),
              trailing: IconButton(
                icon: const Icon(Icons.copy),
                onPressed: () {
                  Clipboard.setData(ClipboardData(text: lobby.code));
                  ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Код скопирован')));
                },
              ),
            ),
          ),
          const SizedBox(height: 8),
          _SettingsSummary(settings: lobby.settings, players: players.length),
          _RolesPreview(lobby: lobby, players: players.length),
          const SizedBox(height: 16),
          Text('Игроки (${players.length}/12)', style: Theme.of(context).textTheme.titleMedium),
          for (final p in players)
            ListTile(
              leading: Avatar(nickname: p.nickname, color: p.avatarColor),
              title: Text(p.nickname + (p.userId == me.id ? ' (вы)' : '')),
              subtitle: Text(p.userId == lobby.hostUserId ? 'Хост' : (p.isReady ? 'Готов' : 'Не готов')),
              trailing: isHost && p.userId != me.id
                  ? IconButton(
                      tooltip: 'Исключить',
                      icon: const Icon(Icons.person_remove_outlined),
                      onPressed: () => runAction(context, () => ref.read(apiProvider).kick(lobby.id, p.userId)),
                    )
                  : (p.isReady || p.userId == lobby.hostUserId ? const Icon(Icons.check_circle, color: Colors.green) : null),
            ),
          if (tables.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text('Экраны стола: ${tables.map((t) => t.nickname).join(', ')}'),
          ],
          const SizedBox(height: 24),
          if (isHost)
            FilledButton(
              key: const Key('start-game'),
              onPressed: players.length >= 2 && allReady
                  ? () => runAction(context, () async {
                        final gameId = await ref.read(apiProvider).startGame(lobby.id);
                        if (context.mounted) context.go('/game/$gameId');
                      })
                  : null,
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Text(allReady ? 'Начать партию' : 'Ждём готовности игроков'),
              ),
            )
          else if (mine != null && !mine.isTable)
            FilledButton.tonal(
              key: const Key('ready'),
              onPressed: () => _apply((api) => api.setReady(lobby.id, !mine.isReady)),
              child: Padding(padding: const EdgeInsets.all(12), child: Text(mine.isReady ? 'Не готов' : 'Готов')),
            ),
        ],
      ),
    );
  }
}

class _SettingsSummary extends StatelessWidget {
  const _SettingsSummary({required this.settings, required this.players});

  final LobbySettings settings;
  final int players;

  @override
  Widget build(BuildContext context) {
    final r = settings.roles;
    final parts = [
      settings.useSecretRow ? '4 ряда (с «Тайной»)' : '3 ряда',
      '${settings.columns} карт в ряду',
      settings.rounds == null ? 'раунды по правилам' : 'раундов: ${settings.rounds}',
      r.killerEnabled ? 'с Убийцей' : 'кооператив',
      settings.discussion == 'Radio' ? 'рация' : 'свободное обсуждение',
      settings.tempo == 'live' ? 'живая' : 'походовая (${settings.turnHours} ч)',
    ];
    return Text(parts.join(' · '), style: Theme.of(context).textTheme.bodySmall);
  }
}

/// Состав ролей для текущего числа игроков и настроек — тот же расчёт, что при старте на сервере.
class _RolesPreview extends ConsumerWidget {
  const _RolesPreview({required this.lobby, required this.players});

  final Lobby lobby;
  final int players;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final preview = ref.watch(rolesPreviewProvider((players: players, roles: lobby.settings.roles)));
    return preview.when(
      loading: () => const SizedBox(height: 24),
      error: (e, _) => Padding(
        padding: const EdgeInsets.only(top: 6),
        child: Text(ApiError.from(e).message, style: const TextStyle(color: Colors.amber)),
      ),
      data: (p) => Padding(
        padding: const EdgeInsets.only(top: 6),
        child: Text(
          '${p.cooperative ? 'Кооператив' : 'С Убийцей'} · раундов: ${lobby.settings.rounds ?? p.rounds} · ${_describe(p.roles)}',
          style: Theme.of(context).textTheme.bodySmall,
        ),
      ),
    );
  }

  static String _describe(List<String> roles) {
    final counts = <String, int>{};
    for (final r in roles) {
      counts[r] = (counts[r] ?? 0) + 1;
    }
    return counts.entries.map((e) => e.value > 1 ? '${T.role(e.key)} ×${e.value}' : T.role(e.key)).join(', ');
  }
}
