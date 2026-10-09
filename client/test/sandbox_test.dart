import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  for (final width in [320.0, 1440.0]) {
    testWidgets('администратор запускает и просматривает песочницу: $width', (tester) async {
      tester.view.physicalSize = Size(width, 950); tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: watson); addTearDown(app.container.dispose);
      app.api.admin = true;
      final summary = {'id': 'r1', 'scenario': 'full-game', 'seed': 1000, 'status': 'completed', 'passed': null,
        'correctRows': 2, 'totalRows': 4, 'winner': 'Killer', 'steps': 2, 'messages': 1, 'elapsedMs': 123};
      app.api.sandboxResult = summary;
      app.api.sandboxList = [summary];
      app.api.sandboxReplayResult = {'summary': summary, 'seats': [{'id': 'u2', 'name': 'Бот Ватсон'}]};
      app.api.sandboxStepResult = {'view': snapshotJson()['view'], 'index': 0, 'total': 2, 'action': 'CastVote', 'actor': 'u2', 'messages': [
        {'author': 'u2', 'round': 1, 'text': 'Проверил связь с подсказкой.', 'cards': ['orig_0001']}
      ]};
      await tester.pumpWidget(app.widget); await tester.pumpAndSettle();
      await tester.tap(find.byIcon(Icons.more_horiz).first); await tester.pumpAndSettle();
      await tester.tap(find.text('Песочница ботов')); await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('sandbox-seed')), '123');
      await tester.ensureVisible(find.byKey(const Key('sandbox-run'))); await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('sandbox-run'))); await tester.pumpAndSettle();
      expect(app.api.named('runSandbox').single.$2, ['full-game', 123]);
      expect(find.text('ПРОСМОТР ПРОГОНА'), findsOneWidget);
      await tester.tap(find.byKey(const Key('sandbox-viewer'))); await tester.pumpAndSettle();
      await tester.tap(find.text('Бот Ватсон').last); await tester.pumpAndSettle();
      expect(app.api.named('sandboxStep').last.$2[2], 'u2');
      await tester.tap(find.byKey(const Key('sandbox-reveal'))); await tester.pumpAndSettle();
      expect(app.api.named('sandboxStep').last.$2[3], true);
      await tester.tap(find.byKey(const Key('sandbox-next'))); await tester.pumpAndSettle();
      expect(app.api.named('sandboxStep').last.$2[1], 1);
      await tester.scrollUntilVisible(find.text('Проверил связь с подсказкой.'), 300, scrollable: find.byType(Scrollable).last);
      expect(tester.takeException(), isNull);
    });
  }
  testWidgets('песочница скрыта от обычного игрока', (tester) async {
    final app = await TestApp.create(user: watson); addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget); await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.more_horiz).first); await tester.pumpAndSettle();
    expect(find.text('Песочница ботов'), findsNothing);
  });
}
