import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

Future<TestApp> _open(WidgetTester tester) async {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.625;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  app.realtime.game = snapshot(phase: 'Mailbox', allowed: const []);
  app.go('/game/g1');
  await tester.pumpAndSettle();
  return app;
}

void main() {
  testWidgets('заметка об игроке: подозрение кнопкой, метка над игроком', (tester) async {
    final app = await _open(tester);

    await tester.tap(find.byKey(const Key('player-u3')));
    await tester.pumpAndSettle();
    expect(find.text('МАРПЛ'), findsOneWidget);
    expect(find.text('ФАКТЫ ПАРТИИ · АВТОМАТИЧЕСКИ'), findsOneWidget);

    await tester.tap(find.byKey(const Key('suspicion-2')));
    await tester.pump();
    await tester.enterText(find.byType(TextField), 'Путается в показаниях');
    await tester.ensureVisible(find.byKey(const Key('save-note')));
    await tester.tap(find.byKey(const Key('save-note')));
    await tester.pumpAndSettle();

    expect(app.api.named('saveNote').single.$2, ['u3', 2, 'Путается в показаниях']);
    expect(find.text('У!'), findsOneWidget);
  });

  testWidgets('подсказку можно рассмотреть крупно', (tester) async {
    await _open(tester);

    await tester.tap(find.byKey(const Key('hint-orig_0100')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('card-zoom')), findsOneWidget);
    expect(find.text('Первая зацепка'), findsOneWidget);

    await tester.tap(find.byKey(const Key('card-zoom')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('card-zoom')), findsNothing);
  });
}
