import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Призрак отмечен маленьким значком-призраком у аватара — в партии и в лобби.
void main() {
  testWidgets('в партии значок только у Призрака', (tester) async {
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

    expect(find.byKey(const Key('ghost-badge-u1')), findsOneWidget);
    expect(find.byKey(const Key('ghost-badge-u2')), findsNothing);
    expect(find.byKey(const Key('ghost-badge-u3')), findsNothing);
  });
}
