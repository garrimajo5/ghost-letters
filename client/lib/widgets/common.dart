import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api.dart';

Color colorFromHex(String hex) {
  final v = int.tryParse(hex.replaceFirst('#', ''), radix: 16) ?? 0x7C6CF2;
  return Color(0xFF000000 | v);
}

/// Заглушка-аватар: цвет игрока и первая буква ника (портреты персонажей не используем).
class Avatar extends StatelessWidget {
  const Avatar({super.key, required this.nickname, required this.color, this.size = 40, this.highlight = false});

  final String nickname;
  final String color;
  final double size;
  final bool highlight;

  @override
  Widget build(BuildContext context) {
    final letter = nickname.isEmpty ? '?' : nickname.characters.first.toUpperCase();
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: colorFromHex(color),
        shape: BoxShape.circle,
        border: highlight ? Border.all(color: Colors.white, width: 3) : null,
      ),
      alignment: Alignment.center,
      child: Text(letter, style: TextStyle(fontSize: size * 0.45, fontWeight: FontWeight.bold, color: Colors.white)),
    );
  }
}

/// Карта улики: картинка из assets/cards, а пока её нет — заглушка с номером.
class CardImage extends StatelessWidget {
  const CardImage({super.key, required this.cardId, this.size = 64});

  final String cardId;
  final double size;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(size * 0.08),
      child: Image.asset(
        'assets/cards/$cardId.webp',
        width: size,
        height: size,
        fit: BoxFit.cover,
        errorBuilder: (context, error, stack) => Container(
          width: size,
          height: size,
          color: const Color(0xFFF3E9D2),
          alignment: Alignment.center,
          child: Text(
            cardId.replaceFirst('orig_', '#'),
            style: TextStyle(fontSize: size * 0.18, color: Colors.brown.shade700, fontWeight: FontWeight.w600),
          ),
        ),
      ),
    );
  }
}

/// Обратный отсчёт до дедлайна фазы.
class Countdown extends StatefulWidget {
  const Countdown({super.key, required this.deadline});

  final DateTime? deadline;

  @override
  State<Countdown> createState() => _CountdownState();
}

class _CountdownState extends State<Countdown> {
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => setState(() {}));
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final deadline = widget.deadline;
    if (deadline == null) return const SizedBox.shrink();
    final left = deadline.difference(DateTime.now());
    final s = left.isNegative ? Duration.zero : left;
    final text = s.inHours > 0
        ? '${s.inHours} ч ${s.inMinutes % 60} мин'
        : '${s.inMinutes}:${(s.inSeconds % 60).toString().padLeft(2, '0')}';
    return Chip(
      avatar: const Icon(Icons.timer_outlined, size: 18),
      label: Text(text),
      backgroundColor: s.inSeconds < 10 ? Colors.red.shade900 : null,
    );
  }
}

/// Выполнить действие и показать ошибку сервера внизу экрана.
Future<T?> runAction<T>(BuildContext context, Future<T> Function() action) async {
  try {
    return await action();
  } catch (e) {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(ApiError.from(e).message)));
    }
    return null;
  }
}
