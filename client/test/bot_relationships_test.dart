import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';
import 'bots_test.dart' show lobbyWithBot;

void main() {
  testWidgets('профиль с симпатиями открывается по аватарке бота в лобби', (tester) async {
    final app = await TestApp.create(user: host);
    addTearDown(app.container.dispose);
    app.api.profileIsBot = true;
    app.realtime.lobby = lobbyWithBot();
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.go('/lobby/l1');
    await tester.pumpAndSettle();
    final avatar = find.byKey(const Key('lobby-profile-b1'));
    await tester.ensureVisible(avatar);
    await tester.tap(avatar);
    await tester.pumpAndSettle();
    expect(find.text('СИМПАТИИ К ИГРОКАМ'), findsOneWidget);
  });

  for (final admin in [false, true]) {
    testWidgets('профиль бота: симпатии и доступ к причинам, admin=$admin', (tester) async {
      tester.view.physicalSize = const Size(360, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: watson);
      addTearDown(app.container.dispose);
      app.api.admin = admin;
      app.api.profileIsBot = true;
      app.api.relationships = [BotRelationship(player: watson, score: 27, attitude: 'Симпатизирует', sharedGames: 5,
        updatedAt: DateTime(2026), components: const {'honesty': 12.5, 'skill': -2})];
      await tester.pumpWidget(app.widget);
      await tester.pumpAndSettle();
      app.go('/profile/b1');
      await tester.pumpAndSettle();
      expect(find.text('СИМПАТИИ К ИГРОКАМ'), findsOneWidget);
      expect(find.text('+27'), findsOneWidget);
      expect(find.textContaining('Симпатизирует'), findsOneWidget);
      final details = find.byKey(Key('affinity-details-${watson.id}'));
      if (admin) {
        await tester.ensureVisible(details);
        await tester.tap(details);
        await tester.pumpAndSettle();
        expect(find.text('Честность о письмах'), findsOneWidget);
        expect(find.text('+12.5'), findsOneWidget);
        expect(app.api.named('botRelationships').any((call) => call.$2[1] == true), isTrue);
      } else {
        expect(details, findsNothing);
        expect(find.text('Честность о письмах'), findsNothing);
        expect(app.api.named('botRelationships').every((call) => call.$2[1] == false), isTrue);
      }
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('новый бот: нейтральные отношения без завершённых игр', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.profileIsBot = true;
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.go('/profile/b1');
    await tester.pumpAndSettle();
    expect(find.textContaining('Пока нет совместных'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  test('социальные настройки сохраняются при изменении игровых черт', () {
    const original = BotSpectra(social: {'influence': 0, 'skillRespect': .2, 'expectedActivity': .9});
    final restored = BotSpectra.fromJson(original.copyWith(risk: 1).toJson());
    expect(restored.social, original.social);
    expect(restored.risk, 1);
  });
}
