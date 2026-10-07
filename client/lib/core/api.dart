import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/models.dart';
import 'config.dart';
import 'session.dart';

/// Ошибка сервера: код из контракта (VALIDATION, NOT_YOUR_TURN, …) и текст для игрока.
class ApiError implements Exception {
  const ApiError(this.code, this.message, [this.status]);

  factory ApiError.from(Object e) {
    if (e is ApiError) return e;
    if (e is DioException) {
      final data = e.response?.data;
      if (data is Map) {
        return ApiError(
          (data['code'] ?? data['title'] ?? 'ERROR').toString(),
          (data['detail'] ?? data['title'] ?? 'Ошибка сервера').toString(),
          e.response?.statusCode,
        );
      }
      if (e.response == null) {
        return const ApiError('OFFLINE', 'Нет связи с сервером. Проверьте, что он запущен.');
      }
      return ApiError('HTTP_${e.response?.statusCode}', 'Ошибка сервера (${e.response?.statusCode}).', e.response?.statusCode);
    }
    return ApiError('ERROR', e.toString());
  }

  final String code;
  final String message;
  final int? status;

  @override
  String toString() => message;
}

/// REST-клиент: подставляет токен и один раз обновляет его при 401.
class Api {
  Api(this._ref) {
    _dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) {
        final token = _ref.read(sessionProvider).accessToken;
        if (token != null) options.headers['Authorization'] = 'Bearer $token';
        handler.next(options);
      },
      onError: (error, handler) async {
        final retried = error.requestOptions.extra['retried'] == true;
        if (error.response?.statusCode == 401 && !retried && await _refresh()) {
          final options = error.requestOptions
            ..extra['retried'] = true
            ..headers['Authorization'] = 'Bearer ${_ref.read(sessionProvider).accessToken}';
          try {
            handler.resolve(await _dio.fetch<dynamic>(options));
          } on DioException catch (e) {
            handler.next(e);
          }
          return;
        }
        handler.next(error);
      },
    ));
  }

  final Ref _ref;
  final Dio _dio = Dio(BaseOptions(
    baseUrl: AppConfig.api,
    connectTimeout: const Duration(seconds: 8),
    receiveTimeout: const Duration(seconds: 15),
    contentType: 'application/json',
  ));

  Future<bool> _refresh() async {
    final refresh = _ref.read(sessionProvider).refreshToken;
    if (refresh == null) return false;
    try {
      final r = await Dio(BaseOptions(baseUrl: AppConfig.api)).post<Map<String, dynamic>>('/auth/refresh', data: {'refreshToken': refresh});
      _ref.read(sessionProvider.notifier).signIn(AuthTokens.fromJson(r.data!));
      return true;
    } catch (_) {
      _ref.read(sessionProvider.notifier).signOut();
      return false;
    }
  }

  Future<dynamic> _call(Future<Response<dynamic>> Function() request) async {
    try {
      return (await request()).data;
    } catch (e) {
      throw ApiError.from(e);
    }
  }

  Future<dynamic> get(String path, {Map<String, dynamic>? query}) => _call(() => _dio.get<dynamic>(path, queryParameters: query));

  Future<dynamic> post(String path, [Object? body]) => _call(() => _dio.post<dynamic>(path, data: body ?? const {}));

  Future<dynamic> put(String path, Object body) => _call(() => _dio.put<dynamic>(path, data: body));

  Future<dynamic> patch(String path, Object body) => _call(() => _dio.patch<dynamic>(path, data: body));

  // ---------- Вход и профиль ----------

  Future<AuthTokens> loginGuest(String deviceId, String nickname, String color) async =>
      AuthTokens.fromJson(await post('/auth/guest', {'deviceId': deviceId, 'nickname': nickname, 'avatarColor': color}) as Json);

  Future<User> updateMe({String? nickname, String? color}) async =>
      User.fromJson(await patch('/me', {if (nickname != null) 'nickname': nickname, if (color != null) 'avatarColor': color}) as Json);

  Future<Profile> profile(String userId) async => Profile.fromJson(await get('/users/$userId/profile') as Json);

  Future<List<MyGame>> myGames({String? status}) async =>
      ((await get('/me/games', query: {if (status != null) 'status': status})) as List).map((e) => MyGame.fromJson(e as Json)).toList();

  // ---------- Лобби ----------

  Future<Lobby> createLobby(String title, LobbySettings settings) async =>
      Lobby.fromJson(await post('/lobbies', {'title': title, 'settings': settings.toJson()}) as Json);

  Future<Lobby> lobbyByCode(String code) async => Lobby.fromJson(await get('/lobbies/${code.toUpperCase()}') as Json);

  Future<Lobby> joinLobby(String code, {bool table = false}) async =>
      Lobby.fromJson(await post('/lobbies/${code.toUpperCase()}/join', {'mode': table ? 'table' : 'player'}) as Json);

  Future<void> leaveLobby(String id) => post('/lobbies/$id/leave');

  Future<Lobby> setReady(String id, bool ready) async => Lobby.fromJson(await post('/lobbies/$id/ready', {'ready': ready}) as Json);

  Future<Lobby> saveSettings(String id, LobbySettings s) async => Lobby.fromJson(await put('/lobbies/$id/settings', s.toJson()) as Json);

  Future<Lobby> addBot(String id) async => Lobby.fromJson(await post('/lobbies/$id/bots') as Json);

  Future<void> kick(String id, String userId) => post('/lobbies/$id/kick', {'userId': userId});

  Future<String> startGame(String id) async => ((await post('/lobbies/$id/start')) as Json)['gameId'] as String;

  // ---------- Партия ----------

  Future<GameSnapshot> snapshot(String gameId) async => GameSnapshot.fromJson(await get('/games/$gameId/view') as Json);

  Future<int> command(String gameId, String type, [Json payload = const {}, int? expectedVersion]) async {
    final r = await post('/games/$gameId/commands', {
      'type': type,
      'payload': payload,
      'expectedVersion': expectedVersion,
      'clientCommandId': '${DateTime.now().microsecondsSinceEpoch}-$type',
    }) as Json;
    return (r['version'] as num).toInt();
  }

  Future<List<ChatMessage>> chat(String gameId) async =>
      ((await get('/games/$gameId/chat')) as List).map((e) => ChatMessage.fromJson(e as Json)).toList();

  Future<ChatMessage> sendChat(String gameId, String text, {String channel = 'public', List<String> cards = const []}) async =>
      ChatMessage.fromJson(await post('/games/$gameId/chat', {'channel': channel, 'text': text, 'cardIds': cards}) as Json);

  Future<void> saveNote(String gameId, String userId, int suspicion, String body) =>
      put('/games/$gameId/notes/$userId', {'suspicion': suspicion, 'body': body});

  Future<List<Json>> notes(String gameId) async => ((await get('/games/$gameId/notes')) as List).cast<Json>();

  Future<List<Json>> marks(String gameId) async => ((await get('/games/$gameId/marks')) as List).cast<Json>();

  Future<void> saveMarks(String gameId, List<Json> marks) => put('/games/$gameId/marks', marks);
}

final apiProvider = Provider<Api>((ref) => Api(ref));
