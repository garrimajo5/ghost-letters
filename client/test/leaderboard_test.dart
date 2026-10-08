import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Таблица лидеров: по умолчанию без ботов, галочка «Показать ботов» добавляет их.
void main() {
  testWidgets('рейтинг игроков: галочка показывает ботов', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.go('/leaderboard');
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('leader-u2')), findsOneWidget);
    expect(find.text('Бот Лестрейд'), findsNothing);
    expect(app.api.named('leaderboard').last.$2[0], isFalse);

    await tester.tap(find.byKey(const Key('show-bots')));
    await tester.pumpAndSettle();

    expect(app.api.named('leaderboard').last.$2[0], isTrue);
    expect(find.text('Бот Лестрейд'), findsOneWidget);
    expect(find.textContaining('· бот'), findsOneWidget);
  });
}
