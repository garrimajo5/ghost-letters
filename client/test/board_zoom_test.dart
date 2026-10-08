import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// «Стол крупно»: поле и подсказки во весь экран, приближение пальцами и кнопками.
Future<TestApp> _open(WidgetTester tester) async {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.625;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  final snap = snapshot(phase: 'Discussion', allowed: const ['RaiseHand']);
  app.realtime.game = snap;
  app.api.snapshotResult = snap;
  app.go('/game/g1');
  await tester.pumpAndSettle();
  return app;
}

double _scale(WidgetTester tester) =>
    tester.widget<InteractiveViewer>(find.byKey(const Key('board-zoom'))).transformationController!.value.getMaxScaleOnAxis();

void main() {
  testWidgets('стол крупно: открывается, приближается и закрывается', (tester) async {
    await _open(tester);

    await tester.tap(find.byKey(const Key('zoom-board')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('board-zoom')), findsOneWidget);
    expect(find.byKey(const Key('board-0-0')), findsOneWidget);
    expect(find.byKey(const Key('hint-orig_0100')), findsOneWidget, reason: 'подсказки тоже на крупном столе');
    // На телефоне сначала видно всё поле целиком — масштаб меньше 1.
    final fit = _scale(tester);
    expect(fit, lessThan(1));

    await tester.tap(find.byKey(const Key('zoom-in')));
    await tester.pump();
    expect(_scale(tester), closeTo(fit * 1.5, 0.001));

    await tester.tap(find.byKey(const Key('zoom-out')));
    await tester.tap(find.byKey(const Key('zoom-out')));
    await tester.pump();
    expect(_scale(tester), closeTo(fit, 0.001), reason: 'мельче, чем «целиком», не отдаляем');

    await tester.tap(find.byKey(const Key('zoom-close')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('board-zoom')), findsNothing);
    expect(find.byKey(const Key('zoom-board')), findsOneWidget);
  });

  testWidgets('на крупном столе карта открывает пометки, как обычно', (tester) async {
    await _open(tester);
    await tester.tap(find.byKey(const Key('zoom-board')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('zoom-in')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-0-0')), warnIfMissed: false);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('mark-crosses')), findsOneWidget);
  });
}
