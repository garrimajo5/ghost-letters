import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/api.dart';
import 'package:ghost_letters/core/config.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/features/auth/login_screen.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Сервер-заглушка для Dio: отвечает по пути, запоминает запросы.
class FakeServer implements HttpClientAdapter {
  FakeServer(this.routes);

  final Map<String, (int, Object?) Function(RequestOptions)> routes;
  final requests = <RequestOptions>[];

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    requests.add(options);
    final route = routes[options.path];
    final (status, body) = route == null ? (404, null) : route(options);
    return ResponseBody.fromString(jsonEncode(body), status, headers: {
      Headers.contentTypeHeader: [Headers.jsonContentType],
    });
  }

  @override
  void close({bool force = false}) {}
}

Map<String, Object?> tokens(String access) => {'accessToken': access, 'refreshToken': 'r-$access', 'user': host.toJson()};

Future<ProviderContainer> containerWith(Map<String, Object> prefsValues, FakeServer server) async {
  SharedPreferences.setMockInitialValues(prefsValues);
  final prefs = await SharedPreferences.getInstance();
  final container = ProviderContainer(overrides: [
    prefsProvider.overrideWithValue(prefs),
    apiProvider.overrideWith((ref) => Api(ref, adapter: server)),
  ]);
  addTearDown(container.dispose);
  return container;
}

void main() {
  testWidgets('запомненный аккаунт: приложение открывается сразу на главной, без экрана ника', (tester) async {
    final app = await TestApp.create(user: host);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('nickname')), findsNothing);
    expect(find.text('СОЗДАТЬ ИГРУ'), findsOneWidget);
  });

  test('токены другого сервера не дают вход в аккаунт', () async {
    final container = await containerWith({
      'session': jsonEncode({'user': host.toJson(), 'accessToken': 'a', 'refreshToken': 'r', 'server': 'http://other:8080'}),
    }, FakeServer({}));

    final session = container.read(sessionProvider);
    expect(session.isSignedIn, isFalse);
    expect(session.hasTokens, isFalse);
    expect(session.user, isNull);
  });

  test('без токенов API не восстанавливает вход по deviceId', () async {
    final server = FakeServer({
      '/auth/guest': (o) => (200, tokens('fresh')),
      '/me/games': (o) => (200, <Object>[]),
    });
    final container = await containerWith({
      'session': jsonEncode({'user': host.toJson(), 'server': 'http://other:8080'}),
      'device_id': 'device-1',
    }, server);

    await container.read(apiProvider).myGames();

    expect(server.requests.where((r) => r.path == '/auth/guest'), isEmpty);
    expect(container.read(sessionProvider).isSignedIn, isFalse);
  });

  test('сервер отверг refresh — сессия удалена, обход через guest запрещён', () async {
    var gamesCalls = 0;
    final server = FakeServer({
      '/me/games': (o) => (++gamesCalls == 1 ? 401 : 200, <Object>[]),
      '/auth/refresh': (o) => (401, {'code': 'UNAUTHORIZED'}),
      '/auth/guest': (o) => (200, tokens('again')),
    });
    final container = await containerWith({
      'session': jsonEncode({'user': host.toJson(), 'accessToken': 'old', 'refreshToken': 'r', 'server': AppConfig.apiUrl}),
    }, server);

    await expectLater(container.read(apiProvider).myGames(), throwsA(isA<ApiError>()));
    expect(container.read(sessionProvider).isSignedIn, isFalse);
    expect(server.requests.where((r) => r.path == '/auth/guest'), isEmpty);
  });

  testWidgets('после выхода экран входа подставляет прежний ник', (tester) async {
    SharedPreferences.setMockInitialValues({'last_profile': jsonEncode(watson.toJson())});
    final prefs = await SharedPreferences.getInstance();
    await tester.pumpWidget(ProviderScope(
      overrides: [prefsProvider.overrideWithValue(prefs)],
      child: const MaterialApp(home: LoginScreen()),
    ));

    expect(tester.widget<TextField>(find.byKey(const Key('nickname'))).controller!.text, watson.nickname);
  });
}
