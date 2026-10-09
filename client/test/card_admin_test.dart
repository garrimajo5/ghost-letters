import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/admin_cards.dart';
import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  for (final width in [320.0, 1440.0]) {
    testWidgets('кабинет карточек: редактирование на ширине $width',
        (tester) async {
      tester.view.physicalSize = Size(width, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: watson);
      addTearDown(app.container.dispose);
      app.api.admin = true;
      app.api.cards = const [
        AdminCard(
            id: 'card-1',
            imageKey: 'orig_0001',
            setCode: 'original',
            isActive: true,
            version: 3,
            tags: ['red'],
            meanings: [CardFeature(tag: 'flower', label: 'Цветок', weight: 1)])
      ];
      await tester.pumpWidget(app.widget);
      await tester.pumpAndSettle();
      await tester.tap(find.byIcon(Icons.more_horiz).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Карточки (кабинет)'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.tap(find.byKey(const Key('admin-card-card-1')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('card-title')), 'Роза');
      await tester.tap(find.byKey(const Key('card-active')));
      await tester.tap(find.byKey(const Key('card-set')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Зеркало истины').last);
      await tester.pumpAndSettle();
      Future<void> reveal(String key) async {
        await tester.scrollUntilVisible(find.byKey(Key(key)), 180,
            scrollable: find.byType(Scrollable).last);
        await tester.ensureVisible(find.byKey(Key(key)));
        await tester.pumpAndSettle();
      }

      await reveal('add-meaning');
      expect(find.text('Цветок'), findsOneWidget);
      await tester.drag(find.byType(Slider).first, const Offset(-80, 0));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('add-meaning')));
      await tester.pumpAndSettle();
      // Removing a draft must not dispose controllers still attached to a field.
      await tester.ensureVisible(find.byTooltip('Удалить признак').last);
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Удалить признак').last);
      await tester.pumpAndSettle();
      await reveal('card-save');
      await tester.tap(find.byKey(const Key('card-save')));
      await tester.pumpAndSettle();
      final saved = app.api.named('saveCard').single.$2.single as AdminCard;
      expect(saved.title, 'Роза');
      expect(saved.isActive, false);
      expect(saved.setCode, 'mirror');
      expect(saved.version, 3);
      expect(saved.meanings.single.weight, lessThan(1));
      expect(AdminCard.fromJson(saved.toJson()).meanings.single.tag, 'flower');
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('обычный игрок не видит кабинет карточек', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.more_horiz).first);
    await tester.pumpAndSettle();
    expect(find.text('Карточки (кабинет)'), findsNothing);
  });
}
