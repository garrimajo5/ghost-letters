import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'support/fakes.dart';
import 'support/fixtures.dart';

Map<String, dynamic> _card(String id, double belief, {List<Map<String, dynamic>> links = const []}) =>
    {'card': id, 'belief': belief, 'rowwise': belief, 'theory': belief, 'evidence': 0, 'links': links};

void main() {
  for (final width in [320.0, 1440.0]) {
    testWidgets('лаборатория: раздача, подсказки, совет Призрака и мысли ботов: $width', (tester) async {
      tester.view.physicalSize = Size(width, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: watson);
      addTearDown(app.container.dispose);
      app.api.admin = true;
      app.api.labNamesResult = {'orig_0001': 'клетка', 'orig_0002': 'роза', 'orig_0003': 'нож', 'orig_0004': 'кот', 'orig_0010': 'корона'};
      app.api.labDealResult = {
        'seed': 42,
        'players': 6,
        'rounds': 4,
        'board': [
          {'category': 'Motive', 'cards': ['orig_0001', 'orig_0002']},
          {'category': 'Method', 'cards': ['orig_0003', 'orig_0004']},
        ],
        'truth': [0, 1],
        'pool': ['orig_0010', 'orig_0011'],
        'names': {'orig_0011': 'шляпа'},
      };
      app.api.labThinkResult = {
        'bots': [
          {
            'bot': 'Пуаро',
            'color': '#5C7C99',
            'about': 'Смысл прежде всего',
            'correct': 1,
            'theoryBest': ['orig_0001', 'orig_0003'],
            'unexplained': <String>[],
            'rows': [
              {
                'category': 'Motive',
                'vote': 0,
                'correct': true,
                'cards': [
                  _card('orig_0001', .8, links: [
                    {'hint': 'orig_0010', 'similarity': .6, 'why': 'по смыслу: золото'}
                  ]),
                  _card('orig_0002', .2),
                ]
              },
              {
                'category': 'Method',
                'vote': 0,
                'correct': false,
                'cards': [_card('orig_0003', .6), _card('orig_0004', .4)]
              },
            ]
          }
        ],
        'names': {},
      };
      app.api.labSuggestResult = [
        {'card': 'orig_0010', 'pick': true, 'trueMatch': .6, 'falseMatch': .1, 'reason': 'истинная карта читается яснее ложных'},
        {'card': 'orig_0011', 'pick': false, 'trueMatch': .05, 'falseMatch': .02, 'reason': 'связь с истиной слишком слабая'},
      ];
      await tester.pumpWidget(app.widget);
      await tester.pumpAndSettle();
      await tester.tap(find.byIcon(Icons.more_horiz).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Лаборатория мыслей'));
      await tester.pumpAndSettle();
      expect(find.text('ЛАБОРАТОРИЯ МЫСЛЕЙ'), findsOneWidget);

      await tester.tap(find.byKey(const Key('lab-deal')));
      await tester.pumpAndSettle();
      expect(app.api.named('labDeal').single.$2, [6, null]);
      expect(app.api.named('labThink'), hasLength(1));
      expect(find.byKey(const Key('lab-board-1-1')), findsOneWidget);

      // Совет Призрака-бота: отмеченное письмо уходит в подсказки раунда.
      await tester.ensureVisible(find.byKey(const Key('lab-suggest-round-0')));
      await tester.tap(find.byKey(const Key('lab-suggest-round-0')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('lab-suggest-orig_0011')), findsOneWidget);
      await tester.tap(find.byKey(const Key('lab-suggest-apply')));
      await tester.pumpAndSettle();
      final think = app.api.named('labThink').last.$2.single as Map;
      expect(think['hints'], [
        {'round': 1, 'cards': ['orig_0010']}
      ]);
      expect(think['truth'], [0, 1]);

      // Ручная подсказка из поиска по всей колоде.
      await tester.tap(find.byKey(const Key('lab-add-hint-1')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('lab-picker-pool')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('lab-picker-search')), 'кле');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('lab-pick-orig_0001')), findsNothing, reason: 'карты поля в подсказки не берём');
      await tester.enterText(find.byKey(const Key('lab-picker-search')), '');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('lab-pick-orig_0002')), findsNothing);
      await tester.tap(find.text('Отмена').last);
      await tester.pumpAndSettle();

      // Своя истина: отметить другую карту ряда.
      await tester.ensureVisible(find.byKey(const Key('lab-board-0-1')));
      await tester.tap(find.byKey(const Key('lab-board-0-1')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('lab-mark-truth')));
      await tester.pumpAndSettle();
      expect((app.api.named('labThink').last.$2.single as Map)['truth'], [1, 1]);

      // Мысли ботов: голоса и «почему».
      expect(find.byKey(const Key('lab-votes')), findsOneWidget);
      await tester.scrollUntilVisible(find.byKey(const Key('lab-bot-Пуаро')), 300, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.byKey(const Key('lab-bot-Пуаро')));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('80 %'));
      await tester.tap(find.text('80 %'));
      await tester.pumpAndSettle();
      expect(find.text('по смыслу: золото'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  }
}
