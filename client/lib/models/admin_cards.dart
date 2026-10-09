import 'models.dart';

class CardFeature {
  const CardFeature(
      {required this.tag, required this.label, required this.weight});
  factory CardFeature.fromJson(Json j) => CardFeature(
      tag: j['tag'] as String,
      label: j['label'] as String,
      weight: (j['weight'] as num).toDouble());
  final String tag;
  final String label;
  final double weight;
  Json toJson() => {'tag': tag, 'label': label, 'weight': weight};
}

class AdminCard {
  const AdminCard(
      {required this.id,
      required this.imageKey,
      required this.setCode,
      required this.isActive,
      required this.version,
      this.title,
      this.tags = const [],
      this.meanings = const [],
      this.details = const []});
  factory AdminCard.fromJson(Json j) {
    final a = Map<String, dynamic>.from(j['annotation'] as Map);
    List<CardFeature> features(String key) => (a[key] as List)
        .map((e) => CardFeature.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
    return AdminCard(
        id: j['id'] as String,
        imageKey: j['imageKey'] as String,
        setCode: j['setCode'] as String,
        title: j['title'] as String?,
        isActive: j['isActive'] as bool,
        version: j['version'] as int,
        tags: (a['tags'] as List).cast<String>(),
        meanings: features('meanings'),
        details: features('details'));
  }
  final String id;
  final String imageKey;
  final String setCode;
  final String? title;
  final bool isActive;
  final int version;
  final List<String> tags;
  final List<CardFeature> meanings;
  final List<CardFeature> details;
  Json toJson() => {
        'id': id,
        'imageKey': imageKey,
        'title': title,
        'setCode': setCode,
        'isActive': isActive,
        'version': version,
        'annotation': {
          'tags': tags,
          'meanings': meanings.map((m) => m.toJson()).toList(),
          'details': details.map((d) => d.toJson()).toList()
        }
      };
}

class AdminCardSet {
  const AdminCardSet(this.code, this.title);
  final String code;
  final String title;
}

class AdminCardPage {
  const AdminCardPage(
      {required this.cards, required this.total, required this.sets});
  factory AdminCardPage.fromJson(Json j) => AdminCardPage(
      cards: (j['cards'] as List)
          .map((e) => AdminCard.fromJson(Map<String, dynamic>.from(e as Map)))
          .toList(),
      total: j['total'] as int,
      sets: (j['sets'] as List)
          .map((e) => AdminCardSet(e['code'] as String, e['title'] as String))
          .toList());
  final List<AdminCard> cards;
  final int total;
  final List<AdminCardSet> sets;
}
