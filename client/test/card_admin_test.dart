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
            scrollable: find.descendant(of: find.byType(ListView).last, matching: find.byType(Scrollable)).first);
        await tester.ensureVisible(find.byKey(Key(key)));
        await tester.pumpAndSettle();
      }

      await reveal('card-colors');
      await tester.enterText(find.byKey(const Key('card-colors')), 'красный 50%, белый 50%');
      await reveal('card-shapes');
      await tester.enterText(find.byKey(const Key('card-shapes')), 'цилиндр 85%');
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
      expect(saved.colors, {'red': .5, 'white': .5});
      expect(AdminCard.fromJson(saved.toJson()).shapes, {'cylinder': .85});
      expect(saved.meanings.single.weight, lessThan(1));
      expect(AdminCard.fromJson(saved.toJson()).meanings.single.tag, 'flower');
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('кабинет карточек: массовый перенос и новый набор', (tester) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.admin = true;
    app.api.cards = const [
      AdminCard(
          id: 'card-1', imageKey: 'orig_0001', setCode: 'original', isActive: true, version: 0),
      AdminCard(
          id: 'card-2', imageKey: 'orig_0002', setCode: 'original', isActive: true, version: 0),
    ];
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.more_horiz).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Карточки (кабинет)'));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('card-select-card-1')));
    await tester.pumpAndSettle();
    expect(find.text('Выбрано: 1'), findsOneWidget);
    await tester.tap(find.byKey(const Key('card-select-card-2')));
    await tester.pumpAndSettle();
    expect(find.text('Выбрано: 2'), findsOneWidget);
    await tester.tap(find.byKey(const Key('cards-move-set')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Зеркало истины').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('cards-move-apply')));
    await tester.pumpAndSettle();
    final moved = app.api.named('moveCards').single.$2;
    expect(moved[0], unorderedEquals(['card-1', 'card-2']));
    expect(moved[1], 'mirror');
    expect(find.textContaining('Выбрано:'), findsNothing);

    await tester.tap(find.byKey(const Key('cards-set-create')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('set-code')), 'winter');
    await tester.enterText(find.byKey(const Key('set-title')), 'Зимние каникулы');
    await tester.tap(find.byKey(const Key('set-create-apply')));
    await tester.pumpAndSettle();
    expect(app.api.named('createCardSet').single.$2, ['winter', 'Зимние каникулы']);
    expect(tester.takeException(), isNull);
  });

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
