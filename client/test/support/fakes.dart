import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:ghost_letters/app.dart';
import 'package:ghost_letters/core/api.dart';
import 'package:ghost_letters/core/realtime.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Запись вызова сервера: имя метода и аргументы.
typedef Call = (String, List<Object?>);

/// API без сети: отдаёт заготовленные ответы и запоминает вызовы.
class FakeApi extends Api {
  FakeApi(super.ref);

  final calls = <Call>[];
  Lobby? lobbyResult;
  GameSnapshot? snapshotResult;
  List<MyGame> games = const [];
  ApiError? failWith;

  T _record<T>(String name, List<Object?> args, T Function() result) {
    calls.add((name, args));
    final error = failWith;
    if (error != null) throw error;
    return result();
  }

  Iterable<Call> named(String name) => calls.where((c) => c.$1 == name);

  @override
  Future<List<MyGame>> myGames({String? status}) async => _record('myGames', [status], () => games);

  @override
  Future<Lobby> createLobby(String title, LobbySettings settings) async => _record('createLobby', [title], () => lobbyResult!);

  @override
  Future<Lobby> joinLobby(String code, {bool table = false}) async => _record('joinLobby', [code, table], () => lobbyResult!);

  @override
  Future<Lobby> setReady(String id, bool ready) async => _record('setReady', [id, ready], () => lobbyResult!);

  @override
  Future<Lobby> addBot(String id) async => _record('addBot', [id], () => lobbyResult!);

  @override
  Future<String> startGame(String id) async => _record('startGame', [id], () => 'g1');

  @override
  Future<void> leaveLobby(String id) async => _record('leaveLobby', [id], () {});

  @override
  Future<GameSnapshot> snapshot(String gameId) async => _record('snapshot', [gameId], () => snapshotResult!);

  @override
  Future<int> command(String gameId, String type, [Json payload = const {}, int? expectedVersion]) async =>
      _record('command', [type, payload, expectedVersion], () => (expectedVersion ?? 0) + 1);

  @override
  Future<List<ChatMessage>> chat(String gameId) async => const [];

  @override
  Future<List<Json>> marks(String gameId) async => const [];

  @override
  Future<void> saveMarks(String gameId, List<Json> marks) async => _record('saveMarks', [marks], () {});
}

/// Хаб без сети: подписки отвечают заготовками, события тест шлёт сам.
class FakeRealtime extends Realtime {
  FakeRealtime(super.ref);

  Lobby? lobby;
  GameSnapshot? game;
  final lobbyCtl = StreamController<Lobby>.broadcast();
  final startedCtl = StreamController<({String lobbyId, String gameId})>.broadcast();
  final viewsCtl = StreamController<({GameView view, DateTime? deadline})>.broadcast();
  final chatCtl = StreamController<ChatMessage>.broadcast();

  @override
  Stream<Lobby> get lobbyUpdates => lobbyCtl.stream;

  @override
  Stream<({String lobbyId, String gameId})> get gameStarted => startedCtl.stream;

  @override
  Stream<({GameView view, DateTime? deadline})> get views => viewsCtl.stream;

  @override
  Stream<ChatMessage> get chat => chatCtl.stream;

  @override
  Future<Lobby> subscribeLobby(String lobbyId) async => lobby!;

  @override
  Future<void> unsubscribeLobby(String lobbyId) async {}

  @override
  Future<GameSnapshot> subscribeGame(String gameId) async => game!;

  @override
  void forgetGame(String gameId) {}

  @override
  Future<void> disconnect() async {}
}

/// Приложение целиком с подменённым сервером; [user] — вошедший игрок (null — без входа).
class TestApp {
  TestApp._(this.widget, this.container);

  final Widget widget;
  final ProviderContainer container;

  FakeApi get api => container.read(apiProvider) as FakeApi;

  FakeRealtime get realtime => container.read(realtimeProvider) as FakeRealtime;

  static Future<TestApp> create({User? user}) async {
    SharedPreferences.setMockInitialValues({
      if (user != null)
        'session': jsonEncode({'user': user.toJson(), 'accessToken': 'access', 'refreshToken': 'refresh'}),
    });
    final prefs = await SharedPreferences.getInstance();
    final container = ProviderContainer(overrides: [
      prefsProvider.overrideWithValue(prefs),
      apiProvider.overrideWith(FakeApi.new),
      realtimeProvider.overrideWith(FakeRealtime.new),
    ]);
    return TestApp._(UncontrolledProviderScope(container: container, child: const GhostLettersApp()), container);
  }

  void go(String location) => container.read(routerProvider).go(location);
}
