import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart' show debugPrint;
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/models.dart';
import '../models/admin_cards.dart';
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
        final slow = e.type == DioExceptionType.connectionTimeout ||
            e.type == DioExceptionType.receiveTimeout ||
            e.type == DioExceptionType.sendTimeout;
        return ApiError('OFFLINE', slow ? slowMessage : offlineMessage);
      }
      final status = e.response?.statusCode;
      // 502–504 — сервер перезапускается (обычно при обновлении) и вернётся через минуту.
      if (status != null && status >= 502 && status <= 504) return ApiError('UNAVAILABLE', restartingMessage, status);
      return ApiError('HTTP_$status', 'Ошибка сервера ($status). Попробуйте ещё раз.', status);
    }
    // Ошибки хаба приходят текстом «… КОД: сообщение».
    final hub = RegExp(r'([A-Z][A-Z_]{3,}):\s*(.+)$', multiLine: true).firstMatch(e.toString());
    if (hub != null) return ApiError(hub.group(1)!, hub.group(2)!.trim());
    // Остальное — внутренняя ошибка: технический текст игроку не показываем.
    debugPrint('Необработанная ошибка: $e');
    return const ApiError('ERROR', unknownMessage);
  }

  static const offlineMessage = 'Нет связи с сервером. Проверьте интернет и попробуйте ещё раз.';
  static const slowMessage = 'Сервер долго не отвечает. Проверьте интернет и попробуйте ещё раз.';
  static const restartingMessage = 'Сервер обновляется — попробуйте через минуту.';
  static const unknownMessage = 'Что-то пошло не так. Попробуйте ещё раз.';

  final String code;
  final String message;
  final int? status;

  @override
  String toString() => message;
}

