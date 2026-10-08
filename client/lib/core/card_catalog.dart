import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Набор карт из assets/cards/cards.json (тот же манифест, что импортирует сервер).
class CardSetInfo {
  const CardSetInfo({required this.code, required this.title, required this.cards});

  final String code;
  final String title;
  final List<String> cards;
}

List<CardSetInfo> parseCardCatalog(String raw) {
  final json = jsonDecode(raw) as Map<String, dynamic>;
  return [
    for (final s in (json['sets'] as List? ?? const []).cast<Map<String, dynamic>>())
      CardSetInfo(
        code: s['code'] as String,
        title: s['title'] as String? ?? s['code'] as String,
        cards: [for (final c in (s['cards'] as List? ?? const []).cast<Map<String, dynamic>>()) c['id'] as String],
      ),
  ];
}

/// Каталог всех карт: для выбора «любой карты из набора».
final cardCatalogProvider = FutureProvider<List<CardSetInfo>>(
  (ref) async => parseCardCatalog(await rootBundle.loadString('assets/cards/cards.json')),
);
