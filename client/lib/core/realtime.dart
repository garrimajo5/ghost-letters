import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../models/models.dart';
import 'config.dart';
import 'session.dart';

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
  final _chat = StreamController<ChatMessage>.broadcast();
  final _connected = StreamController<bool>.broadcast();

  Stream<Lobby> get lobbyUpdates => _lobbyUpdates.stream;

  Stream<({String lobbyId, String gameId})> get gameStarted => _gameStarted.stream;

  Stream<({GameView view, DateTime? deadline})> get views => _views.stream;

  Stream<ChatMessage> get chat => _chat.stream;

  Stream<bool> get connected => _connected.stream;

  Future<HubConnection> _connection() async {
    final existing = _hub;
    if (existing != null && existing.state == HubConnectionState.Connected) return existing;
    if (existing != null && existing.state != HubConnectionState.Disconnected) {
      await existing.stop();
    }

    final hub = HubConnectionBuilder()
        .withUrl(
          AppConfig.hub,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => _ref.read(sessionProvider).accessToken ?? '',
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
      for (final id in _lobbies) {
        await hub.invoke('SubscribeLobby', args: <Object>[id]);
      }
      for (final id in _games) {
        await hub.invoke('SubscribeGame', args: <Object>[id]);
      }
    });
    hub.onreconnecting(({error}) => _connected.add(false));

    await hub.start();
    _hub = hub;
    _connected.add(true);
    return hub;
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
