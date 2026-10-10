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
      expect(find.text('Подсказка поддерживает первую карту'), findsOneWidget);
      await tester.ensureVisible(find.byKey(const ValueKey('dossier-author-u3')));
      await tester.tap(find.byKey(const ValueKey('dossier-author-u3')));
      await tester.pumpAndSettle();
      send('2', 'u2', 'Чужая версия');
      send('3', 'u3', 'Секретная версия', channel: 'killer_team');
      await tester.pump();
      expect(find.text('Подсказка поддерживает первую карту'), findsOneWidget);
      expect(find.text('Секретная версия'), findsNothing);
      send('4', 'u3', 'Пересмотренная версия');
      await tester.pump();
      expect(find.text('Пересмотренная версия'), findsOneWidget);
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
}
