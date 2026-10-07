// Снимки экранов для проверки дизайна глазами: шрифты и картинки настоящие, размер — Pixel 8.
// Запуск: flutter test --update-goldens test_screens (делает workflow «Screens preview»).
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/home/home_screen.dart';
import 'package:ghost_letters/models/models.dart';

import '../test/support/fakes.dart';
import '../test/support/fixtures.dart';

Future<void> _loadFonts() async {
  const families = {
    'Oswald': ['Oswald-400', 'Oswald-500', 'Oswald-600', 'Oswald-700'],
    'GolosText': ['GolosText-400', 'GolosText-500', 'GolosText-600', 'GolosText-700'],
  };
  for (final e in families.entries) {
    final loader = FontLoader(e.key);
    for (final f in e.value) {
      loader.addFont(rootBundle.load('assets/fonts/$f.ttf'));
    }
    await loader.load();
  }
  final root = Platform.environment['FLUTTER_ROOT'];
  if (root != null) {
    final icons = File('$root/bin/cache/artifacts/material_fonts/MaterialIcons-Regular.otf');
    if (icons.existsSync()) {
      final bytes = icons.readAsBytesSync();
      final loader = FontLoader('MaterialIcons')..addFont(Future.value(ByteData.view(bytes.buffer)));
      await loader.load();
    }
  }
}

