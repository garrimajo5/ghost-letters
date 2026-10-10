import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/api.dart';
import 'package:ghost_letters/core/card_catalog.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/features/auth/recovery_screen.dart';
import 'package:ghost_letters/widgets/common.dart';
import 'package:shared_preferences/shared_preferences.dart';

class RecoveryApi extends Api {
  RecoveryApi(super.ref);
  Map<String, dynamic>? sent;
  String? path;
  Map<String, dynamic> info = {'login': 'watson', 'kind': 'word'};
  @override
  Future<dynamic> get(String path, {Map<String, dynamic>? query}) async => info;
  @override
  Future<dynamic> post(String path, [Object? body]) async {
    this.path = path;
    sent = Map<String, dynamic>.from(body! as Map);
    return {'accessToken': 'access', 'refreshToken': 'refresh', 'user': {'id': 'owner', 'nickname': 'Ватсон', 'avatarColor': '#3E7C6E'}};
  }
  @override
  Future<dynamic> put(String path, Object body) => post(path, body);
}

Future<ProviderContainer> open(WidgetTester tester, RecoveryScreen screen, {Size size = const Size(800, 1000)}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  SharedPreferences.setMockInitialValues({});
  final prefs = await SharedPreferences.getInstance();
  final container = ProviderContainer(overrides: [prefsProvider.overrideWithValue(prefs), apiProvider.overrideWith(RecoveryApi.new),
    recoveryCardCatalogProvider.overrideWith((ref) async => const [CardSetInfo(code: 'original', title: 'Оригинальный', cards: ['orig_0022', 'orig_0023', 'orig_0024'])]),
  ]);
  addTearDown(container.dispose);
  await tester.pumpWidget(UncontrolledProviderScope(container: container, child: MaterialApp(home: Builder(builder: (context) => Scaffold(
    body: TextButton(onPressed: () => Navigator.push<void>(context, MaterialPageRoute(builder: (_) => screen)), child: const Text('Open')),
  )))));
  await tester.tap(find.text('Open'));
  await tester.pumpAndSettle();
  return container;
}

Future<void> submit(WidgetTester tester) async {
  await tester.ensureVisible(find.byKey(const Key('recovery-submit')));
  await tester.tap(find.byKey(const Key('recovery-submit')));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('registration sends confirmed key atomically and never stores it in preferences', (tester) async {
    final c = await open(tester, const RecoveryScreen(nickname: 'Ватсон', color: '#3E7C6E'));
    final api = c.read(apiProvider) as RecoveryApi;
    await tester.enterText(find.byKey(const Key('recovery-login')), 'Watson');
    await tester.enterText(find.byKey(const Key('recovery-word')), 'Секретная фраза');
    await tester.enterText(find.byKey(const Key('recovery-repeat')), 'Другая фраза');
    await submit(tester);
    expect(api.sent, isNull);
    expect(find.text('Слова не совпадают'), findsOneWidget);
    await tester.enterText(find.byKey(const Key('recovery-repeat')), 'Секретная фраза');
    await submit(tester);
    expect(api.path, '/auth/guest');
    expect(api.sent!['recovery'], {'login': 'watson', 'key': {'kind': 'word', 'word': 'Секретная фраза'}});
    expect(c.read(sessionProvider).user?.id, 'owner');
    final prefs = c.read(prefsProvider);
    expect(prefs.getKeys().map(prefs.get).join(), isNot(contains('Секретная фраза')));
  });

  testWidgets('existing account can enter with word on narrow phone', (tester) async {
    final c = await open(tester, const RecoveryScreen(), size: const Size(360, 740));
    await tester.enterText(find.byKey(const Key('recovery-login')), 'watson');
    await tester.enterText(find.byKey(const Key('recovery-word')), 'секрет');
    await submit(tester);
    expect((c.read(apiProvider) as RecoveryApi).path, '/auth/key-login');
    expect(tester.takeException(), isNull);
  });

  testWidgets('profile key change confirms previous key and updates session', (tester) async {
    final c = await open(tester, const RecoveryScreen(edit: true));
    expect(find.text('watson'), findsOneWidget);
    await tester.enterText(find.byKey(const Key('recovery-word')).at(0), 'старый ключ');
    await tester.enterText(find.byKey(const Key('recovery-word')).at(1), 'новый ключ');
    await tester.enterText(find.byKey(const Key('recovery-repeat')), 'новый ключ');
    await submit(tester);
    final api = c.read(apiProvider) as RecoveryApi;
    expect(api.path, '/me/recovery');
    expect(api.sent!['currentKey'], {'kind': 'word', 'word': 'старый ключ'});
    expect(c.read(sessionProvider).refreshToken, 'refresh');
  });

  testWidgets('three card key preserves selection order on a narrow phone', (tester) async {
    final c = await open(tester, const RecoveryScreen(), size: const Size(360, 740));
    await tester.enterText(find.byKey(const Key('recovery-login')), 'watson');
    await tester.tap(find.text('Три карты'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Выбрать карты'));
    await tester.pumpAndSettle();
    final images = find.byType(CardImage);
    final ids = [for (final i in [2, 0, 1]) tester.widget<CardImage>(images.at(i)).cardId];
    for (final i in [2, 0, 1]) { await tester.tap(images.at(i)); await tester.pump(); }
    await tester.tap(find.text('Готово'));
    await tester.pumpAndSettle();
    await submit(tester);
    expect((c.read(apiProvider) as RecoveryApi).sent!['key'], {'kind': 'cards', 'cards': ids});
    expect(tester.takeException(), isNull);
  });
}
