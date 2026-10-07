import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  testWidgets('долгое нажатие на чужое сообщение добавляет цитату в заметку', (tester) async {
    tester.view.physicalSize = const Size(1200, 3000);
    tester.view.devicePixelRatio = 1.5;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.game = snapshot(phase: 'Discussion', allowed: const ['RaiseHand']);
    app.api.notesResult = [
      {'targetUserId': 'u3', 'suspicion': 1, 'body': 'Подозрительно молчит', 'entries': <Object>[], 'updatedAt': '2026-10-07T10:00:00Z'},
    ];
    app.go('/game/g1');
    await tester.pumpAndSettle();

    app.realtime.chatCtl.add(ChatMessage.fromJson({
      'id': 'm1',
      'channel': 'public',
      'authorId': 'u3',
      'kind': 'text',
      'text': 'Я отправила ключ',
      'cardIds': <String>[],
      'createdAt': '2026-10-07T10:01:00Z',
      'round': 2,
    }));
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Чат'));
    await tester.pumpAndSettle();
    await tester.longPress(find.text('Я отправила ключ'));
    await tester.pumpAndSettle();

    final saved = app.api.named('saveNote').single.$2;
    expect(saved[0], 'u3');
    expect(saved[1], 1, reason: 'подозрение сохраняется');
    expect(saved[2], 'Подозрительно молчит\nРаунд 2: «Я отправила ключ»');
    expect(find.text('Цитата добавлена в заметку о Марпл'), findsOneWidget);
  });
}
