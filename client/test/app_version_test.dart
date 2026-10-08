import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/app_version.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  test('подпись версии: сборка из CI и локальная', () {
    final ci = AppVersion(version: '0.2.0', build: 57, date: DateTime(2026, 10, 8, 17, 20));
    expect(ci.label, 'Версия 0.2.0 · сборка 57 · 8 окт 2026, 17:20');
    expect(const AppVersion(version: '0.2.0', build: 0).label, 'Версия 0.2.0 · сборка для разработки');
  });

  test('версия с сервера читается из version.json', () {
    final v = AppVersion.fromJson({'version': '0.2.0', 'build': 60, 'date': '2026-10-08T14:20:00Z'});
    expect(v.build, 60);
    expect(v.date!.toUtc(), DateTime.utc(2026, 10, 8, 14, 20));
  });

  test('обновление предлагаем только Android-сборке из CI, если на сервере новее', () {
    const installed = AppVersion(version: '0.2.0', build: 57);
    const newer = AppVersion(version: '0.2.0', build: 60);
    const same = AppVersion(version: '0.2.0', build: 57);
    expect(shouldOfferUpdate(installed, newer, platform: TargetPlatform.android, web: false), isTrue);
    expect(shouldOfferUpdate(installed, same, platform: TargetPlatform.android, web: false), isFalse);
    expect(shouldOfferUpdate(installed, newer, platform: TargetPlatform.iOS, web: false), isFalse);
    expect(shouldOfferUpdate(installed, newer, platform: TargetPlatform.android, web: true), isFalse, reason: 'веб обновляется сам');
    expect(shouldOfferUpdate(const AppVersion(version: '0.2.0', build: 0), newer, platform: TargetPlatform.android, web: false), isFalse,
        reason: 'локальная сборка разработчика');
    expect(shouldOfferUpdate(installed, null, platform: TargetPlatform.android, web: false), isFalse);
  });

  testWidgets('на главной видна версия приложения', (tester) async {
    final app = await TestApp.create(user: host);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    await tester.scrollUntilVisible(find.byKey(const Key('app-version')), 200);
    expect(find.textContaining('Версия 0.1.0'), findsOneWidget);
  });
}
