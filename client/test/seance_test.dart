import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/config.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Сеанс — только в сборке «Досье» (DOSSIER_DESIGN=true), как и dossier_test.
Future<TestApp> _open(WidgetTester tester, Size size, {String phase = 'Discussion', List<String> allowed = const []}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson, classicTable: false);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(RepaintBoundary(key: _shotKey, child: app.widget));
  await tester.pumpAndSettle();
  final j = snapshotJson(phase: phase, allowed: allowed);
  final view = j['view'] as Json;
  view['board'] = [
    for (final (i, cat) in const ['Motive', 'Place', 'Method', 'Secret'].indexed)
      {'category': cat, 'cards': [for (var c = 0; c < 5; c++) 'orig_0${(i * 5 + c + 1).toString().padLeft(3, '0')}']},
  ];
  view['finale'] = null;
  view['hints'] = [
    {'round': 0, 'cards': ['orig_0100']},
    {'round': 1, 'cards': ['orig_0101', 'orig_0102']},
  ];
  app.realtime.game = GameSnapshot.fromJson(j);
  app.go('/game/g1');
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 500));
  return app;
}

final _shotKey = GlobalKey();

/// Снимок экрана для проверки вида: только при --dart-define=TABLE_SCREENSHOTS=<папка>.
Future<void> _shot(WidgetTester tester, String name) async {
  const dir = String.fromEnvironment('TABLE_SCREENSHOTS');
  if (dir.isEmpty) return;
  await tester.runAsync(() async {
    final boundary = _shotKey.currentContext!.findRenderObject()! as RenderRepaintBoundary;
    final image = await boundary.toImage();
    final png = await image.toByteData(format: ui.ImageByteFormat.png);
    await Directory(dir).create(recursive: true);
    await File('$dir/seance-$name.png').writeAsBytes(png!.buffer.asUint8List());
    image.dispose();
  });
}

void main() {
  const portrait = Size(390, 844);
  const phone = Size(800, 360);

  for (final size in const [portrait, phone]) {
    testWidgets('сеанс ${size.width.toInt()}×${size.height.toInt()}: круг, сектора со свечами, поле и веер', (tester) async {
      await _open(tester, size);

      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('seance-table')), findsOneWidget);
      expect(find.byKey(const Key('seance-circle')), findsOneWidget);
      for (var i = 0; i <= 4; i++) {
        expect(find.byKey(Key('seance-sector-$i')), findsOneWidget);
      }
      expect(find.byKey(const Key('hint-orig_0100')), findsOneWidget);
      expect(find.byKey(const Key('hint-orig_0102')), findsOneWidget);
      expect(find.byKey(const Key('board-3-4')), findsOneWidget);
      expect(find.byKey(const Key('main-fan')), findsOneWidget);
      expect(find.byKey(const Key('ghost-badge-u1')), findsOneWidget);
      await _shot(tester, '${size.width.toInt()}x${size.height.toInt()}');
    }, skip: !AppConfig.dossierDesign);
  }

  testWidgets('сеанс: пока дух читает письма, ящик светится', (tester) async {
    await _open(tester, portrait, phase: 'GhostPick');
    expect(find.byKey(const Key('seance-mailbox-glow')), findsOneWidget);
    expect(find.text('Дух читает письма'), findsOneWidget);
    await _shot(tester, 'ghostpick');
    await tester.pumpWidget(const SizedBox());
  }, skip: !AppConfig.dossierDesign);

  testWidgets('сеанс: письма отправляют — ящик без свечения', (tester) async {
    await _open(tester, portrait, phase: 'Mailbox', allowed: const ['SendLetter']);
    expect(find.byKey(const Key('seance-mailbox')), findsOneWidget);
    expect(find.byKey(const Key('seance-mailbox-glow')), findsNothing);
  }, skip: !AppConfig.dossierDesign);

  testWidgets('сеанс: в центре — карта, которую показал говорящий', (tester) async {
    final app = await _open(tester, portrait);
    app.realtime.chatCtl.add(ChatMessage(
        id: 's1', channel: 'public', authorId: 'u3', kind: 'text', text: 'Со стола: эта улика; указывает на Мотив 1.',
        cardIds: const ['orig_0100', 'orig_0001'], cardNotes: const ['улика', 'думаю, эта:0'], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.byKey(const Key('seance-shown-orig_0100')), findsOneWidget);
    expect(find.byKey(const Key('say-mark-orig_0001')), findsOneWidget);
    await _shot(tester, 'statement');
  }, skip: !AppConfig.dossierDesign);

  testWidgets('сеанс: меню → «Прежнее досье» и обратно', (tester) async {
    await _open(tester, portrait);
    await tester.tap(find.byKey(const Key('main-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Прежнее досье'));
    await tester.pump(const Duration(milliseconds: 500));
    expect(find.byKey(const Key('seance-table')), findsNothing);
    expect(find.text('ЗА СТОЛОМ'), findsOneWidget);
  }, skip: !AppConfig.dossierDesign);
}
