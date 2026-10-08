import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/session.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Один аккаунт на нескольких устройствах: код в профиле на старом, ввод кода на новом.
void main() {
  testWidgets('профиль: «Войти на другом устройстве» показывает код', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.go('/profile/u2');
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('link-device')));
    await tester.pumpAndSettle();

    expect(find.text('ABCD-EFGH'), findsOneWidget);
    expect(find.textContaining('до 12:30'), findsOneWidget);
  });

  testWidgets('чужой профиль: кнопки входа на другом устройстве нет', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.go('/profile/u3');
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('link-device')), findsNothing);
  });

  testWidgets('вход: ввожу код с другого устройства — вхожу в тот же аккаунт', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create();
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(const Key('login-by-code')));
    await tester.tap(find.byKey(const Key('login-by-code')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('link-code-input')), 'abcd-efgh');
    await tester.pump();
    await tester.tap(find.byKey(const Key('link-code-submit')));
    await tester.pumpAndSettle();

    final call = app.api.named('loginByCode').single.$2;
    expect(call[1], 'ABCDEFGH');
    expect(app.container.read(sessionProvider).user?.id, 'u2');
  });
}
