import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Кабинет ботов (только админ) и выбор бота в лобби.
Future<TestApp> _start(WidgetTester tester, {required bool admin, User user = watson}) async {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.625;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: user);
  addTearDown(app.container.dispose);
  app.api.admin = admin;
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  return app;
}

void main() {
  Future<void> reveal(WidgetTester tester, String key, {double delta = 200}) async {
    final target = find.byKey(Key(key));
    await tester.scrollUntilVisible(target, delta, scrollable: find.byType(Scrollable).last);
    await tester.ensureVisible(target);
    // A newly revealed control may still have its old layout before this pump.
    await tester.pumpAndSettle();
  }

  testWidgets('не админ: пункта «Боты» в меню нет', (tester) async {
    await _start(tester, admin: false);

    await tester.tap(find.byIcon(Icons.more_horiz).first);
    await tester.pumpAndSettle();
    expect(find.text('Боты (кабинет)'), findsNothing);
  });

  testWidgets('админ: готовые характеры, новый бот со спектрами', (tester) async {
    final app = await _start(tester, admin: true);

    await tester.tap(find.byIcon(Icons.more_horiz).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Боты (кабинет)'));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('bot-presets')));
    await tester.pumpAndSettle();
    expect(find.text('Бот Пуаро'), findsOneWidget);
    expect(find.text('Бот Коломбо'), findsOneWidget);

    await tester.tap(find.byKey(const Key('bot-new')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('bot-name')), 'Холмс');
    expect(find.text('Смысл 50% · форма 25% · цвет 25%'), findsOneWidget);
    await reveal(tester, 'spectrum-influence');
    await tester.drag(find.byKey(const Key('spectrum-influence')), const Offset(-600, 0));
    await tester.pumpAndSettle();
    // Двигаем «Риск» до конца вправо — «блефует и обвиняет в лоб».
    await reveal(tester, 'spectrum-risk');
    await tester.drag(find.byKey(const Key('spectrum-risk')), const Offset(600, 0));
    await tester.pumpAndSettle();
    // Строгость ассоциаций влево до конца — «одной уликой проверил всё связанное».
    await reveal(tester, 'spectrum-strictness', delta: -200);
    expect(find.text('Строгость ассоциаций'), findsOneWidget);
    await tester.drag(find.byKey(const Key('spectrum-strictness')), const Offset(-600, 0));
    await tester.pumpAndSettle();
    await reveal(tester, 'spectrum-memory');
    await tester.drag(find.byKey(const Key('spectrum-memory')), const Offset(-600, 0));
    await tester.pumpAndSettle();
    await reveal(tester, 'spectrum-details');
    await tester.drag(find.byKey(const Key('spectrum-details')), const Offset(600, 0));
    await tester.pumpAndSettle();
    await reveal(tester, 'bot-save');
    await tester.tap(find.byKey(const Key('bot-save')));
    await tester.pumpAndSettle();

    final saved = app.api.named('saveBot').single.$2;
    expect(saved[1], 'Холмс');
    expect((saved[2] as BotSpectra).risk, 1.0);
    expect((saved[2] as BotSpectra).social['influence'], 0.0);
    expect((saved[2] as BotSpectra).strictness, 0.0);
    expect((saved[2] as BotSpectra).memory, 0.0);
    expect((saved[2] as BotSpectra).details, 1.0);
    expect(BotSpectra.fromJson((saved[2] as BotSpectra).toJson()).details, 1.0);
    expect((saved[2] as BotSpectra).toJson()['strictness'], 0.0, reason: 'уходит на сервер');
    expect(find.text('Бот Холмс'), findsOneWidget, reason: 'вернулись в кабинет, список обновлён');
  });

  testWidgets('лобби: хост выбирает бота с характером', (tester) async {
    final app = await _start(tester, admin: false, user: host);
    app.api.botList = const [
      BotInfo(id: 'p1', nickname: 'Бот Пуаро', avatarColor: '#5C7C99', about: 'Смысл прежде всего', spectra: BotSpectra(), enabled: true),
      BotInfo(id: 'p2', nickname: 'Бот Коломбо', avatarColor: '#E57F4F', about: 'Рискует', spectra: BotSpectra(), enabled: false),
    ];
    app.realtime.lobby = lobby();
    app.api.lobbyResult = lobby();
    app.go('/lobby/l1');
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(const Key('add-bot')));
    await tester.tap(find.byKey(const Key('add-bot')));
    await tester.pumpAndSettle();

    expect(find.text('Смысл прежде всего'), findsOneWidget);
    expect(find.text('Бот Коломбо'), findsNothing, reason: 'выключенного не предлагаем');
    await tester.tap(find.byKey(const Key('bot-p1')));
    await tester.pumpAndSettle();

    expect(app.api.named('addBot').single.$2, ['l1', 'p1']);
  });

  testWidgets('админ ставит фото боту: обрезка, превью, загрузка при сохранении; можно убрать', (tester) async {
    final app = await _start(tester, admin: true);
    app.api.botList = const [
      BotInfo(id: 'p1', nickname: 'Бот Пуаро', avatarColor: '#5C7C99', about: '', spectra: BotSpectra(), enabled: true),
    ];
    app.go('/admin/bots');
    await tester.pumpAndSettle();

    await tester.tap(find.text('Бот Пуаро'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('bot-photo-remove')), findsNothing, reason: 'фото ещё нет');
    await tester.tap(find.byKey(const Key('bot-photo-pick')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('avatar-photo-local')), findsOneWidget, reason: 'превью до сохранения');
    expect(app.api.named('uploadBotAvatar'), isEmpty, reason: 'загружаем только по «Сохранить»');

    await reveal(tester, 'bot-save');
    await tester.tap(find.byKey(const Key('bot-save')));
    await tester.pumpAndSettle();
    expect(app.api.named('uploadBotAvatar').single.$2, ['p1', 4, 'avatar.png']);
    expect(find.byKey(const Key('avatar-photo-photo-p1')), findsOneWidget, reason: 'в списке кабинета — фото');

    await tester.tap(find.text('Бот Пуаро'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('bot-photo-remove')));
    await tester.pumpAndSettle();
    await reveal(tester, 'bot-save');
    await tester.tap(find.byKey(const Key('bot-save')));
    await tester.pumpAndSettle();
    expect(app.api.named('removeBotAvatar').single.$2, ['p1']);
    expect(find.byKey(const Key('avatar-photo-photo-p1')), findsNothing);
  });

  testWidgets('новый бот: фото выбрано до создания — загружается после', (tester) async {
    final app = await _start(tester, admin: true);
    app.go('/admin/bots');
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('bot-new')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('bot-name')), 'Холмс');
    await tester.tap(find.byKey(const Key('bot-photo-pick')));
    await tester.pumpAndSettle();
    await reveal(tester, 'bot-save');
    await tester.tap(find.byKey(const Key('bot-save')));
    await tester.pumpAndSettle();

    final created = app.api.botList.single;
    expect(app.api.named('uploadBotAvatar').single.$2.first, created.id);
    expect(created.avatarId, isNotNull);
  });
}
