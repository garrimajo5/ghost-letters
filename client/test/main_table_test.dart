import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/theme.dart';
import 'package:ghost_letters/features/game/game_screen.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Новый стол по наброску: телефон боком и вертикально.
Future<TestApp> _open(WidgetTester tester, Size size,
    {String phase = 'Discussion', List<String> allowed = const ['RaiseHand'], void Function(Json view)? edit}) async {
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
  view['currentSpeaker'] = 'u3';
  (j['roster'] as List)[2] = {'id': 'u3', 'nickname': 'Марпл', 'avatarColor': '#E5647A', 'seat': 2, 'isBot': true};
  edit?.call(view);
  app.realtime.game = GameSnapshot.fromJson(j);
  app.go('/game/g1');
  await tester.pumpAndSettle();
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
    await File('$dir/main-$name.png').writeAsBytes(png!.buffer.asUint8List());
    image.dispose();
  });
}

void main() {
  const phone = Size(800, 360);
  const portrait = Size(390, 844);

  for (final size in const [phone, portrait, Size(1280, 800)]) {
    for (final (phase, allowed) in const [
      ('Discussion', ['RaiseHand']),
      ('Mailbox', ['SendLetter']),
      ('Night', <String>[]),
    ]) {
      testWidgets('новый стол ${size.width.toInt()}×${size.height.toInt()}, $phase: без переполнений', (tester) async {
        await _open(tester, size, phase: phase, allowed: allowed);

        expect(tester.takeException(), isNull);
        expect(find.byKey(const Key('main-table')), findsOneWidget);
        expect(find.byKey(const Key('main-fan')), findsOneWidget);
        expect(find.byKey(const Key('board-3-4')), findsOneWidget);
        expect(find.byTooltip('Чат'), findsOneWidget);
        await _shot(tester, '${size.width.toInt()}x${size.height.toInt()}-$phase');
      });
    }
  }

  testWidgets('ряды по порядку: Тайна, Мотив, Способ, Место; подсказки на высоте своих рядов', (tester) async {
    await _open(tester, phone);

    double y(String key) => tester.getTopLeft(find.byKey(Key(key))).dy;
    // board: 0 Motive, 1 Place, 2 Method, 3 Secret.
    expect(y('row-label-3'), lessThan(y('row-label-0')));
    expect(y('row-label-0'), lessThan(y('row-label-2')));
    expect(y('row-label-2'), lessThan(y('row-label-1')));
    // Первая зацепка — в ряду Тайны.
    final hint = tester.getCenter(find.byKey(const Key('hint-orig_0100'))).dy;
    expect(hint, greaterThan(y('board-3-0')));
    expect(hint, lessThan(tester.getBottomLeft(find.byKey(const Key('board-3-0'))).dy + 6));
    // Игроки справа от поля.
    expect(tester.getTopLeft(find.byKey(const Key('main-players'))).dx,
        greaterThan(tester.getTopRight(find.byKey(const Key('board-0-4'))).dx));
  });

  testWidgets('игроки: Призрак, бот и говорящий отмечены', (tester) async {
    await _open(tester, phone);

    expect(find.byKey(const Key('ghost-badge-u1')), findsOneWidget);
    expect(find.byKey(const Key('bot-badge-u3')), findsOneWidget);
    expect(find.textContaining('бот'), findsWidgets);
    // Первым говорил тот, у кого рация (реплик в раунде ещё нет).
    expect(find.byKey(const Key('first-u2')), findsOneWidget);
    // Говорящий — зелёное имя.
    final name = tester.widget<Text>(find.descendant(of: find.byKey(const Key('player-u3')), matching: find.text('Марпл')));
    expect(name.style?.color, AppColors.believed);
  });

  testWidgets('письмо: из веера выбирается одна карта', (tester) async {
    await _open(tester, phone, phase: 'Mailbox', allowed: const ['SendLetter']);

    final state = tester.state<GameScreenState>(find.byType(GameScreen));
    await tester.tap(find.byKey(const Key('hand-orig_0200')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('hand-orig_0201')));
    await tester.pump();
    expect(state.selectedHand, {'orig_0201'});
  });

  testWidgets('глазик переключает стол и ход партии на одном месте, выбор сохраняется', (tester) async {
    await _open(tester, portrait, phase: 'Mailbox', allowed: const ['SendLetter']);

    final state = tester.state<GameScreenState>(find.byType(GameScreen));
    await tester.tap(find.byKey(const Key('hand-orig_0200')));
    await tester.pump();
    final eye = tester.getCenter(find.byKey(const Key('main-eye')));
    await tester.tap(find.byKey(const Key('main-eye')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('main-panel')), findsOneWidget);
    expect(find.byKey(const Key('main-board')), findsNothing);
    expect(tester.getCenter(find.byKey(const Key('main-eye'))), eye);
    await tester.tap(find.byKey(const Key('main-eye')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('main-board')), findsOneWidget);
    expect(state.selectedHand, {'orig_0200'});
  });

  testWidgets('ничья открывает ход партии сама', (tester) async {
    await _open(tester, phone, phase: 'VoteTie', allowed: const ['ReadyRevote'],
        edit: (view) => view['finale'] = (snapshotJson()['view'] as Json)['finale']);

    expect(tester.takeException(), isNull);
    expect(find.byKey(const Key('main-panel')), findsOneWidget);
  });

  testWidgets('меню: «Классический стол» возвращает прежнюю раскладку и обратно', (tester) async {
    await _open(tester, phone);

    await tester.tap(find.byKey(const Key('main-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Классический стол'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('main-table')), findsNothing);
    expect(find.byKey(const Key('landscape-board')), findsOneWidget);

    await tester.tap(find.byTooltip('Меню партии'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Новый стол'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('main-table')), findsOneWidget);
  });

  testWidgets('показ на столе: кидал эту, зелёная и красная — уходит в чат заметками для ботов', (tester) async {
    final app = await _open(tester, phone);

    await tester.tap(find.byKey(const Key('say-start')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('hand-orig_0200')));
    await tester.pump();
    expect(find.byKey(const Key('say-source')), findsOneWidget);
    await tester.tap(find.byKey(const Key('board-0-1')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-1-2')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-1-2')));
    await tester.pump();
    expect(find.byKey(const Key('say-mark-orig_0002')), findsOneWidget);
    expect(find.byKey(const Key('say-mark-orig_0008')), findsOneWidget);
    await _shot(tester, 'say-editing');
    await tester.tap(find.byKey(const Key('say-send')));
    await tester.pumpAndSettle();

    final call = app.api.named('sendChat').single.$2;
    expect(call[2], 'public');
    expect(call[3], ['orig_0200', 'orig_0002', 'orig_0008']);
    expect(call[4], ['кидал', 'думаю, эта:0', 'исключаю:0']);
    expect(call[1] as String, startsWith('Со стола: кидал эту карту'));
    expect(call[1] as String, contains('Мотив 2'));
    expect(call[1] as String, contains('не вытащили — не Место 3'));
    expect(find.byKey(const Key('say-send')), findsNothing);
  });

  testWidgets('заявление говорящего видно на столе: его карта и отметки', (tester) async {
    final app = await _open(tester, phone);

    app.realtime.chatCtl.add(ChatMessage(
        id: 's1',
        channel: 'public',
        authorId: 'u3',
        kind: 'text',
        text: 'Со стола: эта улика; указывает на Мотив 1; не Место 2.',
        cardIds: const ['orig_0100', 'orig_0001', 'orig_0007'],
        cardNotes: const ['улика', 'думаю, эта:0', 'исключаю:0'],
        createdAt: DateTime.now(),
        round: 4));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('say-shown-orig_0100')), findsOneWidget);
    expect(find.byKey(const Key('say-mark-orig_0001')), findsOneWidget);
    expect(find.byKey(const Key('say-mark-orig_0007')), findsOneWidget);
    expect(find.textContaining('Марпл: эта улика'), findsOneWidget);
    await _shot(tester, 'statement');
    // Верность ряда не раскрывается: отметки — только мнение игрока.
    expect(tester.takeException(), isNull);
  });

  testWidgets('эмодзи: карта в веере, летят по столу и дублируются значком у аватара', (tester) async {
    final app = await _open(tester, phone);

    await tester.tap(find.byKey(const Key('fan-emoji')));
    await tester.pump();
    expect(find.byKey(const Key('emoji-bar')), findsOneWidget);
    await tester.tap(find.byKey(const Key('emoji-👍')));
    await tester.pump(const Duration(milliseconds: 100));
    expect(app.realtime.sentReactions, ['👍']);
    expect(find.byKey(const Key('flying-👍')), findsOneWidget);
    expect(find.byKey(const Key('react-badge-u2')), findsOneWidget);

    for (var i = 0; i < 3; i++) {
      app.realtime.reactionsCtl.add(Reaction(gameId: 'g1', userId: 'u3', emoji: '😂', at: DateTime.now()));
    }
    await tester.pump(const Duration(milliseconds: 100));
    expect(find.byKey(const Key('flying-😂')), findsNWidgets(3));
    expect(find.text('😂×3'), findsOneWidget);
    await _shot(tester, 'emoji');
    // Чужая партия не долетает.
    app.realtime.reactionsCtl.add(Reaction(gameId: 'other', userId: 'u3', emoji: '🔥', at: DateTime.now()));
    await tester.pump(const Duration(milliseconds: 100));
    expect(find.byKey(const Key('flying-🔥')), findsNothing);

    await tester.pump(const Duration(seconds: 5));
    expect(find.byKey(const Key('flying-😂')), findsNothing);
    expect(find.byKey(const Key('react-badge-u3')), findsNothing);
  });

  testWidgets('чат по раундам: разделители, метки «бот» и «со стола», выбор стола без верности', (tester) async {
    final app = await _open(tester, portrait, edit: (view) => view['finale'] = (snapshotJson()['view'] as Json)['finale']);

    ChatMessage msg(String id, int round, String text, {List<String> cards = const [], List<String> notes = const []}) => ChatMessage(
        id: id, channel: 'public', authorId: 'u3', kind: 'text', text: text, cardIds: cards, cardNotes: notes,
        createdAt: DateTime.now(), round: round);
    app.realtime.chatCtl
      ..add(msg('a', 3, 'Думаю про мотив'))
      ..add(msg('b', 4, 'Со стола: эта улика; указывает на Мотив 1.', cards: const ['orig_0100', 'orig_0001'], notes: const ['улика', 'думаю, эта:0']));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Чат'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('chat-divider-3')), findsOneWidget);
    expect(find.byKey(const Key('chat-divider-4')), findsOneWidget);
    expect(find.byKey(const Key('chat-bot-b')), findsOneWidget);
    expect(find.byKey(const Key('chat-table-b')), findsOneWidget);
    expect(find.byKey(const Key('chat-table-a')), findsNothing);
    expect(find.byKey(const Key('chat-outcome-0')), findsOneWidget);
    await _shot(tester, 'chat');
    expect(find.textContaining('стол выбрал карту 3'), findsOneWidget);
    expect(find.textContaining('верн'), findsNothing);
  });

  testWidgets('«Кто Убийца?»: под игроками столбцы их карт по рядам, выбор — нажатием', (tester) async {
    await _open(tester, phone, phase: 'Voting', allowed: const ['CastVote'], edit: (view) {
      final finale = Map<String, dynamic>.from((snapshotJson()['view'] as Json)['finale'] as Map);
      finale['currentStage'] = {
        'index': 1, 'kind': 'Killer', 'row': -1, 'attempt': 1, 'candidateColumns': <int>[], 'candidateSuspects': ['u3'],
      };
      finale['votes'] = [
        {'stage': 0, 'attempt': 1, 'voter': 'u2', 'column': 2, 'suspect': null},
        {'stage': 0, 'attempt': 1, 'voter': 'u3', 'column': 4, 'suspect': null},
        {'stage': 0, 'attempt': 2, 'voter': 'u3', 'column': 1, 'suspect': null},
      ];
      view['finale'] = finale;
    });

    expect(tester.takeException(), isNull);
    expect(find.byKey(const Key('suspect-board')), findsOneWidget);
    await _shot(tester, 'suspect');
    final u2 = tester.widget<CardImage>(find.byKey(const Key('suspect-vote-u2-0')));
    expect(u2.cardId, 'orig_0003');
    // Учитывается последняя попытка.
    expect(tester.widget<CardImage>(find.byKey(const Key('suspect-vote-u3-0'))).cardId, 'orig_0002');
    expect(find.byKey(const Key('suspect-vote-u1-0')), findsNothing);

    final state = tester.state<GameScreenState>(find.byType(GameScreen));
    await tester.tap(find.byKey(const Key('suspect-u3')));
    await tester.pump();
    expect(state.target, 'u3');
    // Не кандидат — не выбирается.
    await tester.tap(find.byKey(const Key('suspect-u1')));
    await tester.pump();
    expect(state.target, 'u3');
  });
}
