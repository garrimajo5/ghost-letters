import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/theme.dart';
import 'package:ghost_letters/features/lobby/settings_sheet.dart';
import 'package:ghost_letters/models/models.dart';

/// Настройки лобби: Сообщник вместо Детектива (6 игроков: Призрак, Убийца, Сообщник, 3 Детектива).
void main() {
  Future<LobbySettings?> edit(WidgetTester tester, LobbySettings initial, Future<void> Function() steps) async {
    tester.view.physicalSize = const Size(1200, 4000);
    tester.view.devicePixelRatio = 1.5;
    addTearDown(tester.view.reset);
    LobbySettings? result;
    await tester.pumpWidget(MaterialApp(
      theme: AppTheme.build(),
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: TextButton(
              onPressed: () async => result = await SettingsSheet.show(context, initial, players: 6),
              child: const Text('open'),
            ),
          ),
        ),
      ),
    ));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    await steps();
    await tester.ensureVisible(find.text('Сохранить'));
    await tester.tap(find.text('Сохранить'));
    await tester.pumpAndSettle();
    return result;
  }

  String value(WidgetTester tester) => tester.widget<Text>(find.byKey(const Key('extra-accomplices-value'))).data!;

  testWidgets('хост добавляет Сообщника вместо Детектива', (tester) async {
    final result = await edit(tester, const LobbySettings(), () async {
      await tester.ensureVisible(find.byKey(const Key('extra-accomplices')));
      expect(value(tester), '0');
      await tester.tap(find.byKey(const Key('extra-accomplices-plus')));
      await tester.pump();
      expect(value(tester), '1');
    });
    expect(result?.roles.extraAccomplices, 1);
    expect((result!.toJson()['roles'] as Map)['extraAccomplices'], 1, reason: 'уходит на сервер');
  });

  testWidgets('не больше двух; без Убийцы — выключено', (tester) async {
    await edit(tester, const LobbySettings(), () async {
      await tester.ensureVisible(find.byKey(const Key('extra-accomplices')));
      await tester.tap(find.byKey(const Key('extra-accomplices-plus')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('extra-accomplices-plus')));
      await tester.pump();
      expect(value(tester), '2');
      expect(tester.widget<IconButton>(find.byKey(const Key('extra-accomplices-plus'))).onPressed, isNull);

      await tester.tap(find.widgetWithText(SwitchListTile, 'Убийца'));
      await tester.pump();
      expect(tester.widget<IconButton>(find.byKey(const Key('extra-accomplices-minus'))).onPressed, isNull);
    });
  });

  test('из JSON и обратно; старые лобби без поля — 0', () {
    expect(RoleOptions.fromJson(const {'killerEnabled': true}).extraAccomplices, 0);
    expect(RoleOptions.fromJson(const RoleOptions(extraAccomplices: 2).toJson()).extraAccomplices, 2);
  });
}
