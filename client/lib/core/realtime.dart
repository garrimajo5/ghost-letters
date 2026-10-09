import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../models/models.dart';
import 'api.dart';
import 'config.dart';

/// Подключение к /hubs/play. Ходы и чат идут через REST, хаб — для подписок и обновлений.
class Realtime {
  Realtime(this._ref);

  final Ref _ref;
  HubConnection? _hub;
  final _lobbies = <String>{};
  final _games = <String>{};

  final _lobbyUpdates = StreamController<Lobby>.broadcast();
  final _gameStarted = StreamController<({String lobbyId, String gameId})>.broadcast();
  final _views = StreamController<({GameView view, DateTime? deadline})>.broadcast();
  final _snapshots = StreamController<GameSnapshot>.broadcast();
  final _chat = StreamController<ChatMessage>.broadcast();
  final _connected = StreamController<bool>.broadcast();

  Stream<Lobby> get lobbyUpdates => _lobbyUpdates.stream;

  Stream<({String lobbyId, String gameId})> get gameStarted => _gameStarted.stream;

  Stream<({GameView view, DateTime? deadline})> get views => _views.stream;

  /// Полный снимок при восстановлении подписки, включая состав игроков.
  Stream<GameSnapshot> get snapshots => _snapshots.stream;

  Stream<ChatMessage> get chat => _chat.stream;

  Stream<bool> get connected => _connected.stream;

  Future<HubConnection>? _connecting;
  Timer? _restart;
  int _attempt = 0;
  bool _stopped = false;

  /// Готовое подключение; одновременные вызовы ждут одно и то же.
  Future<HubConnection> _connection() {
    final hub = _hub;
    if (hub != null && hub.state == HubConnectionState.Connected) return Future.value(hub);
    return _connecting ??= _connect().whenComplete(() => _connecting = null);
  }

  Future<HubConnection> _connect() async {
    _stopped = false;
    final old = _hub;
    _hub = null;
    if (old != null && old.state != HubConnectionState.Disconnected) {
      await old.stop();
    }

    final hub = HubConnectionBuilder()
        .withUrl(
          AppConfig.hub,
          options: HttpConnectionOptions(
            // Токен живёт 15 минут: при каждом (пере)подключении берём свежий.
            accessTokenFactory: () => _ref.read(apiProvider).freshAccessToken(),
          ),
        )
        .withAutomaticReconnect()
        .build();

    hub.on('LobbyUpdated', (args) {
      final j = _first(args);
      if (j != null) _lobbyUpdates.add(Lobby.fromJson(j));
    });
    hub.on('GameStarted', (args) {
      final j = _first(args);
      if (j != null) _gameStarted.add((lobbyId: j['lobbyId'] as String, gameId: j['gameId'] as String));
    });
    hub.on('GameView', (args) {
      final j = _first(args);
      if (j == null) return;
      _views.add((
        view: GameView.fromJson(Map<String, dynamic>.from(j['view'] as Map)),
        deadline: j['deadline'] is String ? DateTime.tryParse(j['deadline'] as String) : null,
      ));
    });
    hub.on('ChatMessage', (args) {
      final j = _first(args);
      if (j != null) _chat.add(ChatMessage.fromJson(j));
    });
    hub.onreconnected(({connectionId}) async {
      _connected.add(true);
      await resync();
    });
    hub.onreconnecting(({error}) => _connected.add(false));
    // Автоматические попытки кончились — пересоздаём подключение сами, пока есть подписки.
    hub.onclose(({error}) {
      _connected.add(false);
      if (!_stopped && identical(_hub, hub)) _scheduleRestart();
    });

    await hub.start();
    _hub = hub;
    _attempt = 0;
    _connected.add(true);
    return hub;
  }

  void _scheduleRestart() {
    if (_lobbies.isEmpty && _games.isEmpty) return;
    _restart?.cancel();
    final delay = Duration(seconds: [2, 5, 10, 30][_attempt.clamp(0, 3)]);
    _attempt++;
    _restart = Timer(delay, () async {
      try {
        await resync();
      } catch (_) {
        if (!_stopped) _scheduleRestart();
      }
    });
  }

  /// Подписаться заново и разослать свежие снимки — например, после возврата приложения из фона.
  Future<void>? _resyncing;

  Future<void> resync() => _resyncing ??= _resync().whenComplete(() => _resyncing = null);

  Future<void> _resync() async {
    if (_lobbies.isEmpty && _games.isEmpty) return;
    await _resubscribe(await _connection());
  }

  Future<void> _resubscribe(HubConnection hub) async {
    for (final id in _lobbies.toList()) {
      final r = await hub.invoke('SubscribeLobby', args: <Object>[id]);
      if (r is Map) _lobbyUpdates.add(Lobby.fromJson(Map<String, dynamic>.from(r)));
    }
    for (final id in _games.toList()) {
      final r = await hub.invoke('SubscribeGame', args: <Object>[id]);
      if (r is Map) {
        final snap = GameSnapshot.fromJson(Map<String, dynamic>.from(r));
        _snapshots.add(snap);
      }
    }
  }

  Future<Lobby> subscribeLobby(String lobbyId) async {
    final hub = await _connection();
    _lobbies.add(lobbyId);
    final r = await hub.invoke('SubscribeLobby', args: <Object>[lobbyId]);
    return Lobby.fromJson(Map<String, dynamic>.from(r! as Map));
  }

  Future<void> unsubscribeLobby(String lobbyId) async {
    _lobbies.remove(lobbyId);
    final hub = _hub;
    if (hub != null && hub.state == HubConnectionState.Connected) {
      await hub.invoke('UnsubscribeLobby', args: <Object>[lobbyId]);
    }
  }

  Future<GameSnapshot> subscribeGame(String gameId) async {
    final hub = await _connection();
    _games.add(gameId);
    final r = await hub.invoke('SubscribeGame', args: <Object>[gameId]);
    return GameSnapshot.fromJson(Map<String, dynamic>.from(r! as Map));
  }

  void forgetGame(String gameId) => _games.remove(gameId);

  Future<void> disconnect() async {
    _stopped = true;
    _restart?.cancel();
    _lobbies.clear();
    _games.clear();
    await _hub?.stop();
    _hub = null;
  }

  static Map<String, dynamic>? _first(List<Object?>? args) {
    if (args == null || args.isEmpty || args.first is! Map) return null;
    return Map<String, dynamic>.from(args.first! as Map);
  }
}

final realtimeProvider = Provider<Realtime>((ref) {
  final realtime = Realtime(ref);
  ref.onDispose(realtime.disconnect);
  return realtime;
});
