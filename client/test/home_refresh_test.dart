import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  testWidgets('главная сама обновляет список партий — видно, что настал ваш ход', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    expect(find.text('Пока нет идущих партий'), findsOneWidget);

    app.api.games = [const MyGame(gameId: 'g1', status: 'active', phase: 'Mailbox', yourTurn: true, deadline: null)];
    await tester.pump(const Duration(seconds: 31));
    await tester.pumpAndSettle();

    expect(find.text('Пока нет идущих партий'), findsNothing);
    expect(find.text('Ваш ход'), findsWidgets);
  });
}
