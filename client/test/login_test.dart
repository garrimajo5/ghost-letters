import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/features/auth/login_screen.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<Widget> _app() async {
  SharedPreferences.setMockInitialValues({});
  final prefs = await SharedPreferences.getInstance();
  return ProviderScope(
    overrides: [prefsProvider.overrideWithValue(prefs)],
    child: const MaterialApp(home: LoginScreen()),
  );
}

void main() {
  testWidgets('короткий ник не отправляется', (tester) async {
    await tester.pumpWidget(await _app());

    await tester.enterText(find.byKey(const Key('nickname')), 'Я');
    await tester.tap(find.byKey(const Key('login')));
    await tester.pump();

    expect(find.text('Ник — от 2 до 20 символов'), findsOneWidget);
  });

  testWidgets('аватар показывает первую букву ника', (tester) async {
    await tester.pumpWidget(await _app());

    await tester.enterText(find.byKey(const Key('nickname')), 'ватсон');
    await tester.pump();

    expect(find.text('В'), findsOneWidget);
  });

  test('deviceId создаётся один раз и сохраняется', () async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    final container = ProviderContainer(overrides: [prefsProvider.overrideWithValue(prefs)]);
    addTearDown(container.dispose);

    final first = container.read(sessionProvider.notifier).deviceId;
    final second = container.read(sessionProvider.notifier).deviceId;

    expect(first, startsWith('device-'));
    expect(second, first);
    expect(container.read(sessionProvider).isSignedIn, isFalse);
  });
}
