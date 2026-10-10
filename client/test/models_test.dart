import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/game/game_state.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fixtures.dart';

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
    expect(actionHint(v), 'Режим зрителя');
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
    expect(m.toJson('orig_0001'), {
      'cardId': 'orig_0001',
      'crosses': 20,
      'checks': 0,
      'believed': true,
      'sources': {'crossBy': <String>[], 'checkBy': <String>[], 'claimedBy': null, 'claim': null},
    });

    // Источник ✕: добавили игрока — счётчик +1, убрали — −1; список и пометка с источниками не «пустые».
    final byMarple = empty.toggleSource('u3', cross: true);
    expect(byMarple.crosses, 1);
    expect(byMarple.crossBy, ['u3']);
    expect(byMarple.isEmpty, isFalse);
    final back = byMarple.toggleSource('u3', cross: true);
    expect(back.crosses, 0);
    expect(back.crossBy, isEmpty);
    expect(back.isEmpty, isTrue);
    expect(CardMark.fromJson(byMarple.toJson('x')).crossBy, ['u3']);
  });

  test('доска улик и «не брать» Сообщника разбираются из снимка', () {
    final j = snapshotJson(phase: 'Discussion', allowed: []);
    final view = j['view'] as Map<String, dynamic>;
    view['table'] = {
      'threads': [
        {'id': 7, 'author': 'u3', 'round': 2, 'sourceKind': 'Letter', 'source': 'orig_0300', 'target': 'orig_0002',
          'stance': 'Against', 'reason': 'цвет', 'endorsedBy': ['u2'], 'disputedBy': <String>[]},
      ],
      'pins': [{'author': 'u3', 'row': 1, 'column': 4}],
      'checks': [{'author': 'u2', 'card': 'orig_0001'}],
      'claims': [{'author': 'u3', 'round': 1, 'card': 'orig_0300'}],
      'canPost': true,
      'pinsOnly': false,
    };
    view['teamSuggestions'] = [
      {'from': 'u3', 'columns': [1, 2], 'avoid': [{'row': 0, 'column': 3}]},
    ];
    final v = GameSnapshot.fromJson(j).view;
    final t = v.table.threads.single;
    expect((t.id, t.author, t.sourceKind, t.isFor, t.reason), (7, 'u3', 'Letter', false, 'цвет'));
    expect(t.endorsedBy, ['u2']);
    expect((v.table.pins.single.row, v.table.pins.single.column), (1, 4));
    expect(v.table.checks.single.card, 'orig_0001');
    expect(v.table.claims.single.round, 1);
    expect(v.table.canPost, isTrue);
    expect(v.teamSuggestions.single.avoid, [(0, 3)]);

    final old = GameSnapshot.fromJson(snapshotJson()).view;
    expect(old.table.isEmpty, isTrue);
    expect(old.table.canPost, isFalse);
  });
}
