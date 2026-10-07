import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  testWidgets('в лобби виден состав ролей', (tester) async {
    final app = await TestApp.create(user: host);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.lobby = lobby();
    app.go('/lobby/l1');
    await tester.pumpAndSettle();

    expect(find.textContaining('Кооператив · раундов: 5 · Призрак, Детектив'), findsOneWidget);
  });

  testWidgets('в партии видны мои письма по раундам', (tester) async {
    tester.view.physicalSize = const Size(1200, 3000);
    tester.view.devicePixelRatio = 1.5;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.game = snapshot(phase: 'Mailbox', allowed: const ['SendLetter']);
    app.go('/game/g1');
    await tester.pumpAndSettle();

    expect(find.text('МОИ ПИСЬМА'), findsOneWidget);
    // Письмо первого раунда исчезло: маленький перечёркнутый глаз под картой.
    expect(find.byWidgetPredicate((w) => w is Icon && w.icon == Icons.visibility_off && w.size == 14), findsOneWidget);
  });
}
