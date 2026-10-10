import 'dart:io';
import 'dart:ui' as ui;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/config.dart';
import 'package:ghost_letters/models/models.dart';
import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  testWidgets('typing pauses, references mark cards, hints expand below table', (tester) async {
    tester.view.physicalSize = const Size(1440, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    final snap = GameSnapshot.fromJson(snapshotJson(phase: 'Discussion', allowed: []));
    app.realtime.game = snap;
    app.api.snapshotResult = snap;
    app.go('/game/g1');
    await tester.pumpAndSettle();
    const text = 'Исключаю мотив 1. Эта карта противоречит моей версии, но я ещё могу передумать.';
    app.realtime.chatCtl.add(ChatMessage(id: 'typing', channel: 'public', authorId: 'u3',
      kind: 'text', text: text, cardIds: ['orig_0100', 'orig_0001'],
      cardNotes: ['улика', 'исключаю:0'], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 175));
    String shown() => tester.widget<Text>(find.byKey(const Key('dossier-statement'))).data!;
    expect(shown(), 'Исклю');
    expect(find.byKey(const ValueKey('dossier-mark-orig_0001')), findsNothing);
    await tester.tap(find.text('Пауза'));
    await tester.pump(const Duration(seconds: 2));
    expect(shown(), 'Исклю');
    await tester.tap(find.text('Продолжить'));
    await tester.pump(const Duration(milliseconds: 600));
    expect(shown(), startsWith('Исключаю мотив 1.'));
    final badge = find.byKey(const ValueKey('dossier-mark-orig_0001'));
    expect(badge, findsOneWidget);
    expect(find.descendant(of: badge, matching: find.byIcon(Icons.close)), findsOneWidget);
    await tester.tap(find.text('Показать сразу'));
    await tester.pump();
    expect(shown(), text);
    final hints = find.byKey(const Key('dossier-bottom-hints'));
    expect(hints, findsOneWidget);
    await tester.ensureVisible(hints);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('hint-orig_0100')), findsOneWidget);
    await tester.tap(find.text('Подсказки Призрака · по раундам'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('hint-orig_0100')).hitTestable(), findsNothing);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
  }, skip: !AppConfig.dossierDesign);

  for (final width in [320.0, 1440.0]) {
    testWidgets('dossier public versions, playback and editor at $width', (tester) async {
      tester.view.physicalSize = Size(width, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: watson);
      addTearDown(app.container.dispose);
      final imageKey = GlobalKey();
      await tester.pumpWidget(RepaintBoundary(key: imageKey, child: app.widget));
      await tester.pumpAndSettle();
      final snap = GameSnapshot.fromJson(snapshotJson(phase: 'Discussion', allowed: []));
      app.realtime.game = snap;
      app.api.snapshotResult = snap;
      app.go('/game/g1');
      await tester.pumpAndSettle();
      void send(String id, String author, String text, {String channel = 'public'}) {
        app.realtime.chatCtl.add(ChatMessage(id: id, channel: channel,
          authorId: author, kind: 'text', text: text,
          cardIds: ['orig_0100', 'orig_0001'], cardNotes: ['улика', 'думаю, эта:0'],
          createdAt: DateTime.now(), round: 4));
      }
      send('1', 'u3', 'Подсказка поддерживает первую карту');
      await tester.pump();
      await tester.pump(const Duration(seconds: 3));
      expect(tester.widget<Text>(find.byKey(const Key('dossier-statement'))).data, 'Подсказка поддерживает первую карту');
      expect(find.byKey(const ValueKey('dossier-author-u3')), findsNothing);
      send('3', 'u3', 'Секретная версия', channel: 'killer_team');
      await tester.pump();
      expect(find.text('Секретная версия'), findsNothing);
      final board = tester.getSize(find.byKey(const Key('board-0-0')));
      final hint = tester.getSize(find.byKey(const Key('hint-orig_0100')));
      expect(hint.width, closeTo(board.width, 1));
      if (width >= 1440) expect(board.width, greaterThan(130));
      const captures = String.fromEnvironment('DOSSIER_SCREENSHOTS');
      if (captures.isNotEmpty) {
        await tester.pumpAndSettle();
        await tester.runAsync(() async {
          final boundary = imageKey.currentContext!.findRenderObject()! as RenderRepaintBoundary;
          final image = await boundary.toImage();
          final png = await image.toByteData(format: ui.ImageByteFormat.png);
          await File('$captures/dossier-$width.png').writeAsBytes(png!.buffer.asUint8List());
          image.dispose();
        });
      }
      await tester.ensureVisible(find.byKey(const Key('dossier-edit')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('dossier-edit')));
      await tester.pumpAndSettle();
      expect(find.text('Моя версия на столе'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
    }, skip: !AppConfig.dossierDesign);
  }

  testWidgets('evidence board: threads, pins, checks, a new thread and one post', (tester) async {
    tester.view.physicalSize = const Size(1440, 1200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    final j = snapshotJson(phase: 'Discussion', allowed: []);
    (j['view'] as Map<String, dynamic>)['table'] = {
      'threads': [
        {'id': 1, 'author': 'u3', 'round': 1, 'sourceKind': 'Hint', 'source': 'orig_0100', 'target': 'orig_0001',
          'stance': 'For', 'reason': 'цвет', 'endorsedBy': <String>[], 'disputedBy': <String>[]},
      ],
      'pins': [{'author': 'u3', 'row': 0, 'column': 0}],
      'checks': [{'author': 'u3', 'card': 'orig_0002'}],
      'claims': <Object>[],
      'canPost': true,
      'pinsOnly': false,
    };
    final snap = GameSnapshot.fromJson(j);
    app.realtime.game = snap;
    app.api.snapshotResult = snap;
    app.go('/game/g1');
    await tester.pumpAndSettle();

    expect(find.byKey(const ValueKey('table-pins-orig_0001')), findsOneWidget);
    expect(find.byKey(const ValueKey('table-check-orig_0002')), findsOneWidget);
    expect(find.byKey(const Key('layer-u3')), findsOneWidget);
    expect(find.text('МОЯ ВЕРСИЯ: 0 ИЗ 2 РЯДОВ'), findsOneWidget);

    Future<void> tapKey(Key key) async {
      await tester.ensureVisible(find.byKey(key));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(key));
      await tester.pumpAndSettle();
    }

    // Новая нить «против» с причиной и булавкой.
    await tapKey(const Key('hint-orig_0100'));
    expect(tester.widget<Text>(find.byKey(const Key('table-prompt'))).data, startsWith('Улика в руке'));
    await tapKey(const ValueKey('table-card-orig_0003'));
    expect(find.text('УЖЕ НА СТОЛЕ'), findsNothing);
    await tester.tap(find.byKey(const Key('reason-форма')));
    await tester.tap(find.byKey(const Key('thread-pin')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('thread-against')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('table-draft-1')), findsOneWidget);

    // Согласие с чужой нитью между теми же картами.
    await tapKey(const Key('hint-orig_0100'));
    await tapKey(const ValueKey('table-card-orig_0001'));
    expect(find.text('УЖЕ НА СТОЛЕ'), findsOneWidget);
    await tester.tap(find.byKey(const Key('thread-endorse-1')));
    await tester.pumpAndSettle();
    expect(find.text('ВЫЛОЖИТЬ НА СТОЛ (3)'), findsOneWidget);

    await tapKey(const Key('table-post'));
    final sent = app.api.calls.lastWhere((c) => c.$1 == 'command').$2;
    expect(sent[0], 'TablePost');
    final ops = ((sent[1] as Map)['ops'] as List).cast<Map>();
    expect(ops.map((o) => o['kind']), ['Link', 'Pin', 'Endorse']);
    expect(ops[0], {'kind': 'Link', 'sourceKind': 'Hint', 'source': 'orig_0100', 'target': 'orig_0003',
      'stance': 'Against', 'reason': 'форма'});
    expect(ops[1], {'kind': 'Pin', 'target': 'orig_0003'});
    expect(ops[2], {'kind': 'Endorse', 'thread': 1});
    expect(find.byKey(const Key('table-draft-0')), findsNothing);

    // Слой «Спорные»: приглушены карты без споров, ошибок отрисовки нет.
    await tapKey(const Key('layer-conflict'));
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
  }, skip: !AppConfig.dossierDesign);
}
