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

  Future<void> _openSettings(Lobby lobby, int players) async {
    final s = await SettingsSheet.show(context, lobby.settings, inGame: lobby.status == 'in_game', players: players);
    if (s != null) await _apply((api) => api.saveSettings(lobby.id, s));
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

    final landscape = isCompactLandscape(context);
    final headItems = <Widget>[
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
            members: players,
            inGame: lobby.status == 'in_game',
            onChange: isHost ? (next) => _apply((api) => api.saveSettings(lobby.id, next)) : null,
            onOpenSheet: isHost ? () => _openSettings(lobby, players.length) : null,
          ),
    ];
    final playerItems = <Widget>[
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
    ];

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(tooltip: 'На главную', icon: const Icon(Icons.arrow_back), onPressed: () => context.go('/')),
        title: Text(lobby.title.toUpperCase(), maxLines: 1, overflow: TextOverflow.ellipsis),
        actions: [
          if (isHost)
            IconButton(
              tooltip: 'Настройки',
              icon: const Icon(Icons.tune),
              onPressed: () => _openSettings(lobby, players.length),
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
      bottomNavigationBar: bottom == null || landscape
          ? null
          : SafeArea(
              child: Padding(
                padding: pageInsets(context, bottom: 12),
                child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  _RolesPreview(lobby: lobby, players: players.length),
                  const SizedBox(height: 8),
                  bottom,
                ]),
              ),
            ),
      body: Column(children: [
        const ConnectionBanner(),
        Expanded(
          child: landscape
              // Телефон боком: слева код, настройки и кнопка, справа игроки в две колонки.
              ? Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  SizedBox(
                    width: (MediaQuery.sizeOf(context).width * 0.44).clamp(280.0, 420.0),
                    child: ListView(
                      key: const Key('lobby-left'),
                      padding: const EdgeInsets.fromLTRB(16, 8, 8, 16),
                      children: [
                        ...headItems,
                        if (bottom != null) ...[
                          const SizedBox(height: 12),
                          _RolesPreview(lobby: lobby, players: players.length),
                          const SizedBox(height: 8),
                          bottom,
                        ],
                      ],
                    ),
                  ),
                  Expanded(
                    child: ListView(
                      key: const Key('lobby-right'),
                      padding: const EdgeInsets.fromLTRB(8, 0, 16, 16),
                      children: playerItems,
                    ),
                  ),
                ])
              : ListView(padding: pageInsets(context), children: [...headItems, ...playerItems]),
        ),
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

/// Пункт меню у метки настройки: подпись и что станет с настройками.
typedef _Choice = (String label, LobbySettings Function(LobbySettings s));

/// Метки текущих настроек. У хоста метка — кнопка: нажал и выбрал вариант прямо здесь.
class _SettingsSummary extends StatelessWidget {
  const _SettingsSummary({
    required this.settings,
    required this.players,
    required this.members,
    this.onChange,
    this.onOpenSheet,
    this.inGame = false,
  });

  final LobbySettings settings;
  final int players;

  /// Игроки лобби — для выбора Призрака.
  final List<LobbyMember> members;

  /// null — не хост: метки только для чтения.
  final ValueChanged<LobbySettings>? onChange;

  /// Полные настройки (наборы карт, таймеры) — лист настроек.
  final VoidCallback? onOpenSheet;

  /// Партия идёт: менять можно только раунды, обсуждение и темп.
  final bool inGame;

  @override
  Widget build(BuildContext context) {
    final r = settings.roles;
    final ghost = members.where((p) => p.userId == settings.ghostUserId).map((p) => p.nickname).firstOrNull;
    final chips = <Widget>[
      _chip(context, 'rows', settings.useSecretRow ? '4 ряда (с «Тайной»)' : '3 ряда', rules: true, choices: [
        ('3 ряда: Мотив, Место, Способ', (s) => s.copyWith(useSecretRow: false)),
        ('4 ряда: + «Тайна»', (s) => s.copyWith(useSecretRow: true)),
      ]),
      _chip(context, 'columns', '${settings.columns} карт в ряду', rules: true, choices: [
        for (var c = 4; c <= 7; c++) ('$c карт в ряду', (s) => s.copyWith(columns: c)),
      ]),
      _chip(context, 'rounds', 'раундов: ${settings.rounds ?? (players >= 2 ? defaultRounds(players) : 'по правилам')}', choices: [
        ('По правилам${players >= 2 ? ' (${defaultRounds(players)})' : ''}', (s) => s.copyWith(clearRounds: true)),
        for (var n = 1; n <= 5; n++) ('$n', (s) => s.copyWith(rounds: n)),
      ]),
      _chip(context, 'killer', r.killerEnabled ? 'с Убийцей' : 'кооператив', rules: true, choices: [
        ('С Убийцей', (s) => s.copyWith(roles: s.roles.copyWith(killerEnabled: true))),
        ('Кооператив — без Убийцы', (s) => s.copyWith(roles: s.roles.copyWith(killerEnabled: false))),
      ]),
      _chip(context, 'discussion', settings.discussion == 'Radio' ? 'рация' : 'свободное обсуждение', choices: [
        ('Рация: говорят по очереди', (s) => s.copyWith(discussion: 'Radio')),
        ('Свободное обсуждение', (s) => s.copyWith(discussion: 'FreeChat')),
      ]),
      _chip(context, 'tempo', settings.tempo == 'live' ? 'живая' : 'походовая (${settings.turnHours} ч)', choices: [
        ('Живая — таймеры в секундах', (s) => s.copyWith(tempo: 'live')),
        for (final h in const [6, 12, 24, 48]) ('Походовая — $h ч на ход', (s) => s.copyWith(tempo: 'turn', turnHours: h)),
      ]),
      _chip(context, 'ghost', 'Призрак: ${ghost ?? 'по жребию'}', rules: true, choices: [
        ('По жребию', (s) => s.copyWith(clearGhost: true)),
        for (final m in members) (m.nickname, (s) => s.copyWith(ghostUserId: m.userId)),
      ]),
      _chip(context, 'sets', settings.cardSets.length >= 4 ? 'все наборы карт' : 'наборы: ${settings.cardSets.map(_setName).join(', ')}',
          rules: true, opensSheet: true),
    ];
    return Wrap(spacing: 6, runSpacing: 6, children: chips);
  }

  Widget _chip(BuildContext context, String id, String text, {List<_Choice> choices = const [], bool rules = false, bool opensSheet = false}) {
    final editable = onChange != null && !(inGame && rules);
    final body = Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(
        color: AppColors.surface2,
        borderRadius: BorderRadius.circular(99),
        border: editable ? Border.all(color: AppColors.border) : null,
      ),
      child: Row(mainAxisSize: MainAxisSize.min, children: [
        Text(text, style: TextStyle(fontSize: 12, color: editable ? AppColors.text : AppColors.muted)),
        if (editable) ...[
          const SizedBox(width: 2),
          const Icon(Icons.arrow_drop_down, size: 16, color: AppColors.muted),
        ],
      ]),
    );
    if (!editable) return KeyedSubtree(key: Key('chip-$id'), child: body);
    if (opensSheet) {
      return InkWell(key: Key('chip-$id'), borderRadius: BorderRadius.circular(99), onTap: onOpenSheet, child: body);
    }
    return PopupMenuButton<int>(
      key: Key('chip-$id'),
      tooltip: 'Изменить',
      onSelected: (i) => onChange!(choices[i].$2(settings)),
      itemBuilder: (_) => [for (var i = 0; i < choices.length; i++) PopupMenuItem(value: i, child: Text(choices[i].$1))],
      child: body,
    );
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
