import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/game/chat_card_text.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

ChatMessage message(String text, {bool annotated = true}) => ChatMessage.fromJson({
      'id': 'm', 'channel': 'public', 'authorId': 'bot', 'kind': 'text',
      'text': text, 'cardIds': ['orig_1'], 'cardNotes': annotated ? ['кидал эту'] : <String>[],
      'createdAt': '2026-10-09T12:00:00Z', 'round': 1,
    });

const board = [BoardRow('Motive', ['orig_2']), BoardRow('Method', ['orig_3'])];
const hints = [HintGroup(1, ['orig_4'])];

void main() {
  test('проверки, варианты формулировок, версия и подсказки ссылаются на верные карты', () {
    final m = message('Моё письмо — вот это. Проверял: мотив 1.\n'
        '• мотив 1 — по деталям: цветок. Версия: Способ — карта 1.\n'
        'Ставлю на способ, карта 1. По-моему, Способ: 1.\n'
        'Подсказка р.1 №1 → способ 1. Связная версия: мотив 1; способ 1.');
    final parts = ChatCardText.parse(m, board, hints);
    expect(parts.map((p) => p.cardId).whereType<String>(),
        ['orig_1', 'orig_2', 'orig_2', 'orig_3', 'orig_3', 'orig_3', 'orig_4', 'orig_3', 'orig_2', 'orig_3']);
    expect(parts.map((p) => p.text).join(), isNot(contains('карта 1')));
    expect(m.text, contains('карта 1'), reason: 'цитата сохраняет исходный читаемый текст');
  });

  test('неизвестные ссылки и обычные сообщения остаются текстом', () {
    for (final text in ['мотив 0; место 9; Подсказка р.9 №1', 'мотив 999999999999999999999999', 'мотив 1abc']) {
      final parts = ChatCardText.parse(message(text), board, hints);
      expect(parts.map((p) => p.text).join(), text);
      expect(parts.every((p) => p.cardId == null), isTrue);
    }
    expect(ChatCardText.parse(message('мотив 1', annotated: false), board, hints).single.cardId, isNull);
  });

  testWidgets('миниатюры помещаются в узкий текст и открываются нажатием', (tester) async {
    final m = message('Проверял: мотив 1 — по деталям: цветок.\nВерсия: способ 1.');
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: Center(child: SizedBox(
      width: 200,
      child: ChatCardText(message: m, parts: ChatCardText.parse(m, board, hints)),
    )))));
    await tester.pumpAndSettle();
    expect(find.byType(CardImage), findsNWidgets(2));
    expect(tester.takeException(), isNull);
    await tester.tap(find.byKey(const Key('chat-inline-m-1')));
    await tester.pumpAndSettle();
    expect(find.byType(CardImage), findsNWidgets(3));
    expect(tester.takeException(), isNull);
  });
}