/// REST-клиент: подставляет токен и один раз обновляет его при 401.
class Api {
  Api(this._ref, {HttpClientAdapter? adapter}) : _adapter = adapter {
    if (adapter != null) _dio.httpClientAdapter = adapter;
    _dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) async {
        // Токены другого сервера не используются.
        if (!_ref.read(sessionProvider).hasTokens && !options.path.startsWith('/auth/')) await ensureTokens();
        final token = _ref.read(sessionProvider).accessToken;
        if (token != null) options.headers['Authorization'] = 'Bearer $token';
        handler.next(options);
      },
      onError: (error, handler) async {
        final retried = error.requestOptions.extra['retried'] == true;
        final used = (error.requestOptions.headers['Authorization'] as String?)?.replaceFirst('Bearer ', '');
        // Токен уже обновил параллельный запрос — просто повторяем с новым.
        final alreadyFresh = used != null && _ref.read(sessionProvider).accessToken != null && used != _ref.read(sessionProvider).accessToken;
        if (!error.requestOptions.path.startsWith('/auth/') && error.response?.statusCode == 401 && !retried && (alreadyFresh || await refreshTokens())) {
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
  final HttpClientAdapter? _adapter;
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

  /// Истекшую или отозванную сессию нельзя восстановить идентификатором устройства.
  Future<bool> ensureTokens() async => _ref.read(sessionProvider).hasTokens;

  Dio _bare() {
    final dio = Dio(BaseOptions(baseUrl: AppConfig.api, connectTimeout: const Duration(seconds: 8)));
    final adapter = _adapter;
    if (adapter != null) dio.httpClientAdapter = adapter;
    return dio;
  }

  Future<bool> _doRefresh() async {
    final refresh = _ref.read(sessionProvider).refreshToken;
    if (refresh == null) return false;
    try {
      final r = await _bare().post<Map<String, dynamic>>('/auth/refresh', data: {'refreshToken': refresh});
      _ref.read(sessionProvider.notifier).signIn(AuthTokens.fromJson(r.data!));
      return true;
    } on DioException catch (e) {
      // Сбой сети сохраняет сессию; явный отзыв требует нового подтверждения входа.
      if (e.response?.statusCode == 401) _ref.read(sessionProvider.notifier).signOut();
      return false;
    }
  }

  Future<void> logout() async {
    await _refreshing;
    final refresh = _ref.read(sessionProvider).refreshToken;
    if (refresh != null) {
      await _call(() => _bare().post<dynamic>('/auth/logout', data: {'refreshToken': refresh}));
    }
    _ref.read(sessionProvider.notifier).signOut();
  }

  /// Токен для хаба: если истекает в ближайшую минуту — сначала обновляем.
  Future<String> freshAccessToken() async {
    if (!_ref.read(sessionProvider).hasTokens) await ensureTokens();
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

  /// Вход на этом устройстве по коду с другого — тот же игрок, что и там.
  Future<AuthTokens> loginByCode(String deviceId, String code) async =>
      AuthTokens.fromJson(await post('/auth/link', {'deviceId': deviceId, 'code': code}) as Json);

  /// Одноразовый код (10 минут), чтобы войти в этот аккаунт на другом устройстве.
  Future<LinkCode> createLinkCode() async => LinkCode.fromJson(await post('/auth/link-code') as Json);

  Future<User> updateMe({String? nickname, String? color}) async =>
      User.fromJson(await patch('/me', {if (nickname != null) 'nickname': nickname, if (color != null) 'avatarColor': color}) as Json);

  /// Таблица лидеров; [bots] — показать и ботов.
  Future<List<LeaderRow>> leaderboard({bool bots = false}) async =>
      ((await get('/leaderboard', query: {'limit': 100, 'bots': bots})) as List).map((e) => LeaderRow.fromJson(Map<String, dynamic>.from(e as Map))).toList();

  Future<List<BotRelationship>> botRelationships(String id, {bool details = false}) async =>
      (await get('${details ? '/admin' : ''}/bots/$id/relationships') as List)
          .map((j) => BotRelationship.fromJson(Map<String, dynamic>.from(j as Map))).toList();

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
      'accomplices': roles.extraAccomplices,
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

  Future<List<SettingsPreset>> settingsPresets() async =>
      (await get('/me/settings-presets') as List).map((j) => SettingsPreset.fromJson(j as Json)).toList();

  Future<SettingsPreset> saveSettingsPreset(String id, String name, LobbySettings settings) async =>
      SettingsPreset.fromJson(await put('/me/settings-presets/$id', {'name': name, 'settings': settings.toJson()}) as Json);

  Future<void> deleteSettingsPreset(String id) async => _call(() => _dio.delete<dynamic>('/me/settings-presets/$id'));


  Future<List<WatchableGame>> watchableGames() async =>
      (await get('/lobbies/watchable') as List).map((j) => WatchableGame.fromJson(j as Json)).toList();

  Future<Lobby> joinLobby(String code, {bool table = false, bool spectator = false}) async =>
      Lobby.fromJson(await post('/lobbies/${code.toUpperCase()}/join', {'mode': spectator ? 'spectator' : (table ? 'table' : 'player')}) as Json);

  Future<void> leaveLobby(String id) => post('/lobbies/$id/leave');

  Future<Lobby> setReady(String id, bool ready) async => Lobby.fromJson(await post('/lobbies/$id/ready', {'ready': ready}) as Json);

  Future<Lobby> saveSettings(String id, LobbySettings s) async => Lobby.fromJson(await put('/lobbies/$id/settings', s.toJson()) as Json);

  /// Добавить бота в лобби: конкретного из кабинета ([botId]) или случайного.
  Future<Lobby> addBot(String id, {String? botId}) async =>
      Lobby.fromJson(await post(botId == null ? '/lobbies/$id/bots' : '/lobbies/$id/bots?botId=$botId') as Json);

  /// Боты, которых можно позвать в лобби.
  Future<List<BotCard>> bots() async =>
      ((await get('/bots')) as List).map((e) => BotCard.fromJson(Map<String, dynamic>.from(e as Map))).toList();

  /// Админ ли я (кабинет ботов).
  Future<bool> isAdmin() async => ((await get('/admin/me')) as Json)['isAdmin'] as bool? ?? false;

  Future<AdminCardPage> adminCards({String query = '', String setCode = '', bool? active, int page = 0}) async =>
      AdminCardPage.fromJson(await get('/admin/cards', query: {'query': query, 'setCode': setCode, if (active != null) 'active': active, 'page': page}) as Json);

  Future<AdminCard> saveCard(AdminCard card) async => AdminCard.fromJson(await put('/admin/cards/${card.id}', card.toJson()) as Json);

  Future<List<BotInfo>> adminBots() async =>
      ((await get('/admin/bots')) as List).map((e) => BotInfo.fromJson(Map<String, dynamic>.from(e as Map))).toList();

  Future<BotInfo> saveBot({String? id, required String nickname, required String color, required String about, required BotSpectra spectra, required bool enabled}) async {
    final body = {'nickname': nickname, 'avatarColor': color, 'about': about, 'spectra': spectra.toJson(), 'enabled': enabled};
    return BotInfo.fromJson((id == null ? await post('/admin/bots', body) : await put('/admin/bots/$id', body)) as Json);
  }

  /// Фото бота (только админ): уже обрезанное под круг.
  Future<BotInfo> uploadBotAvatar(String botId, Uint8List bytes, String fileName) async {
    final form = FormData.fromMap({'file': MultipartFile.fromBytes(bytes, filename: fileName)});
    return BotInfo.fromJson(await _call(
        () => _dio.post<dynamic>('/admin/bots/$botId/avatar', data: form, options: Options(contentType: 'multipart/form-data'))) as Json);
  }

  Future<BotInfo> removeBotAvatar(String botId) async =>
      BotInfo.fromJson(await _call(() => _dio.delete<dynamic>('/admin/bots/$botId/avatar')) as Json);

  Future<List<BotInfo>> createPresetBots() async =>
      ((await post('/admin/bots/presets')) as List).map((e) => BotInfo.fromJson(Map<String, dynamic>.from(e as Map))).toList();

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

  /// Загрузить свою аватарку (JPEG/PNG/WebP до 1 МБ).
  Future<User> uploadAvatar(Uint8List bytes, String fileName) async {
    final form = FormData.fromMap({'file': MultipartFile.fromBytes(bytes, filename: fileName)});
    return User.fromJson(
        await _call(() => _dio.post<dynamic>('/me/avatar', data: form, options: Options(contentType: 'multipart/form-data'))) as Json);
  }

  Future<User> removeAvatar() async => User.fromJson(await _call(() => _dio.delete<dynamic>('/me/avatar')) as Json);

  /// Загрузить голосовое и получить его id для сообщения.
  Future<String> uploadVoice(MultipartFile file, int durationMs) async {
    final form = FormData.fromMap({
      'file': file,
      'durationMs': durationMs.toString(),
    });
    final r = await _call(() => _dio.post<dynamic>('/media', data: form, options: Options(contentType: 'multipart/form-data'))) as Json;
    return r['mediaId'] as String;
  }

  /// Скачать голосовое (запрос с токеном — файлы доступны только участникам).
  Future<Uint8List> downloadVoice(String mediaId) async => Uint8List.fromList(
      (await _call(() => _dio.get<List<int>>('/media/$mediaId', options: Options(responseType: ResponseType.bytes)))) as List<int>);

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
