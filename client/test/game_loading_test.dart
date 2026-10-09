import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/game/game_screen.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

ChatMessage message(String id) => ChatMessage.fromJson({
      'id': id, 'channel': 'public', 'authorId': 'u3', 'kind': 'text',
      'text': 'Сообщение $id', 'cardIds': <String>[], 'round': 4,
      'createdAt': '2026-10-09T12:00:00Z',
    });

Future<TestApp> prepare(WidgetTester tester, {double width = 390}) async {
  tester.view.physicalSize = Size(width, 1100);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  app.realtime.game = snapshot(phase: 'Discussion', allowed: const []);
  app.realtime.lobby = lobby();
  return app;
}

Future<void> finish(WidgetTester tester) async {
  await tester.pumpWidget(const SizedBox());
  await tester.pump();
}

void main() {
  for (final width in [390.0, 1500.0]) {
    testWidgets('чат обновляется без перестроения поля при ширине $width', (tester) async {
      final app = await prepare(tester, width: width);
      app.go('/game/g1');
      await tester.pumpAndSettle();
      final screen = tester.state<GameScreenState>(find.byType(GameScreen));
      final card = tester.widget(find.byKey(const Key('board-0-0')));
      app.realtime.chatCtl.add(message('live'));
      app.realtime.chatCtl.add(message('live'));
      await tester.pumpAndSettle();
      expect(identical(tester.widget(find.byKey(const Key('board-0-0'))), card), isTrue,
          reason: 'виджет карты не пересоздаётся из-за сообщения');
      expect(screen.chat.length, 1);
      expect(screen.unread, width < 1400 ? 1 : 0);
      if (width < 1400) {
        expect(find.descendant(of: find.byTooltip('Чат'), matching: find.text('1')), findsOneWidget);
        await tester.tap(find.byTooltip('Чат'));
        await tester.pumpAndSettle();
        expect(screen.unread, 0);
      }
      expect(find.text('Сообщение live'), findsOneWidget);
      app.realtime.chatCtl.add(message('next'));
      await tester.pumpAndSettle();
      expect(find.text('Сообщение next'), findsOneWidget);
      expect(screen.unread, 0);
      await finish(tester);
    });
  }

  testWidgets('пометки и заметки загружаются, пока история чата ещё не пришла', (tester) async {
    final app = await prepare(tester);
    final history = Completer<List<ChatMessage>>();
    var started = 0;
    app.api.chatLoad = () { started++; return history.future; };
    app.api.marksLoad = () async { started++; return [{'cardId': 'orig_0001', 'excluded': true}]; };
    app.api.notesLoad = () async { started++; return [{'targetUserId': 'u3', 'suspicion': 2}]; };
    app.go('/game/g1');
    await tester.pumpAndSettle();
    final screen = tester.state<GameScreenState>(find.byType(GameScreen));
    expect(started, 3);
    expect(screen.marks, contains('orig_0001'));
    expect(screen.suspicion['u3'], 2);
    expect(screen.lobby, isNotNull);
    await tester.tap(find.byTooltip('Чат'));
    await tester.pumpAndSettle();
    app.realtime.chatCtl.add(message('live'));
    await tester.pumpAndSettle();
    history.complete([message('old'), message('live')]);
    await tester.pumpAndSettle();
    expect(screen.chat.map((m) => m.id), ['old', 'live']);
    expect(find.text('Сообщение old'), findsOneWidget);
    expect(find.text('Сообщение live'), findsOneWidget);
    await finish(tester);
  });

  testWidgets('ошибка истории не прячет поле; повтор загружает только историю', (tester) async {
    final app = await prepare(tester);
    var chatCalls = 0;
    var markCalls = 0;
    app.api.chatLoad = () async {
      if (++chatCalls == 1) throw Exception('offline');
      return [message('retry')];
    };
    app.api.marksLoad = () async { markCalls++; return []; };
    app.go('/game/g1');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('board-0-0')), findsOneWidget);
    await tester.tap(find.text('Повторить'));
    await tester.pumpAndSettle();
    expect(chatCalls, 2);
    expect(markCalls, 1);
    expect(tester.state<GameScreenState>(find.byType(GameScreen)).chat.single.id, 'retry');
    await finish(tester);
  });

  testWidgets('возврат обновляет полный снимок без дополнительного HTTP-запроса', (tester) async {
    final app = await prepare(tester);
    app.go('/game/g1');
    await tester.pumpAndSettle();
    final fresh = snapshotJson(phase: 'Mailbox', allowed: const []);
    (fresh['view'] as Json)['version'] = 43;
    (fresh['roster'] as List)[2]['nickname'] = 'Новое имя';
    app.realtime.game = GameSnapshot.fromJson(fresh);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();
    final screen = tester.state<GameScreenState>(find.byType(GameScreen));
    expect(app.realtime.resyncs, 1);
    expect(app.api.named('snapshot'), isEmpty);
    expect(screen.view!.version, 43);
    expect(screen.view!.phase, 'Mailbox');
    expect(screen.nick('u3'), 'Новое имя');
    app.realtime.snapshotsCtl.add(snapshot(version: 41));
    await tester.pumpAndSettle();
    expect(screen.view!.version, 43, reason: 'устаревший снимок не откатывает партию');
    await finish(tester);
  });
}
