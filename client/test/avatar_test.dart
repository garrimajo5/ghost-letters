import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Своя аватарка: загрузка из галереи в профиле, показ вместо буквы, удаление.
void main() {
  testWidgets('профиль: выбрал фото — оно у меня в аккаунте; убрал — снова буква', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.go('/profile/u2');
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('avatar-edit')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('avatar-remove')), findsNothing, reason: 'фото ещё нет');
    await tester.tap(find.byKey(const Key('avatar-pick')));
    await tester.pumpAndSettle();

    expect(app.api.named('uploadAvatar').single.$2[1], 'avatar.png');
    expect(app.container.read(sessionProvider).user?.avatarId, 'm1');
    expect(find.byKey(const Key('avatar-photo-m1')), findsOneWidget);

    await tester.tap(find.byKey(const Key('avatar-edit')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('avatar-remove')));
    await tester.pumpAndSettle();

    expect(app.container.read(sessionProvider).user?.avatarId, isNull);
    expect(find.byKey(const Key('avatar-photo-m1')), findsNothing);
  });

  testWidgets('чужой профиль: менять фото нельзя', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.go('/profile/u3');
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('avatar-edit')), findsNothing);
  });

  testWidgets('аватар с фото показывает картинку, без фото — букву', (tester) async {
    await tester.pumpWidget(const MaterialApp(
      home: Row(children: [
        Avatar(nickname: 'Ирен', color: '#3FB68B', photoId: 'm7'),
        Avatar(nickname: 'Ватсон', color: '#3FB68B'),
      ]),
    ));

    expect(find.byKey(const Key('avatar-photo-m7')), findsOneWidget);
    expect(find.text('В'), findsOneWidget);
  });

  test('аватарка приходит в профиле и составе лобби', () {
    expect(User.fromJson({'id': 'u', 'nickname': 'Н', 'avatarColor': '#000000', 'avatarId': 'a1'}).avatarId, 'a1');
    expect(
      LobbyMember.fromJson({'userId': 'u', 'nickname': 'Н', 'avatarColor': '#000000', 'seat': 0, 'mode': 'player', 'avatarId': 'a2'}).avatarId,
      'a2',
    );
    expect(RosterEntry.fromJson({'id': 'u', 'nickname': 'Н', 'avatarColor': '#000000', 'seat': 0, 'avatarId': 'a3'}).avatarId, 'a3');
  });
}
