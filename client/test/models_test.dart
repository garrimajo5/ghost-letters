import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/game/game_state.dart';
import 'package:ghost_letters/models/models.dart';

/// Ответ сервера в том виде, как его отдаёт GET /games/{id}/view.
Map<String, dynamic> snapshotJson({String phase = 'Voting', List<String> allowed = const ['CastVote']}) => {
      'view': {
        'gameId': 'g1',
        'version': 42,
        'phase': phase,
        'round': 4,
        'totalRounds': 4,
        'discussion': 'Radio',
        'board': [
          {'category': 'Motive', 'cards': ['orig_0001', 'orig_0002', 'orig_0003', 'orig_0004', 'orig_0005']},
          {'category': 'Secret', 'cards': ['orig_0006', 'orig_0007', 'orig_0008', 'orig_0009', 'orig_0010']},
        ],
        'hints': [
          {'round': 0, 'cards': ['orig_0100']},
          {'round': 1, 'cards': <String>[]},
        ],
        'vanishedCount': 6,
        'players': [
          {'id': 'u1', 'seat': 0, 'isGhost': true, 'knownRole': 'Ghost', 'hasActed': false, 'handCount': 5},
          {'id': 'u2', 'seat': 1, 'isGhost': false, 'knownRole': 'Detective', 'hasActed': true, 'handCount': 5},
          {'id': 'u3', 'seat': 2, 'isGhost': false, 'knownRole': null, 'hasActed': false, 'handCount': 5},
        ],
        'me': {
          'id': 'u2',
          'role': 'Detective',
          'hand': ['orig_0200', 'orig_0201'],
          'letters': [
            {'round': 1, 'cardId': 'orig_0300', 'revealed': false},
          ],
        },
        'truth': null,
        'mailboxCount': 0,
        'mailboxForGhost': null,
        'radioHolder': 'u2',
        'currentSpeaker': null,
        'floorGrantedTo': null,
        'raisedHands': <String>[],
        'allowedCommands': allowed,
        'finale': {
          'currentStage': {
            'index': 1,
            'kind': 'Row',
            'row': 1,
            'attempt': 2,
            'candidateColumns': [0, 3],
            'candidateSuspects': <String>[],
          },
          'stagesTotal': 3,
          'myVote': null,
          'votes': [
            {'stage': 0, 'attempt': 1, 'voter': 'u2', 'column': 2, 'suspect': null},
          ],
          'outcomes': [
            {'stage': 0, 'kind': 'Row', 'row': 0, 'column': 2, 'suspect': null, 'correct': null, 'byLot': false, 'revealedRole': null},
          ],
          'arrested': <String>[],
          'hunt': null,
          'blackmailerFound': null,
          'result': null,
          'awards': <Object>[],
          'likes': <Object>[],
        },
      },
      'deadline': '2026-10-07T10:00:00+00:00',
      'roster': [
        {'id': 'u1', 'nickname': 'Призрачный', 'avatarColor': '#7C6CF2', 'seat': 0},
        {'id': 'u2', 'nickname': 'Ватсон', 'avatarColor': '#3FB68B', 'seat': 1},
        {'id': 'u3', 'nickname': 'Марпл', 'avatarColor': '#E5647A', 'seat': 2},
      ],
      'lobbyId': 'l1',
    };

void main() {
  test('снимок партии разбирается целиком', () {
    final snap = GameSnapshot.fromJson(snapshotJson());
    final v = snap.view;

    expect(v.version, 42);
    expect(v.board, hasLength(2));
    expect(v.board[1].category, 'Secret');
    expect(v.hints[1].cards, isEmpty);
    expect(v.me!.role, 'Detective');
    expect(v.me!.letters.single.revealed, isFalse);
    expect(v.player('u3')!.knownRole, isNull);
    expect(v.truth, isNull);
    expect(v.can('CastVote'), isTrue);
    expect(v.finale!.currentStage!.isRow, isTrue);
    expect(v.finale!.currentStage!.candidateColumns, [0, 3]);
    expect(v.finale!.outcomes.single.correct, isNull);
    expect(v.finale!.hasMyVote, isFalse);
    expect(snap.roster.map((r) => r.nickname), contains('Ватсон'));
    expect(snap.deadline, DateTime.utc(2026, 10, 7, 10));
  });

  test('экран стола: без me и без доступных команд', () {
    final json = snapshotJson(allowed: const []);
    (json['view'] as Map<String, dynamic>)['me'] = null;
    final v = GameSnapshot.fromJson(json).view;

    expect(v.me, isNull);
    expect(actionHint(v), 'Экран стола');
  });

  test('настройки лобби: в JSON уходят все поля, перечисления строками', () {
    const s = LobbySettings(
      columns: 6,
      rounds: 3,
      roles: RoleOptions(useBlackmailer: true, imitator: ImitatorMode.replaceAccomplice),
      discussion: 'FreeChat',
      timers: {'mailbox': 120},
    );
    final j = s.toJson();

    expect(j['columns'], 6);
    expect((j['roles'] as Map)['imitator'], 'ReplaceAccomplice');
    expect((j['timers'] as Map)['mailbox'], 120);

    final back = LobbySettings.fromJson(j);
    expect(back.roles.imitator, ImitatorMode.replaceAccomplice);
    expect(back.copyWith(clearRounds: true).rounds, isNull);
    expect(back.timer('voting', 60), 60);
  });

  test('подсказка действия зависит от фазы и роли', () {
    final mailbox = GameSnapshot.fromJson(snapshotJson(phase: 'Mailbox', allowed: const ['SendLetter'])).view;
    expect(actionHint(mailbox), 'Отправьте в ящик одну карту');

    final waiting = GameSnapshot.fromJson(snapshotJson(phase: 'Mailbox', allowed: const [])).view;
    expect(actionHint(waiting), 'Ждём других игроков');
  });

  test('пометка на карте: счётчики ограничены, пустая пометка распознаётся', () {
    const empty = CardMark();
    expect(empty.isEmpty, isTrue);
    final m = empty.copyWith(crosses: 25, checks: -3, believed: true);
    expect(m.crosses, 20);
    expect(m.checks, 0);
    expect(m.toJson('orig_0001'), {'cardId': 'orig_0001', 'crosses': 20, 'checks': 0, 'believed': true});
  });
}
