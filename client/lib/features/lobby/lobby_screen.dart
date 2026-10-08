import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/session.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import '../../widgets/connection_banner.dart';
import '../game/game_state.dart';
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

    final readyCount = players.where((p) => p.userId == lobby.hostUserId || p.isReady).length;
    Widget? bottom;
    if (isHost) {
      bottom = FilledButton(
        key: const Key('start-game'),
        style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56), textStyle: heading(18, spacing: 2)),
        onPressed: players.length >= 2 && allReady
            ? () => runAction(context, () async {
                  final gameId = await ref.read(apiProvider).startGame(lobby.id);
                  if (context.mounted) context.go('/game/$gameId');
                })
            : null,
        child: Text(allReady ? 'НАЧАТЬ ПАРТИЮ' : 'Ждём готовности игроков'),
      );
    } else if (mine != null && !mine.isTable) {
      bottom = mine.isReady
          ? OutlinedButton(
              key: const Key('ready'),
              style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(56)),
              onPressed: () => _apply((api) => api.setReady(lobby.id, false)),
              child: const Text('Не готов'),
            )
          : FilledButton(
              key: const Key('ready'),
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56), textStyle: heading(18, spacing: 2)),
              onPressed: () => _apply((api) => api.setReady(lobby.id, true)),
              child: const Text('Готов'),
            );
    }

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(tooltip: 'На главную', icon: const Icon(Icons.arrow_back), onPressed: () => context.go('/')),
        title: Text(lobby.title.toUpperCase(), maxLines: 1, overflow: TextOverflow.ellipsis),
        actions: [
          if (isHost)
            IconButton(
              tooltip: 'Настройки',
              icon: const Icon(Icons.tune),
              onPressed: () async {
                final s = await SettingsSheet.show(context, lobby.settings, inGame: lobby.status == 'in_game', players: players.length);
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
      bottomNavigationBar: bottom == null
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
                child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  _RolesPreview(lobby: lobby, players: players.length),
                  const SizedBox(height: 8),
                  bottom,
                ]),
              ),
            ),
      body: Column(children: [
        const ConnectionBanner(),
        Expanded(child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
        children: [
          Panel(
            padding: const EdgeInsets.all(16),
            child: Row(children: [
              const AppImage('letter', width: 72, height: 72, radius: 12),
              const SizedBox(width: 16),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  const Text('Код лобби', style: TextStyle(fontSize: 13, color: AppColors.muted)),
                  FittedBox(child: Text(lobby.code, style: heading(34, color: AppColors.amber, spacing: 6))),
                  const SizedBox(height: 4),
                  OutlinedButton.icon(
                    style: OutlinedButton.styleFrom(minimumSize: const Size(0, 36), padding: const EdgeInsets.symmetric(horizontal: 12)),
                    icon: const Icon(Icons.copy, size: 16),
                    label: const Text('Скопировать код', style: TextStyle(fontFamily: AppFonts.body, fontSize: 13, letterSpacing: 0)),
                    onPressed: () {
                      Clipboard.setData(ClipboardData(text: lobby.code));
                      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Код скопирован')));
                    },
                  ),
                ]),
              ),
            ]),
          ),
          const SizedBox(height: 12),
          _SettingsSummary(
            settings: lobby.settings,
            players: players.length,
            ghost: players.where((p) => p.userId == lobby.settings.ghostUserId).map((p) => p.nickname).firstOrNull,
          ),
          const SizedBox(height: 20),
          Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
            Expanded(child: Text('ИГРОКИ', style: heading(18, color: AppColors.ice, spacing: 2))),
            Text('${players.length} из 12 · готовы $readyCount', style: const TextStyle(fontSize: 13, color: AppColors.muted)),
          ]),
          const SizedBox(height: 8),
          for (final p in players)
            Padding(
              padding: const EdgeInsets.only(bottom: 6),
              child: Panel(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                child: Row(children: [
                  p.isBot
                      ? CircleAvatar(radius: 20, backgroundColor: colorFromHex(p.avatarColor), child: const Icon(Icons.smart_toy_outlined, color: Colors.white))
                      : Avatar(nickname: p.nickname, color: p.avatarColor, highlight: p.userId == me.id),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text.rich(TextSpan(children: [
                      TextSpan(text: p.nickname),
                      if (p.userId == me.id) const TextSpan(text: ' · вы', style: TextStyle(color: AppColors.amber, fontSize: 13)),
                      if (p.userId == lobby.hostUserId) const TextSpan(text: ' · хост', style: TextStyle(color: AppColors.ice, fontSize: 13)),
                      if (lobby.settings.ghostUserId == p.userId)
                        const TextSpan(text: ' · Призрак', style: TextStyle(color: AppColors.ice, fontSize: 13, fontWeight: FontWeight.w600)),
                    ])),
                  ),
                  if (p.userId != lobby.hostUserId)
                    Text(p.isReady ? 'готов' : 'ждём', style: TextStyle(fontSize: 13, color: p.isReady ? AppColors.believed : AppColors.dim)),
                  if (p.isReady || p.userId == lobby.hostUserId) ...[
                    const SizedBox(width: 6),
                    const Icon(Icons.check_circle, color: AppColors.believed, size: 20),
                  ],
                  if (isHost && lobby.status == 'open')
                    IconButton(
                      key: Key('ghost-${p.userId}'),
                      tooltip: lobby.settings.ghostUserId == p.userId ? 'Призрак по жребию' : 'Сделать Призраком',
                      onPressed: () => _apply((api) => api.saveSettings(
                            lobby.id,
                            lobby.settings.ghostUserId == p.userId
                                ? lobby.settings.copyWith(clearGhost: true)
                                : lobby.settings.copyWith(ghostUserId: p.userId),
                          )),
                      icon: Opacity(
                        opacity: lobby.settings.ghostUserId == p.userId ? 1 : 0.35,
                        child: const AppImage('role_ghost', width: 22, height: 30, radius: 3),
                      ),
                    ),
                  if (isHost && p.userId != me.id)
                    IconButton(
                      tooltip: 'Исключить',
                      icon: const Icon(Icons.person_remove_outlined, size: 20),
                      onPressed: () => runAction(context, () => ref.read(apiProvider).kick(lobby.id, p.userId)),
                    ),
                ]),
              ),
            ),
          if (isHost && players.length < 12 && lobby.status == 'open')
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: const Key('add-bot'),
                onPressed: () => _apply((api) => api.addBot(lobby.id)),
                icon: const Icon(Icons.smart_toy_outlined),
                label: const Text('Добавить бота'),
              ),
            ),
          const SizedBox(height: 8),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            decoration: BoxDecoration(borderRadius: BorderRadius.circular(14), border: Border.all(color: AppColors.border)),
            child: Row(children: [
              const Icon(Icons.tv, color: AppColors.muted, size: 20),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  tables.isEmpty ? 'Экран стола: не подключён' : 'Экраны стола: ${tables.map((t) => t.nickname).join(', ')}',
                  style: const TextStyle(fontSize: 13, color: AppColors.muted),
                ),
              ),
            ]),
          ),
          if (bottom == null) ...[
            const SizedBox(height: 12),
            _RolesPreview(lobby: lobby, players: players.length),
          ],
        ],
      )),
      ]),
    );
  }
}

