import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:ghost_letters/app.dart';
import 'package:ghost_letters/core/api.dart';
import 'package:ghost_letters/core/app_version.dart';
import 'package:ghost_letters/core/card_catalog.dart';
import 'package:ghost_letters/core/realtime.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/core/sound.dart';
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
  Future<RolesPreview> previewRoles(int players, RoleOptions roles) async =>
      const RolesPreview(cooperative: true, rounds: 5, roles: ['Ghost', 'Detective']);

  @override
  Future<Lobby> createLobby(String title, LobbySettings settings) async => _record('createLobby', [title], () => lobbyResult!);

  @override
  Future<Lobby> joinLobby(String code, {bool table = false}) async => _record('joinLobby', [code, table], () => lobbyResult!);

  @override
  Future<Lobby> setReady(String id, bool ready) async => _record('setReady', [id, ready], () => lobbyResult!);

  @override
  Future<Lobby> saveSettings(String id, LobbySettings s) async => _record('saveSettings', [id, s], () => lobbyResult!);

  @override
  Future<Lobby> addBot(String id) async => _record('addBot', [id], () => lobbyResult!);

  @override
  Future<String> startGame(String id) async => _record('startGame', [id], () => 'g1');

  @override
  Future<void> leaveLobby(String id) async => _record('leaveLobby', [id], () {});

  @override
  Future<GameSnapshot> snapshot(String gameId) async => _record('snapshot', [gameId], () => snapshotResult!);

  /// Ошибка только для следующей команды (например, VERSION_CONFLICT).
  ApiError? failNextCommand;

  @override
  Future<int> command(String gameId, String type, [Json payload = const {}, int? expectedVersion]) async =>
      _record('command', [type, payload, expectedVersion], () {
        final once = failNextCommand;
        failNextCommand = null;
        if (once != null) throw once;
        return (expectedVersion ?? 0) + 1;
      });

  @override
  Future<List<ChatMessage>> chat(String gameId) async => const [];

  List<Json> marksResult = const [];

  @override
  Future<List<Json>> marks(String gameId) async => marksResult;

  List<Json> notesResult = const [];

  @override
  Future<List<Json>> notes(String gameId) async => notesResult;

  @override
  Future<void> saveNote(String gameId, String userId, int suspicion, String body) async =>
      _record('saveNote', [userId, suspicion, body], () {});

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
  final connectedCtl = StreamController<bool>.broadcast();

  @override
  Stream<bool> get connected => connectedCtl.stream;

  int resyncs = 0;

  @override
  Future<void> resync() async {
    resyncs++;
  }

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
      soundOutputProvider.overrideWith((ref) => FakeSoundOutput()),
      apiProvider.overrideWith(FakeApi.new),
      realtimeProvider.overrideWith(FakeRealtime.new),
      cardCatalogProvider.overrideWith((ref) async => testCatalog),
      latestAndroidVersionProvider.overrideWith((ref) async => null),
    ]);
    return TestApp._(UncontrolledProviderScope(container: container, child: const GhostLettersApp()), container);
  }

  /// Маленький каталог вместо assets/cards/cards.json (в тестах ассеты грузятся медленно).
  static const testCatalog = [
    CardSetInfo(code: 'original', title: 'Оригинальный', cards: ['orig_0300', 'orig_0301', 'orig_0302']),
    CardSetInfo(code: 'ritual', title: 'Тайный ритуал', cards: ['orig_0400', 'orig_0401']),
  ];

  void go(String location) => container.read(routerProvider).go(location);
}

class FakeSoundOutput implements SoundOutput {
  final effects = <Sfx>[];
  final musicCalls = <({Music? track, double volume})>[];
  @override
  void music(Music? track, double volume) => musicCalls.add((track: track, volume: volume));
  @override
  void effect(Sfx effect, double volume) => effects.add(effect);
  @override
  void dispose() {}
}
