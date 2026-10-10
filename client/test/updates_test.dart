import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/app_version.dart';
import 'package:ghost_letters/features/updates/updates_screen.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  test('history starts with the installed release and has unique versions', () {
    expect(releaseHistory.first.version, AppVersion.current.version);
    expect(releaseHistory.map((r) => r.version).toSet().length,
        releaseHistory.length);
    expect(
        releaseHistory.every((r) => r.added.isNotEmpty || r.fixed.isNotEmpty),
        isTrue);
  });

  for (final width in [320.0, 1440.0]) {
    testWidgets('Что нового открывается с главной и прокручивается: $width',
        (tester) async {
      tester.view.physicalSize = Size(width, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: host);
      addTearDown(app.container.dispose);
      await tester.pumpWidget(app.widget);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(PopupMenuButton<String>).first);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Что нового'));
      await tester.tap(find.text('Что нового'));
      await tester.pumpAndSettle();
      expect(find.byType(UpdatesScreen), findsOneWidget);
      expect(find.text('Версия 0.1.1'), findsOneWidget);
      await tester.scrollUntilVisible(find.text('Версия 0.1.0'), 200,
          scrollable: find
              .descendant(
                  of: find.byType(UpdatesScreen),
                  matching: find.byType(Scrollable))
              .first);
      expect(tester.takeException(), isNull);
      await tester.tap(find.byType(BackButton));
      await tester.pumpAndSettle();
      expect(find.byType(UpdatesScreen), findsNothing);
    });
  }
}
