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

const host = User(id: 'u1', nickname: 'Призрачный', avatarColor: '#7C6CF2');
const watson = User(id: 'u2', nickname: 'Ватсон', avatarColor: '#3FB68B');

Json member(User u, {int seat = 0, bool ready = false, String mode = 'player'}) => {
      'userId': u.id,
      'nickname': u.nickname,
      'avatarColor': u.avatarColor,
      'seat': seat,
      'mode': mode,
      'isReady': ready,
    };

/// Лобби на двоих: хост и Ватсон; [watsonReady] — готов ли второй игрок.
Lobby lobby({bool watsonReady = false, String status = 'open', String? gameId}) => Lobby.fromJson({
      'id': 'l1',
      'code': 'ABC234',
      'title': 'Стол Призрачный',
      'hostUserId': host.id,
      'status': status,
      'settings': const LobbySettings().toJson(),
      'currentGameId': gameId,
      'members': [member(host), member(watson, seat: 1, ready: watsonReady)],
    });

GameSnapshot snapshot({String phase = 'Voting', List<String> allowed = const ['CastVote'], int version = 42}) {
  final j = snapshotJson(phase: phase, allowed: allowed);
  (j['view'] as Json)['version'] = version;
  return GameSnapshot.fromJson(j);
}
