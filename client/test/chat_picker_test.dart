import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  testWidgets('упоминания: чёткая сетка, свои письма и показанные карты без приватных', (tester) async {
    tester.view.physicalSize = const Size(1500, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.game = snapshot(phase: 'Discussion', allowed: const []);
    app.go('/game/g1');
    await tester.pumpAndSettle();
    for (final channel in ['public', 'killer_team']) {
      app.realtime.chatCtl.add(ChatMessage.fromJson({
        'id': channel, 'channel': channel, 'authorId': 'u3', 'kind': 'text',
        'text': 'Кидал эту', 'cardIds': [channel == 'public' ? 'orig_0400' : 'orig_0401'],
        'cardNotes': ['кидал эту'], 'round': 4, 'createdAt': '2026-10-09T12:00:00Z',
      }));
    }
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Упомянуть карту'));
    await tester.pumpAndSettle();
    final selected = find.byKey(const Key('mention-card-orig_0400'));
    expect(selected, findsOneWidget);
    expect(find.byKey(const Key('mention-card-orig_0300')), findsOneWidget, reason: 'собственное старое письмо');
    expect(find.byKey(const Key('mention-card-orig_0401')), findsNothing);
    final card = tester.widget<CardImage>(find.descendant(of: selected, matching: find.byType(CardImage)));
    final cell = tester.getSize(selected).width;
    expect(card.size, closeTo(cell, .01));
    expect(CardImage.decodeSize(card.size, 1), greaterThanOrEqualTo(cell.ceil()));
    await tester.tap(selected);
    await tester.pumpAndSettle();
    expect(find.byWidgetPredicate((w) => w is CardImage && w.cardId == 'orig_0400' && w.size == 48), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
  });
}
