import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:ghost_letters/app.dart';
import 'package:ghost_letters/core/api.dart';
import 'package:ghost_letters/core/app_version.dart';
import 'package:ghost_letters/core/avatar_picker.dart';
import 'package:ghost_letters/core/card_catalog.dart';
import 'package:ghost_letters/core/realtime.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/core/sound.dart';
import 'package:ghost_letters/features/profile/avatar_crop.dart';
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
  Future<AuthTokens> loginByCode(String deviceId, String code) async => _record('loginByCode', [deviceId, code],
      () => const AuthTokens(accessToken: 'access2', refreshToken: 'refresh2', user: User(id: 'u2', nickname: 'Ватсон', avatarColor: '#3FB68B')));

  @override
  Future<Profile> profile(String userId) async => _record('profile', [userId], () => Profile(
        user: User(id: userId, nickname: userId == 'u2' ? 'Ватсон' : 'Игрок', avatarColor: '#3FB68B', avatarId: userId == 'u2' ? avatarId : null),
        games: 3,
        wins: 2,
        rating: 1016,
        likes: 1,
        achievements: const [],
      ));

  String? avatarId;

  @override
  Future<User> uploadAvatar(Uint8List bytes, String fileName) async => _record('uploadAvatar', [bytes.length, fileName], () {
        avatarId = 'm1';
        return const User(id: 'u2', nickname: 'Ватсон', avatarColor: '#3FB68B', avatarId: 'm1');
      });

  @override
  Future<User> removeAvatar() async => _record('removeAvatar', const [], () {
        avatarId = null;
        return const User(id: 'u2', nickname: 'Ватсон', avatarColor: '#3FB68B');
      });

  @override
  Future<List<LeaderRow>> leaderboard({bool bots = false}) async => _record('leaderboard', [bots], () => [
        const LeaderRow(user: User(id: 'u2', nickname: 'Ватсон', avatarColor: '#3FB68B'), rating: 1032, games: 4, wins: 3),
        if (bots) const LeaderRow(user: User(id: 'b1', nickname: 'Бот Лестрейд', avatarColor: '#5C7C99', avatarId: 'bot-portrait'), rating: 1010, games: 9, wins: 4, isBot: true),
        const LeaderRow(user: User(id: 'u1', nickname: 'Призрачный', avatarColor: '#7C6CF2'), rating: 984, games: 4, wins: 1),
      ]);

  @override
  Future<LinkCode> createLinkCode() async =>
      _record('createLinkCode', const [], () => LinkCode(code: 'ABCDEFGH', expiresAt: DateTime(2026, 10, 9, 12, 30)));

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
  Future<Lobby> addBot(String id, {String? botId}) async => _record('addBot', [id, botId], () => lobbyResult!);

  bool admin = false;
  List<BotInfo> botList = [];

  @override
  Future<List<BotCard>> bots() async => _record('bots', const [], () => [
        for (final b in botList.where((b) => b.enabled))
          BotCard(id: b.id, nickname: b.nickname, avatarColor: b.avatarColor, about: b.about),
      ]);

  @override
  Future<bool> isAdmin() async => _record('isAdmin', const [], () => admin);

  @override
  Future<List<BotInfo>> adminBots() async => _record('adminBots', const [], () => botList);

  @override
  Future<BotInfo> saveBot({String? id, required String nickname, required String color, required String about, required BotSpectra spectra, required bool enabled}) async =>
      _record('saveBot', [id, nickname, spectra, enabled], () {
        final bot = BotInfo(
            id: id ?? 'b${botList.length + 1}',
            nickname: 'Бот $nickname'.replaceFirst('Бот Бот ', 'Бот '),
            avatarColor: color,
            avatarId: botList.where((b) => b.id == id).firstOrNull?.avatarId,
            about: about,
            spectra: spectra,
            enabled: enabled);
        botList = [for (final b in botList) if (b.id != bot.id) b, bot];
        return bot;
      });

  @override
  Future<BotInfo> uploadBotAvatar(String botId, Uint8List bytes, String fileName) async =>
      _record('uploadBotAvatar', [botId, bytes.length, fileName], () => _setBotPhoto(botId, 'photo-$botId'));

  @override
  Future<BotInfo> removeBotAvatar(String botId) async => _record('removeBotAvatar', [botId], () => _setBotPhoto(botId, null));

  BotInfo _setBotPhoto(String botId, String? photo) {
    final b = botList.firstWhere((b) => b.id == botId);
    final bot = BotInfo(
        id: b.id, nickname: b.nickname, avatarColor: b.avatarColor, avatarId: photo, about: b.about, spectra: b.spectra, enabled: b.enabled);
    botList = [for (final x in botList) x.id == botId ? bot : x];
    return bot;
  }

  @override
  Future<List<BotInfo>> createPresetBots() async => _record('createPresetBots', const [], () {
        botList = [
          ...botList,
          const BotInfo(id: 'p1', nickname: 'Бот Пуаро', avatarColor: '#5C7C99', about: 'Смысл прежде всего', spectra: BotSpectra(meaning: 0.8, shape: 0.1, color: 0.1), enabled: true),
          const BotInfo(id: 'p2', nickname: 'Бот Коломбо', avatarColor: '#E57F4F', about: 'Рискует', spectra: BotSpectra(risk: 0.85), enabled: true),
        ];
        return botList;
      });

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
  Future<List<ChatMessage>> chat(String gameId) async => chatLoad == null ? const [] : await chatLoad!();

  Future<List<ChatMessage>> Function()? chatLoad;
  Future<List<Json>> Function()? marksLoad;
  Future<List<Json>> Function()? notesLoad;

  List<Json> marksResult = const [];

  @override
  Future<List<Json>> marks(String gameId) async => marksLoad == null ? marksResult : await marksLoad!();

  List<Json> notesResult = const [];

  @override
  Future<List<Json>> notes(String gameId) async => notesLoad == null ? notesResult : await notesLoad!();

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
  final snapshotsCtl = StreamController<GameSnapshot>.broadcast();
  final chatCtl = StreamController<ChatMessage>.broadcast();
  final connectedCtl = StreamController<bool>.broadcast();

  @override
  Stream<bool> get connected => connectedCtl.stream;

  int resyncs = 0;

  @override
  Future<void> resync() async {
    resyncs++;
    if (game case final snap?) snapshotsCtl.add(snap);
  }

  @override
  Stream<Lobby> get lobbyUpdates => lobbyCtl.stream;

  @override
  Stream<({String lobbyId, String gameId})> get gameStarted => startedCtl.stream;

  @override
  Stream<({GameView view, DateTime? deadline})> get views => viewsCtl.stream;

  @override
  Stream<GameSnapshot> get snapshots => snapshotsCtl.stream;

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
      avatarCropperProvider.overrideWithValue((context, bytes) async => bytes),
      avatarPickerProvider.overrideWithValue(() async => (bytes: Uint8List.fromList(const [0x89, 0x50, 0x4E, 0x47]), name: 'me.png')),
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
