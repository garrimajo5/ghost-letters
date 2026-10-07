import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  testWidgets('правила открываются из меню главной', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    await tester.tap(find.byIcon(Icons.more_horiz));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Правила'));
    await tester.pumpAndSettle();

    expect(find.text('ПРАВИЛА'), findsOneWidget);
    expect(find.text('ЦЕЛЬ'), findsOneWidget);
    await tester.scrollUntilVisible(find.text('ПОДРАЖАТЕЛЬ'), 300);
    expect(find.text('ПОДРАЖАТЕЛЬ'), findsOneWidget);
  });
}
