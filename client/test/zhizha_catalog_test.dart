import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/card_catalog.dart';
import 'package:ghost_letters/models/models.dart';

void main() {
  test('Жижа: все 200 финальных карт и их разметка доступны в игре', () {
    final sets = parseCardCatalog(File('assets/cards/cards.json').readAsStringSync());
    final set = sets.singleWhere((s) => s.code == 'zhizha');
    expect(set.title, 'Жижа');
    expect(set.cards, List.generate(200, (i) => 'zhizha_${(i + 1).toString().padLeft(4, '0')}'));
    final draft = jsonDecode(File('../docs/card-sets/zhizha.json').readAsStringSync()) as Map<String, dynamic>;
    for (final field in ['names', 'tags', 'associations']) {
      final values = jsonDecode(File('assets/cards/$field.json').readAsStringSync()) as Map<String, dynamic>;
      for (final card in draft['cards'] as List) {
        expect(values[card['id']], card[field], reason: '${card['id']}/$field');
      }
    }
    for (final id in set.cards) {
      expect(File('assets/cards/$id.webp').readAsBytesSync(),
          orderedEquals(File('../docs/card-sets/zhizha/$id.webp').readAsBytesSync()), reason: id);
    }
  });

  test('новые настройки включают Жижу, явный выбор наборов сохраняется', () {
    expect(const LobbySettings().cardSets, contains('zhizha'));
    expect(LobbySettings.fromJson({}).cardSets, contains('zhizha'));
    expect(LobbySettings.fromJson({'cardSets': ['original']}).cardSets, ['original']);
  });
}
