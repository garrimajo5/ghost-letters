import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/api.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

String _jwt(Map<String, Object> payload) {
  String part(Object o) => base64Url.encode(utf8.encode(jsonEncode(o))).replaceAll('=', '');
  return '${part({'alg': 'HS256'})}.${part(payload)}.signature';
}

void main() {
  test('срок токена читается из exp', () {
    final exp = DateTime.utc(2026, 10, 7, 12);
    expect(jwtExpiry(_jwt({'sub': 'u1', 'exp': exp.millisecondsSinceEpoch ~/ 1000})), exp.toLocal());
    expect(jwtExpiry('не токен'), isNull);
  });

  test('ошибка хаба превращается в код и понятный текст', () {
    final e = ApiError.from(Exception(
        "An unexpected error occurred invoking 'SubscribeGame' on the server. HubException: FORBIDDEN: Вы не участвуете в этой партии."));
    expect(e.code, 'FORBIDDEN');
    expect(e.message, 'Вы не участвуете в этой партии.');
  });

  testWidgets('конфликт версий: клиент перечитывает партию и повторяет ход', (tester) async {
    tester.view.physicalSize = const Size(1200, 3000);
    tester.view.devicePixelRatio = 1.5;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    final snap = snapshot(phase: 'Mailbox', allowed: const ['SendLetter']);
    app.realtime.game = snap;
    app.api.snapshotResult = snapshot(phase: 'Mailbox', allowed: const ['SendLetter'], version: 43);
    app.go('/game/g1');
    await tester.pumpAndSettle();

    app.api.failNextCommand = const ApiError('VERSION_CONFLICT', 'Состояние изменилось', 409);
    await tester.tap(find.byKey(const Key('hand-orig_0200')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('cta')));
    await tester.pumpAndSettle();

    final commands = app.api.named('command').toList();
    expect(commands, hasLength(2));
    expect(commands[0].$2[2], 42);
    expect(commands[1].$2[2], 43, reason: 'повтор с версией из свежего снимка');
    expect(find.text('Состояние изменилось'), findsNothing);
  });
}