String _setName(String code) => switch (code) {
      'original' => 'Оригинальный',
      'mailbox' => 'Почтовый ящик',
      'ritual' => 'Тайный ритуал',
      'mirror' => 'Зеркало истины',
      _ => code,
    };

class _SettingsSummary extends StatelessWidget {
  const _SettingsSummary({required this.settings, required this.players, this.ghost});

  final LobbySettings settings;
  final int players;

  /// Ник игрока, которому хост заранее отдал роль Призрака.
  final String? ghost;

  @override
  Widget build(BuildContext context) {
    final r = settings.roles;
    final parts = [
      settings.useSecretRow ? '4 ряда (с «Тайной»)' : '3 ряда',
      '${settings.columns} карт в ряду',
      'раундов: ${settings.rounds ?? (players >= 2 ? defaultRounds(players) : 'по правилам')}',
      r.killerEnabled ? 'с Убийцей' : 'кооператив',
      settings.discussion == 'Radio' ? 'рация' : 'свободное обсуждение',
      settings.tempo == 'live' ? 'живая' : 'походовая (${settings.turnHours} ч)',
      'Призрак: ${ghost ?? 'по жребию'}',
      settings.cardSets.length >= 4 ? 'все наборы карт' : 'наборы: ${settings.cardSets.map(_setName).join(', ')}',
    ];
    return Wrap(spacing: 6, runSpacing: 6, children: [
      for (final p in parts)
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
          decoration: BoxDecoration(color: AppColors.surface2, borderRadius: BorderRadius.circular(99)),
          child: Text(p, style: const TextStyle(fontSize: 12, color: AppColors.muted)),
        ),
    ]);
  }
}

/// Состав ролей для текущего числа игроков и настроек — тот же расчёт, что при старте на сервере.
class _RolesPreview extends ConsumerWidget {
  const _RolesPreview({required this.lobby, required this.players});

  final Lobby lobby;
  final int players;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (players < 2) return const SizedBox.shrink();
    final preview = ref.watch(rolesPreviewProvider((players: players, roles: lobby.settings.roles)));
    return preview.when(
      loading: () => const SizedBox(height: 24),
      error: (e, _) => Padding(
        padding: const EdgeInsets.only(top: 6),
        child: Text(ApiError.from(e).message, style: const TextStyle(color: AppColors.amber)),
      ),
      data: (p) => Padding(
        padding: const EdgeInsets.only(top: 6),
        child: Text(
          '${p.cooperative ? 'Кооператив' : 'С Убийцей'} · раундов: ${lobby.settings.rounds ?? p.rounds} · ${_describe(p.roles)}',
          style: const TextStyle(fontSize: 12, color: AppColors.muted),
          textAlign: TextAlign.center,
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
