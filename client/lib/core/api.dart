import 'dart:convert';

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
    // Ошибки хаба приходят текстом «… КОД: сообщение».
    final hub = RegExp(r'([A-Z][A-Z_]{3,}):\s*(.+)$', multiLine: true).firstMatch(e.toString());
    if (hub != null) return ApiError(hub.group(1)!, hub.group(2)!.trim());
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
        final used = (error.requestOptions.headers['Authorization'] as String?)?.replaceFirst('Bearer ', '');
        // Токен уже обновил параллельный запрос — просто повторяем с новым.
        final alreadyFresh = used != null && used != _ref.read(sessionProvider).accessToken;
        if (error.response?.statusCode == 401 && !retried && (alreadyFresh || await refreshTokens())) {
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

  Future<bool>? _refreshing;

  /// Обновить пару токенов. Одновременные вызовы ждут один запрос: повторное использование
  /// старого refresh-токена сервер считает кражей и отзывает все сессии.
  Future<bool> refreshTokens() => _refreshing ??= _doRefresh().whenComplete(() => _refreshing = null);

  Future<bool> _doRefresh() async {
    final refresh = _ref.read(sessionProvider).refreshToken;
    if (refresh == null) return false;
    try {
      final r = await Dio(BaseOptions(baseUrl: AppConfig.api)).post<Map<String, dynamic>>('/auth/refresh', data: {'refreshToken': refresh});
      _ref.read(sessionProvider.notifier).signIn(AuthTokens.fromJson(r.data!));
      return true;
    } on DioException catch (e) {
      // Выходим, только если сервер отверг сессию; при обрыве сети остаёмся в аккаунте.
      if (e.response?.statusCode == 401) _ref.read(sessionProvider.notifier).signOut();
      return false;
    }
  }

  /// Токен для хаба: если истекает в ближайшую минуту — сначала обновляем.
  Future<String> freshAccessToken() async {
    final token = _ref.read(sessionProvider).accessToken;
    if (token == null) return '';
    final exp = jwtExpiry(token);
    if (exp != null && exp.difference(DateTime.now()) < const Duration(minutes: 1)) {
      await refreshTokens();
    }
    return _ref.read(sessionProvider).accessToken ?? '';
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

  /// Состав ролей по числу игроков и настройкам (для экрана лобби).
  Future<RolesPreview> previewRoles(int players, RoleOptions roles) async {
    final r = await get('/rules/roles', query: {
      'players': players,
      'killer': roles.killerEnabled,
      'witness': roles.useWitness,
      'expert': roles.useExpert,
      'blackmailer': roles.useBlackmailer,
      'imitator': roles.toJson()['imitator'],
    }) as Json;
    return RolesPreview(
      cooperative: r['cooperative'] as bool,
      rounds: (r['rounds'] as num).toInt(),
      roles: (r['roles'] as List).map((e) => e.toString()).toList(),
    );
  }

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

  Future<ChatMessage> sendChat(String gameId, String text,
          {String channel = 'public', List<String> cards = const [], String? mediaId}) async =>
      ChatMessage.fromJson(await post('/games/$gameId/chat', {
        'channel': channel,
        'text': text.isEmpty ? null : text,
        'cardIds': cards,
        if (mediaId != null) 'mediaId': mediaId,
      }) as Json);

  /// Загрузить голосовое (AAC) и получить его id для сообщения.
  Future<String> uploadVoice(String filePath, int durationMs) async {
    final form = FormData.fromMap({
      'file': await MultipartFile.fromFile(filePath, filename: 'voice.m4a', contentType: DioMediaType('audio', 'mp4')),
      'durationMs': durationMs.toString(),
    });
    final r = await _call(() => _dio.post<dynamic>('/media', data: form, options: Options(contentType: 'multipart/form-data'))) as Json;
    return r['mediaId'] as String;
  }

  /// Скачать голосовое во временный файл (запрос с токеном — файлы доступны только участникам).
  Future<void> downloadVoice(String mediaId, String toPath) => _call(() => _dio.download('/media/$mediaId', toPath));

  Future<void> saveNote(String gameId, String userId, int suspicion, String body) =>
      put('/games/$gameId/notes/$userId', {'suspicion': suspicion, 'body': body});

  Future<List<Json>> notes(String gameId) async => ((await get('/games/$gameId/notes')) as List).cast<Json>();

  Future<List<Json>> marks(String gameId) async => ((await get('/games/$gameId/marks')) as List).cast<Json>();

  Future<void> saveMarks(String gameId, List<Json> marks) => put('/games/$gameId/marks', marks);
}

final apiProvider = Provider<Api>((ref) => Api(ref));

class RolesPreview {
  const RolesPreview({required this.cooperative, required this.rounds, required this.roles});

  final bool cooperative;
  final int rounds;
  final List<String> roles;
}

final rolesPreviewProvider = FutureProvider.autoDispose.family<RolesPreview, ({int players, RoleOptions roles})>(
  (ref, key) => ref.read(apiProvider).previewRoles(key.players, key.roles),
);

/// Время истечения JWT из поля exp (без проверки подписи — только чтобы обновить заранее).
DateTime? jwtExpiry(String token) {
  final parts = token.split('.');
  if (parts.length != 3) return null;
  try {
    final payload = jsonDecode(utf8.decode(base64Url.decode(base64Url.normalize(parts[1])))) as Map<String, dynamic>;
    final exp = payload['exp'];
    return exp is num ? DateTime.fromMillisecondsSinceEpoch(exp.toInt() * 1000) : null;
  } catch (_) {
    return null;
  }
}
