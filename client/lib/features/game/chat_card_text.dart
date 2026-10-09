import 'package:flutter/material.dart';

import '../../core/texts.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

/// Resolve only public board/hint references. Keep the stored text readable for
/// quotes, older clients and references that cannot be resolved in this snapshot.
class ChatCardText extends StatelessWidget {
  const ChatCardText({super.key, required this.message, required this.parts});

  final ChatMessage message;
  final List<({String text, String? cardId})> parts;

  static List<({String text, String? cardId})> parse(
    ChatMessage message,
    List<BoardRow> board,
    List<HintGroup> hints,
  ) {
    final text = message.text ?? '';
    if (message.cardNotes.isEmpty) return [(text: text, cardId: null)];
    final pattern = RegExp(
      r'(?<![а-яёa-z])(Подсказка р\.(\d+) №(\d+)|(мотив|место|способ|тайна)(?:\s*[,—:]\s*(?:карт[ау]\s*)?|\s+)(\d+)(?!\d)|вот эт[оу])(?![а-яёa-z])',
      caseSensitive: false,
    );
    final parts = <({String text, String? cardId})>[];
    var end = 0;
    for (final match in pattern.allMatches(text)) {
      String? cardId;
      var label = match[0]!;
      if (match[2] != null) {
        final round = int.tryParse(match[2]!);
        final index = (int.tryParse(match[3]!) ?? 0) - 1;
        for (final hint in hints) {
          if (hint.round == round && index >= 0 && index < hint.cards.length) {
            cardId = hint.cards[index];
            label = 'Подсказка р.$round';
            break;
          }
        }
      } else if (match[4] != null) {
        final index = (int.tryParse(match[5]!) ?? 0) - 1;
        for (final row in board) {
          if (T.categories[row.category]?.toLowerCase() == match[4]!.toLowerCase() && index >= 0 && index < row.cards.length) {
            cardId = row.cards[index];
            label = match[4]!;
            break;
          }
        }
      } else {
        final index = message.cardNotes.indexOf('кидал эту');
        if (index >= 0 && index < message.cardIds.length) cardId = message.cardIds[index];
      }
      if (cardId == null) continue;
      parts.add((text: text.substring(end, match.start), cardId: null));
      parts.add((text: label, cardId: cardId));
      end = match.end;
    }
    parts.add((text: text.substring(end), cardId: null));
    return parts;
  }

  @override
  Widget build(BuildContext context) => Text.rich(
        TextSpan(children: [
          for (var i = 0; i < parts.length; i++) ...[
            TextSpan(text: parts[i].text + (parts[i].cardId == null ? '' : ' ')),
            if (parts[i].cardId case final cardId?)
              WidgetSpan(
                alignment: PlaceholderAlignment.middle,
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 3, horizontal: 2),
                  child: Semantics(
                    label: 'Карта: ${parts[i].text}. Открыть',
                    button: true,
                    child: Tooltip(
                      message: parts[i].text,
                      child: GestureDetector(
                        key: Key('chat-inline-${message.id}-$i'),
                        onTap: () => showCardZoom(context, cardId, caption: parts[i].text),
                        child: CardImage(cardId: cardId, size: 44, radius: 6),
                      ),
                    ),
                  ),
                ),
              ),
          ],
        ]),
        style: const TextStyle(fontSize: 14, height: 1.4),
      );
}
