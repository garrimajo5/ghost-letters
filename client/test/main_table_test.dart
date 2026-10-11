import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/theme.dart';
import 'package:ghost_letters/features/game/game_screen.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Новый стол по наброску: телефон боком и вертикально.
Future<TestApp> _open(WidgetTester tester, Size size,
    {String phase = 'Discussion', List<String> allowed = const ['RaiseHand'], void Function(Json view)? edit, bool big = false}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson, classicTable: false, bigBoard: big);
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

/// Для снимков — настоящие шрифты игры вместо тестовых квадратиков.
Future<void> _loadFonts() async {
  const families = {
    'Oswald': ['400', '500', '600', '700'],
    'GolosText': ['400', '500', '600', '700'],
  };
  for (final e in families.entries) {
    final loader = FontLoader(e.key);
    for (final w in e.value) {
      loader.addFont(rootBundle.load('assets/fonts/${e.key}-$w.ttf'));
    }
    await loader.load();
  }
}

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
  setUpAll(() async {
    if (const String.fromEnvironment('TABLE_SCREENSHOTS').isNotEmpty) await _loadFonts();
  });
  const phone = Size(800, 360);
  const portrait = Size(390, 844);

  for (final size in const [phone, portrait, Size(1280, 800), Size(1600, 900)]) {
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

  testWidgets('Призрак выбирает подсказки, видя стол: панель писем сверху, поле под ней', (tester) async {
    await _open(tester, const Size(390, 844), phase: 'GhostPick', allowed: ['RevealHints']);
    expect(tester.takeException(), isNull);
    expect(find.byKey(const Key('main-panel')), findsOneWidget);
    expect(find.byKey(const Key('board-3-4')), findsOneWidget);
    await _shot(tester, 'ghostpick');
  });

  testWidgets('ряды сверху вниз как в партии; подсказки столбиком слева', (tester) async {
    await _open(tester, phone);

    double y(String key) => tester.getTopLeft(find.byKey(Key(key))).dy;
    // Названия рядов случайны — порядок как пришёл с сервера: 0, 1, 2, 3.
    expect(y('row-label-0'), lessThan(y('row-label-1')));
    expect(y('row-label-1'), lessThan(y('row-label-2')));
    expect(y('row-label-2'), lessThan(y('row-label-3')));
    // Подсказки — столбиком вплотную слева от поля (они ко всему столу, не к рядам).
    expect(tester.getTopRight(find.byKey(const Key('hint-orig_0100'))).dx, lessThan(tester.getTopLeft(find.byKey(const Key('board-0-0'))).dx));
    // Игроки справа от поля.
    expect(tester.getTopLeft(find.byKey(const Key('main-players'))).dx,
        greaterThan(tester.getTopRight(find.byKey(const Key('board-0-4'))).dx));
  });

  testWidgets('голосование по ряду — прямо на поле: ряд подсвечен, карта выбирается нажатием', (tester) async {
    final app = await _open(tester, phone, phase: 'Voting', allowed: const ['CastVote'],
        edit: (view) => view['finale'] = (snapshotJson()['view'] as Json)['finale']);

    expect(find.byKey(const Key('main-panel')), findsNothing);
    expect(find.byKey(const Key('vote-strip')), findsOneWidget);
    expect(find.byKey(const Key('focus-row-1')), findsOneWidget);
    // Прошлый ряд приглушён, кроме карты, которую выбрал стол.
    bool dimmed(String key) => [
          ...find.ancestor(of: find.byKey(Key(key)), matching: find.byType(Opacity)).evaluate(),
          ...find.descendant(of: find.byKey(Key(key)), matching: find.byType(Opacity)).evaluate(),
        ].any((e) => (e.widget as Opacity).opacity < 1);
    expect(dimmed('board-0-1'), isTrue);
    expect(dimmed('board-0-2'), isFalse);
    expect(dimmed('board-1-3'), isFalse);
    await tester.tap(find.byKey(const Key('board-1-3')));
    await tester.pump();
    final state = tester.state<GameScreenState>(find.byType(GameScreen));
    expect(state.voteColumn, 3);
    await tester.tap(find.byKey(const Key('cta')));
    await tester.pumpAndSettle();
    final vote = app.api.named('command').where((c) => c.$2[0] == 'CastVote').single.$2;
    expect(vote[1], {'column': 3});
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

  testWidgets('меню: вид стола переключается на сеанс', (tester) async {
    await _open(tester, phone);
    await tester.tap(find.byKey(const Key('main-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Вид: спиритический сеанс'));
    await tester.pump(const Duration(milliseconds: 500));
    expect(find.byKey(const Key('seance-table')), findsOneWidget);
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
    // Реплика печатается по буквам — даём ей допечататься.
    await tester.pump(const Duration(seconds: 6));

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

  testWidgets('карту вытаскивают из веера на стол — письмо отправлено', (tester) async {
    final app = await _open(tester, phone, phase: 'Mailbox', allowed: const ['SendLetter']);

    final gesture = await tester.startGesture(tester.getCenter(find.byKey(const Key('hand-orig_0201'))));
    await gesture.moveBy(const Offset(0, -40));
    await tester.pump();
    await gesture.moveBy(const Offset(-80, -120));
    await tester.pump();
    expect(find.byKey(const Key('drop-zone')), findsOneWidget);
    expect(find.text('Отправить письмо'), findsOneWidget);
    expect(find.byKey(Key('drop-icon-${Icons.markunread_mailbox_outlined.codePoint}')), findsOneWidget);
    await _shot(tester, 'drag');
    await gesture.up();
    await tester.pumpAndSettle();

    final sent = app.api.named('command').where((c) => c.$2[0] == 'SendLetter').single.$2;
    expect(sent[1], {'cardIds': ['orig_0201']});
  });

  testWidgets('в обсуждении карту не сыграть — вытащенная возвращается в веер', (tester) async {
    final app = await _open(tester, phone);

    await tester.drag(find.byKey(const Key('hand-orig_0200')), const Offset(0, -200));
    await tester.pumpAndSettle();
    expect(app.api.named('command'), isEmpty);
  });

  testWidgets('показ на столе: несколько улик, у каждой свои связи', (tester) async {
    final app = await _open(tester, phone);

    await tester.tap(find.byKey(const Key('say-start')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('hint-orig_0100')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-0-0')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('hand-orig_0200')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-1-1')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-1-1')));
    await tester.pump();
    expect(find.byKey(const Key('say-source-orig_0100')), findsOneWidget);
    expect(find.byKey(const Key('say-source-orig_0200')), findsOneWidget);
    await tester.tap(find.byKey(const Key('say-send')));
    await tester.pumpAndSettle();

    final call = app.api.named('sendChat').single.$2;
    expect(call[3], ['orig_0100', 'orig_0200', 'orig_0001', 'orig_0007']);
    expect(call[4], ['улика', 'кидал', 'думаю, эта:0', 'исключаю:1']);
    expect(call[1] as String, contains('зацепка указывает на Мотив 1'));
    expect(call[1] as String, contains('не Место 2'));
  });

  testWidgets('слово перешло ко мне — вместо чужой реплики крупная кнопка «Показать на столе»', (tester) async {
    final app = await _open(tester, phone, edit: (view) => view['currentSpeaker'] = 'u2');
    app.realtime.chatCtl.add(ChatMessage(
        id: 'c1', channel: 'public', authorId: 'u3', kind: 'text', text: 'Мотив 1.',
        cardIds: const [], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(seconds: 6));
    expect(find.byKey(const Key('say-statement')), findsNothing);
    expect(find.text('Показать на столе'), findsOneWidget);
  });

  testWidgets('две улики показа — два цвета; карта, на которую указывают обе, в составной рамке', (tester) async {
    final app = await _open(tester, phone);
    app.realtime.chatCtl.add(ChatMessage(
        id: 'l1', channel: 'public', authorId: 'u3', kind: 'text', text: 'Со стола: улики указывают на Мотив 1 и Мотив 2.',
        cardIds: const ['orig_0100', 'orig_0200', 'orig_0002', 'orig_0002'],
        cardNotes: const ['улика', 'кидал эту', 'думаю, эта:0', 'думаю, эта:1'],
        createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(seconds: 8));
    expect(find.byKey(const Key('hint-link-orig_0100')), findsOneWidget);
    final both = find.byKey(const Key('say-mark-orig_0002'));
    expect(find.descendant(of: both, matching: find.byWidgetPredicate((w) => w is CustomPaint && w.foregroundPainter != null)), findsWidgets);
  });

  testWidgets('вертикально разговор идёт под полем: чужие слева, свои справа', (tester) async {
    final app = await _open(tester, const Size(390, 844));
    app.realtime.chatCtl
      ..add(ChatMessage(id: 'd1', channel: 'public', authorId: 'u3', kind: 'text', text: 'Ватсон, ты за что в мотиве?',
          cardIds: const [], createdAt: DateTime.now(), round: 4))
      ..add(ChatMessage(id: 'd2', channel: 'public', authorId: 'u2', kind: 'text', text: 'За карту 3.',
          cardIds: const [], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(seconds: 8));
    expect(find.byKey(const Key('main-dialog')), findsOneWidget);
    final theirs = tester.getRect(find.byKey(const Key('dialog-d1')));
    final mine = tester.getRect(find.byKey(const Key('dialog-d2')));
    expect(theirs.center.dx, lessThan(195));
    expect(mine.center.dx, greaterThan(195));
    expect(theirs.top, greaterThan(tester.getRect(find.byKey(const Key('board-3-4'))).bottom));
    await _shot(tester, 'dialog');
  });

  testWidgets('планшет вертикально: поле целиком видно над разговором', (tester) async {
    final app = await _open(tester, const Size(800, 915));
    app.realtime.chatCtl.add(ChatMessage(id: 'h1', channel: 'public', authorId: 'u3', kind: 'text', text: 'Мотив 1.',
        cardIds: const [], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(seconds: 6));
    final last = tester.getRect(find.byKey(const Key('board-3-4')));
    final dialog = tester.getRect(find.byKey(const Key('main-dialog')));
    expect(last.bottom, lessThanOrEqualTo(dialog.top + 1));
    expect(last.height, greaterThan(64));
  });

  testWidgets('вертикально улики Призрака — полосой под полем, разговор — ещё ниже', (tester) async {
    final app = await _open(tester, const Size(390, 844));
    app.realtime.chatCtl.add(ChatMessage(id: 'u1m', channel: 'public', authorId: 'u3', kind: 'text', text: 'Мотив 1.',
        cardIds: const [], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(seconds: 6));
    final hint = tester.getRect(find.byKey(const Key('hint-orig_0100')));
    expect(hint.top, greaterThan(tester.getRect(find.byKey(const Key('board-3-4'))).bottom));
    expect(tester.getRect(find.byKey(const Key('main-dialog'))).top, greaterThanOrEqualTo(hint.bottom));
    await _shot(tester, 'hints-below');
  });

  testWidgets('подпись раунда — сбоку от подсказки, не поверх картинки', (tester) async {
    await _open(tester, const Size(390, 844));
    final label = tester.getRect(find.byKey(const Key('hint-label-0')));
    final card = tester.getRect(find.byKey(const Key('hint-orig_0100')));
    expect(label.right, lessThanOrEqualTo(card.left + 0.5));
  });

  testWidgets('у Призрака нет карты-эмодзи: он подсказывает только картами', (tester) async {
    await _open(tester, phone, edit: (view) => (view['me'] as Json)['role'] = 'Ghost');
    expect(find.byKey(const Key('fan-emoji')), findsNothing);
  });

  testWidgets('веер: карты внахлёст, низ спрятан; выбранная выдвигается целиком', (tester) async {
    await _open(tester, phone, phase: 'Mailbox', allowed: const ['SendLetter']);
    final fan = tester.getRect(find.byKey(const Key('main-fan')));
    final a = tester.getRect(find.byKey(const Key('hand-orig_0200')));
    expect(a.bottom, greaterThan(fan.bottom), reason: 'нижний край карты за краем веера');
    await tester.tap(find.byKey(const Key('hand-orig_0200')));
    await tester.pumpAndSettle();
    final up = tester.getRect(find.byKey(const Key('hand-orig_0200')));
    expect(up.bottom, lessThanOrEqualTo(fan.bottom));
  });

  testWidgets('карта поля: проверки из реплик подставлены сами, с ними можно не согласиться; свайп — соседняя карта', (tester) async {
    final app = await _open(tester, const Size(390, 844));
    app.realtime.chatCtl.add(ChatMessage(
        id: 'e1', channel: 'public', authorId: 'u3', kind: 'text', text: 'Отправлял вот эту — исчезла. Проверял: мотив 1.',
        cardIds: const ['orig_0200', 'orig_0001'], cardNotes: const ['кидал эту', 'проверял эту, исчезла:0'],
        createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(seconds: 8));
    await tester.tap(find.byKey(const Key('board-0-0')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('mark-evidence')), findsOneWidget);
    expect(find.byKey(const Key('evidence-e1:1')), findsOneWidget);
    await tester.tap(find.descendant(of: find.byKey(const Key('evidence-e1:1')), matching: find.byType(Checkbox)));
    await tester.pump();
    final state = tester.state<GameScreenState>(find.byType(GameScreen));
    expect(state.marks['orig_0001']?.ignored, ['e1:1']);
    expect(find.byKey(const Key('mark-token-0')), findsOneWidget, reason: 'слева от первой карты виден жетон ряда');
    await tester.drag(find.byKey(const Key('mark-row-0')), const Offset(-220, 0));
    await tester.pumpAndSettle();
    expect(find.textContaining('Мотив, карта 2'), findsOneWidget);
  });

  testWidgets('боком поле крупнее экрана и листается, подсказки остаются слева', (tester) async {
    await _open(tester, const Size(1076, 457), big: true);
    expect(find.byKey(const Key('main-board-pan')), findsOneWidget);
    final hint = tester.getRect(find.byKey(const Key('hint-orig_0100')));
    final last = tester.getRect(find.byKey(const Key('board-3-4')));
    expect(last.bottom, greaterThan(tester.getRect(find.byKey(const Key('main-board-pan'))).bottom), reason: 'поле не помещается — его листают');
    await tester.drag(find.byKey(const Key('main-board-pan')), const Offset(0, -300));
    await tester.pumpAndSettle();
    expect(tester.getRect(find.byKey(const Key('hint-orig_0100'))).top, hint.top, reason: 'подсказки не уезжают');
  });

  testWidgets('показ: подсказка Призрака только указывает — второе нажатие снимает, а не краснит', (tester) async {
    await _open(tester, phone);
    await tester.tap(find.byKey(const Key('say-start')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('hint-orig_0100')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-0-0')));
    await tester.pump();
    expect(find.byKey(const Key('say-mark-orig_0001')), findsOneWidget);
    await tester.tap(find.byKey(const Key('board-0-0')));
    await tester.pump();
    expect(find.byKey(const Key('say-mark-orig_0001')), findsNothing);
  });

  testWidgets('нажатие на игрока в обсуждении: попросить показать версию', (tester) async {
    final app = await _open(tester, phone, allowed: const ['EndTurn', 'GiveFloor'], edit: (view) => view['currentSpeaker'] = 'u2');
    await tester.tap(find.byKey(const Key('player-u3')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('ask-version-u3')));
    await tester.pumpAndSettle();
    expect(app.api.named('sendChat').single.$2[1] as String, contains('покажи свою версию'));
    expect(app.api.named('command').where((c) => c.$2[0] == 'GiveFloor'), isNotEmpty);
  });

  testWidgets('эмодзи — карта в руке: вытащил — выбор реакции, на стол не играется', (tester) async {
    final app = await _open(tester, phone, phase: 'Mailbox', allowed: const ['SendLetter']);
    await tester.drag(find.byKey(const Key('fan-emoji')), const Offset(0, -150));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('emoji-bar')), findsOneWidget);
    expect(find.byKey(const Key('emoji-🥷')), findsOneWidget);
    expect(app.api.named('command'), isEmpty);
  });

  testWidgets('показ: у каждой подсказки свой цвет и номер, прежняя не гаснет при выборе следующей', (tester) async {
    await _open(tester, phone, edit: (view) => view['hints'] = [
          {'round': 0, 'cards': ['orig_0100']},
          {'round': 1, 'cards': ['orig_0101']},
        ]);
    await tester.tap(find.byKey(const Key('say-start')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('hint-orig_0100')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-0-0')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('hint-orig_0101')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-0-1')));
    await tester.pump();
    expect(find.byKey(const Key('hint-link-orig_0100')), findsOneWidget);
    expect(find.byKey(const Key('hint-link-orig_0101')), findsOneWidget);
    expect(find.descendant(of: find.byKey(const Key('say-mark-orig_0002')), matching: find.byKey(const Key('link-number-2'))), findsOneWidget);
  });

  testWidgets('карта поля: голая версия бота без связи с письмом — не проверка', (tester) async {
    final app = await _open(tester, const Size(390, 844));
    app.realtime.chatCtl.add(ChatMessage(
        id: 'v9', channel: 'public', authorId: 'u3', kind: 'text', text: 'Отправлял вот эту — исчезла. Версия: мотив 1.',
        cardIds: const ['orig_0200', 'orig_0001'], cardNotes: const ['кидал эту', 'думаю, эта'],
        createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(seconds: 8));
    await tester.tap(find.byKey(const Key('board-0-0')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('mark-evidence')), findsNothing);
  });

  testWidgets('реплика бота печатается по буквам', (tester) async {
    final app = await _open(tester, phone);
    app.realtime.chatCtl.add(ChatMessage(
        id: 'b1', channel: 'public', authorId: 'u3', kind: 'text', text: 'Думаю, что мотив — вторая карта.',
        cardIds: const [], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
    final partial = tester.widget<Text>(find.byKey(const Key('say-statement'))).data!;
    expect(partial.length, lessThan('Марпл: Думаю, что мотив — вторая карта.'.length));
    await tester.pump(const Duration(seconds: 6));
    expect(find.text('Марпл: Думаю, что мотив — вторая карта.'), findsOneWidget);
  });

  testWidgets('длинная реплика: в строке видна строка, которую печатают сейчас', (tester) async {
    final app = await _open(tester, phone);
    app.realtime.chatCtl.add(ChatMessage(
        id: 'b2', channel: 'public', authorId: 'u3', kind: 'text', text: 'Строка один.\nСтрока два.',
        cardIds: const [], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 3500));
    expect(tester.widget<Text>(find.byKey(const Key('say-statement'))).data, 'Марпл: Строка два.');
  });

  testWidgets('реплика бота подсвечивает карту, когда до неё доходит', (tester) async {
    final app = await _open(tester, phone);
    app.realtime.chatCtl.add(ChatMessage(
        id: 'v1', channel: 'public', authorId: 'u3', kind: 'text',
        text: 'Долго думал над подсказками, и вот что скажу. Ставлю на мотив, карта 1.',
        cardIds: const ['orig_0001'], cardNotes: const ['думаю, эта'], createdAt: DateTime.now(), round: 4));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 500));
    expect(find.byKey(const Key('say-mark-orig_0001')), findsNothing, reason: 'до карты реплика ещё не дошла');
    await tester.pump(const Duration(seconds: 7));
    expect(find.byKey(const Key('say-mark-orig_0001')), findsOneWidget);
  });

  testWidgets('пока тянут карту, посреди стола — почтовый ящик', (tester) async {
    await _open(tester, phone, phase: 'Mailbox', allowed: const ['SendLetter']);

    final gesture = await tester.startGesture(tester.getCenter(find.byKey(const Key('hand-orig_0200'))));
    await gesture.moveBy(const Offset(0, -30));
    await tester.pump();
    expect(find.byKey(const Key('drop-hint')), findsOneWidget);
    expect(find.byKey(Key('drop-icon-${Icons.markunread_mailbox_outlined.codePoint}')), findsOneWidget);
    await gesture.moveBy(const Offset(0, 30));
    await gesture.up();
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('drop-hint')), findsNothing);
  });
}