Json _base(String phase, List<String> allowed, {String role = 'Detective'}) {
  final j = snapshotJson(phase: phase, allowed: allowed);
  final v = j['view'] as Json;
  v['round'] = 2;
  v['board'] = [
    for (final (i, c) in ['Motive', 'Place', 'Method', 'Secret'].indexed)
      {'category': c, 'cards': [for (var k = 0; k < 5; k++) 'orig_${(i * 40 + k * 7 + 11).toString().padLeft(4, '0')}']},
  ];
  v['hints'] = [
    {'round': 0, 'cards': ['orig_0301']},
    {'round': 1, 'cards': ['orig_0302', 'orig_0303']},
    {'round': 2, 'cards': <String>[]},
  ];
  v['vanishedCount'] = 5;
  v['players'] = [
    {'id': 'u1', 'seat': 0, 'isGhost': true, 'knownRole': 'Ghost', 'hasActed': true, 'handCount': 5},
    {'id': 'u2', 'seat': 1, 'isGhost': false, 'knownRole': role, 'hasActed': false, 'handCount': 5},
    {'id': 'u3', 'seat': 2, 'isGhost': false, 'knownRole': null, 'hasActed': true, 'handCount': 5},
    {'id': 'u4', 'seat': 3, 'isGhost': false, 'knownRole': null, 'hasActed': true, 'handCount': 5},
    {'id': 'u5', 'seat': 4, 'isGhost': false, 'knownRole': null, 'hasActed': false, 'handCount': 5},
    {'id': 'u6', 'seat': 5, 'isGhost': false, 'knownRole': null, 'hasActed': true, 'handCount': 5},
    {'id': 'u7', 'seat': 6, 'isGhost': false, 'knownRole': null, 'hasActed': true, 'handCount': 5},
  ];
  final me = v['me'] as Json;
  me['role'] = role;
  me['hand'] = ['orig_0401', 'orig_0402', 'orig_0403', 'orig_0404', 'orig_0405'];
  j['roster'] = [
    {'id': 'u1', 'nickname': 'Лена', 'avatarColor': '#6A5A9E', 'seat': 0},
    {'id': 'u2', 'nickname': 'Аня', 'avatarColor': '#3E7C6E', 'seat': 1},
    {'id': 'u3', 'nickname': 'Маша', 'avatarColor': '#8A5A44', 'seat': 2},
    {'id': 'u4', 'nickname': 'Олег', 'avatarColor': '#3D6A99', 'seat': 3},
    {'id': 'u5', 'nickname': 'Дима', 'avatarColor': '#7A6A3A', 'seat': 4},
    {'id': 'u6', 'nickname': 'Катя', 'avatarColor': '#9A4F6E', 'seat': 5},
    {'id': 'u7', 'nickname': 'Саша', 'avatarColor': '#4F7F3F', 'seat': 6},
  ];
  j['deadline'] = DateTime.now().add(const Duration(seconds: 42)).toIso8601String();
  return j;
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    // Плагины звука в тестах не нужны: глушим их каналы.
    final messenger = TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
    for (final name in [
      'com.llfbandit.record/messages',
      'xyz.luan/audioplayers',
      'xyz.luan/audioplayers.global',
      'xyz.luan/audioplayers.global/events',
    ]) {
      messenger.setMockMethodCallHandler(MethodChannel(name), (_) async => null);
    }
    await _loadFonts();
  });

  Future<TestApp> start(WidgetTester tester, {User? user = watson}) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: user);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    return app;
  }

  /// Дать картинкам декодироваться по-настоящему и снять экран.
  Future<void> shot(WidgetTester tester, String name) async {
    for (var i = 0; i < 3; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 300)));
      await tester.pump(const Duration(milliseconds: 100));
    }
    await tester.pump(const Duration(seconds: 2));
    await expectLater(find.byType(MaterialApp), matchesGoldenFile('goldens/$name.png'));
  }

  Future<TestApp> game(WidgetTester tester, Json j, {List<Json> marks = const []}) async {
    final app = await start(tester);
    app.api.marksResult = marks;
    final snap = GameSnapshot.fromJson(j);
    app.realtime.game = snap;
    app.api.snapshotResult = snap;
    app.go('/game/g1');
    await tester.pumpAndSettle(const Duration(milliseconds: 100), EnginePhase.sendSemanticsUpdate, const Duration(seconds: 5));
    return app;
  }

  testWidgets('01 вход', (tester) async {
    await start(tester, user: null);
    await tester.enterText(find.byKey(const Key('nickname')), 'Аня');
    await tester.pump();
    await shot(tester, '01_login');
  });

  testWidgets('02 главная', (tester) async {
    final app = await start(tester);
    app.api.games = [
      const MyGame(gameId: 'g1', status: 'active', phase: 'Mailbox', yourTurn: true, deadline: null),
      const MyGame(gameId: 'g2', status: 'active', phase: 'Discussion', yourTurn: false, deadline: null),
    ];
    app.container.invalidate(myGamesProvider);
    await tester.pumpAndSettle();
    await shot(tester, '02_home');
  });

  testWidgets('03 лобби', (tester) async {
    final app = await start(tester, user: host);
    app.realtime.lobby = lobby(watsonReady: true);
    app.go('/lobby/l1');
    await tester.pumpAndSettle();
    await shot(tester, '03_lobby');
  });

  testWidgets('04 роль', (tester) async {
    await game(tester, _base('RoleReveal', const ['AckRole']));
    await shot(tester, '04_role_hidden');
    await tester.tap(find.byKey(const Key('role-card')));
    await tester.pumpAndSettle();
    await shot(tester, '05_role_open');
  });

  testWidgets('06 ночь Убийцы', (tester) async {
    await game(tester, _base('Night', const ['ChooseTruth'], role: 'Killer'));
    await tester.tap(find.byKey(const Key('board-0-2')));
    await tester.tap(find.byKey(const Key('board-1-4')));
    await tester.pump();
    await shot(tester, '06_night_killer');
  });

  testWidgets('07 письмо: ваш ход', (tester) async {
    await game(tester, _base('Mailbox', const ['SendLetter']), marks: [
      {'cardId': 'orig_0018', 'crosses': 3, 'checks': 0, 'believed': false},
      {'cardId': 'orig_0051', 'crosses': 0, 'checks': 2, 'believed': true},
      {'cardId': 'orig_0011', 'crosses': 1, 'checks': 1, 'believed': false},
    ]);
    await tester.tap(find.byKey(const Key('hand-orig_0402')));
    await tester.pump();
    await shot(tester, '07_mailbox_my_turn');
  });

  testWidgets('08 ждём других', (tester) async {
    await game(tester, _base('Mailbox', const []));
    await shot(tester, '08_waiting');
  });

  testWidgets('09 пометка карты', (tester) async {
    await game(tester, _base('Discussion', const ['RaiseHand']));
    await tester.tap(find.byKey(const Key('board-1-1')));
    await tester.pumpAndSettle();
    await shot(tester, '09_mark_sheet');
  });

  testWidgets('10 выбор Призрака', (tester) async {
    final j = _base('GhostPick', const ['RevealHints'], role: 'Ghost');
    final v = j['view'] as Json;
    v['mailboxForGhost'] = ['orig_0501', 'orig_0502', 'orig_0503', 'orig_0504', 'orig_0505', 'orig_0506'];
    await game(tester, j);
    await tester.tap(find.byKey(const Key('mailbox-orig_0502')));
    await tester.pump();
    await shot(tester, '10_ghost_pick');
  });

  testWidgets('11 голосование по ряду', (tester) async {
    final j = _base('Voting', const ['CastVote']);
    (j['view'] as Json)['finale'] = {
      ...((j['view'] as Json)['finale'] as Json),
      'stagesTotal': 5,
      'currentStage': {'index': 1, 'kind': 'Row', 'row': 1, 'attempt': 1, 'candidateColumns': [0, 1, 2, 3, 4], 'candidateSuspects': <String>[]},
    };
    await game(tester, j);
    await tester.tap(find.byKey(const Key('board-1-3')));
    await tester.pump();
    await shot(tester, '11_vote_row');
  });

  testWidgets('12 голосование: кто Убийца', (tester) async {
    final j = _base('Voting', const ['CastVote']);
    (j['view'] as Json)['finale'] = {
      ...((j['view'] as Json)['finale'] as Json),
      'stagesTotal': 5,
      'currentStage': {'index': 4, 'kind': 'Killer', 'row': -1, 'attempt': 1, 'candidateColumns': <int>[], 'candidateSuspects': ['u2', 'u3', 'u4', 'u5', 'u6', 'u7']},
    };
    await game(tester, j);
    await tester.ensureVisible(find.byKey(const Key('pick-u5')));
    await tester.tap(find.byKey(const Key('pick-u5')));
    await tester.pump();
    await shot(tester, '12_vote_killer');
  });

  testWidgets('13 охота', (tester) async {
    final j = _base('Hunt', const ['HuntPick'], role: 'Killer');
    ((j['view'] as Json)['finale'] as Json)['currentStage'] = null;
    await game(tester, j);
    await shot(tester, '13_hunt');
  });

  testWidgets('14 итоги и ачивки', (tester) async {
    final j = _base('AwardNomination', const ['Like', 'Nominate']);
    final v = j['view'] as Json;
    v['truth'] = [1, 3, 0, 2];
    v['finale'] = {
      ...(v['finale'] as Json),
      'currentStage': null,
      'outcomes': [
        {'stage': 0, 'kind': 'Row', 'row': 0, 'column': 1, 'suspect': null, 'correct': true, 'byLot': false, 'revealedRole': null},
        {'stage': 1, 'kind': 'Row', 'row': 1, 'column': 2, 'suspect': null, 'correct': false, 'byLot': false, 'revealedRole': null},
      ],
      'result': {
        'solved': true,
        'correctRows': 3,
        'killerCaught': true,
        'side': 'Detectives',
        'imitatorWon': false,
        'blackmailerWon': false,
        'winners': ['u1', 'u2', 'u3', 'u5'],
      },
      'likes': [
        {'player': 'u3', 'count': 2, 'likedByMe': true},
        {'player': 'u4', 'count': 0, 'likedByMe': false},
      ],
    };
    await game(tester, j);
    await tester.ensureVisible(find.byKey(const Key('nominate-sherlock')));
    await tester.pump();
    await shot(tester, '14_results_awards');
  });

  testWidgets('15 чат', (tester) async {
    final app = await game(tester, _base('Discussion', const ['ReadyNextRound']));
    for (final (i, m) in [
      ('u3', 'Отправляла вот эту — и она открылась.', ['orig_0302']),
      ('u2', 'Думаю, это про второй ряд.', <String>[]),
      ('u5', 'Моё письмо исчезло. Про третий ряд пока ничего сказать не могу.', <String>[]),
    ].indexed) {
      app.realtime.chatCtl.add(ChatMessage(
        id: 'm$i',
        channel: 'public',
        authorId: m.$1,
        kind: 'text',
        text: m.$2,
        cardIds: m.$3,
        createdAt: DateTime(2026, 10, 7, 20, i),
        round: 2,
      ));
    }
    await tester.pump();
    await tester.tap(find.byTooltip('Чат'));
    await tester.pumpAndSettle();
    await shot(tester, '15_chat');
  });

  testWidgets('16 настройки в партии', (tester) async {
    final app = await game(tester, _base('Discussion', const ['ReadyNextRound']));
    app.realtime.lobbyCtl.add(Lobby.fromJson({
      'id': 'l1',
      'code': 'ABC234',
      'title': 'Стол',
      'hostUserId': 'u2',
      'status': 'in_game',
      'settings': const LobbySettings().toJson(),
      'currentGameId': 'g1',
      'members': [member(watson)],
    }));
    await tester.pump();
    await tester.tap(find.byTooltip('Меню партии'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Раунды, темп и таймеры'));
    await tester.pumpAndSettle();
    await shot(tester, '16_settings');
  });
}
