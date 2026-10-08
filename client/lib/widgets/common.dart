import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/theme.dart';

Color colorFromHex(String hex) {
  final v = int.tryParse(hex.replaceFirst('#', ''), radix: 16) ?? 0x3D6A99;
  return Color(0xFF000000 | v);
}

/// Заглушка-аватар: цвет игрока и первая буква ника (портреты персонажей не используем).
class Avatar extends StatelessWidget {
  const Avatar({super.key, required this.nickname, required this.color, this.size = 40, this.highlight = false, this.ring});

  final String nickname;
  final String color;
  final double size;
  final bool highlight;

  /// Цвет обводки: янтарь — «вы», голубой — Призрак.
  final Color? ring;

  @override
  Widget build(BuildContext context) {
    final letter = nickname.isEmpty ? '' : nickname.characters.first.toUpperCase();
    final ringColor = ring ?? (highlight ? AppColors.amber : null);
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: colorFromHex(color),
        shape: BoxShape.circle,
        border: Border.all(color: ringColor ?? AppColors.bg, width: ringColor == null ? 0 : (size > 60 ? 3 : 2)),
      ),
      alignment: Alignment.center,
      child: Text(letter, style: heading(size * 0.44, color: const Color(0xFFF4F7FA), spacing: 0)),
    );
  }
}

/// Карта улики: картинка из assets/cards, а пока её нет — заглушка с номером.
class CardImage extends StatelessWidget {
  const CardImage({super.key, required this.cardId, this.size = 64, this.radius});

  final String cardId;
  final double size;
  final double? radius;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(radius ?? (size * 0.16).clamp(4, 14)),
      child: Image.asset(
        'assets/cards/$cardId.webp',
        width: size,
        height: size,
        fit: BoxFit.cover,
        errorBuilder: (context, error, stack) => Container(
          width: size,
          height: size,
          color: AppColors.surface2,
          alignment: Alignment.center,
          child: Text(
            cardId.replaceFirst('orig_', '#'),
            style: TextStyle(fontSize: size * 0.18, color: AppColors.muted, fontWeight: FontWeight.w600),
          ),
        ),
      ),
    );
  }
}

/// Картинка из assets/images (жетоны, рубашка роли, иллюстрации); если файла нет — пустое место.
class AppImage extends StatelessWidget {
  const AppImage(this.name, {super.key, this.width, this.height, this.fit = BoxFit.cover, this.circle = false, this.radius = 0});

  final String name;
  final double? width;
  final double? height;
  final BoxFit fit;
  final bool circle;
  final double radius;

  @override
  Widget build(BuildContext context) {
    final image = Image.asset(
      'assets/images/$name.webp',
      width: width,
      height: height,
      fit: fit,
      errorBuilder: (context, error, stack) => SizedBox(width: width, height: height),
    );
    if (circle) return ClipOval(child: image);
    if (radius > 0) return ClipRRect(borderRadius: BorderRadius.circular(radius), child: image);
    return image;
  }
}

/// Круглый жетон категории ряда: Мотив, Место, Способ, Тайна.
String categoryToken(String category) => switch (category) {
      'Motive' => 'token_motive',
      'Place' => 'token_place',
      'Method' => 'token_method',
      'Secret' => 'token_secret',
      _ => 'token_clues',
    };

/// Карта роли из прототипа; для ролей без своей картинки — рубашка.
String roleImage(String? role) => switch (role) {
      'Ghost' => 'role_ghost',
      'Detective' => 'role_detective',
      'Killer' => 'role_killer',
      'Accomplice' => 'role_accomplice',
      'Witness' => 'role_witness',
      'Imitator' => 'role_imitator',
      'Blackmailer' => 'role_blackmailer',
      _ => 'role_back',
    };

bool isKillerTeam(String? role) => role == 'Killer' || role == 'Accomplice';

/// Тёмная плашка-панель с мягким скруглением.
class Panel extends StatelessWidget {
  const Panel({super.key, required this.child, this.padding = const EdgeInsets.all(12), this.color = AppColors.surface, this.border});

  final Widget child;
  final EdgeInsets padding;
  final Color color;
  final Color? border;

  @override
  Widget build(BuildContext context) => Container(
        padding: padding,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(14),
          border: border == null ? null : Border.all(color: border!),
        ),
        child: child,
      );
}

/// Маленький круглый бейдж-счётчик: ✕ красный, ✓ зелёный. Значки — иконками, чтобы не зависеть от шрифта.
class CountBadge extends StatelessWidget {
  const CountBadge({super.key, this.text = '', required this.color, this.icon, this.fontSize = 10});

  final String text;
  final IconData? icon;
  final Color color;
  final double fontSize;

  @override
  Widget build(BuildContext context) => Container(
        height: fontSize + 6,
        constraints: BoxConstraints(minWidth: fontSize + 6),
        padding: EdgeInsets.symmetric(horizontal: text.isEmpty ? 0 : 4),
        decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(99)),
        alignment: Alignment.center,
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          if (icon != null) Icon(icon, size: fontSize + 1, color: Colors.white),
          if (text.isNotEmpty)
            Text(text, style: TextStyle(fontSize: fontSize, height: 1, color: Colors.white, fontWeight: FontWeight.w700)),
        ]),
      );
}

/// Обратный отсчёт до дедлайна фазы: янтарные цифры, последние 10 секунд — красные.
class Countdown extends StatefulWidget {
  const Countdown({super.key, required this.deadline, this.size = 18});

  final DateTime? deadline;
  final double size;

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
    return Text(
      text,
      key: const Key('countdown'),
      style: heading(widget.size, color: s.inSeconds < 10 && s.inHours == 0 ? AppColors.redBright : AppColors.amber, spacing: 0.5),
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

/// Карта крупно поверх экрана: нажмите в любом месте, чтобы закрыть.
Future<void> showCardZoom(BuildContext context, String cardId, {String? caption, String? actionLabel, VoidCallback? onAction}) =>
    showDialog<void>(
      context: context,
      barrierColor: const Color(0xEB050A10),
      builder: (context) {
        final size = (MediaQuery.sizeOf(context).shortestSide - 48).clamp(160.0, 420.0);
        return GestureDetector(
          key: const Key('card-zoom'),
          behavior: HitTestBehavior.opaque,
          onTap: () => Navigator.pop(context),
          child: Center(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              CardImage(cardId: cardId, size: size, radius: 20),
              const SizedBox(height: 12),
              if (caption != null) Text(caption, style: const TextStyle(fontSize: 14, color: AppColors.muted)),
              if (actionLabel != null && onAction != null) ...[
                const SizedBox(height: 12),
                FilledButton(
                  key: const Key('zoom-action'),
                  onPressed: () {
                    Navigator.pop(context);
                    onAction();
                  },
                  child: Text(actionLabel),
                ),
              ],
              const SizedBox(height: 4),
              const Text('Нажмите, чтобы закрыть', style: TextStyle(fontSize: 12, color: AppColors.dim)),
            ]),
          ),
        );
      },
    );

/// Поля страницы: на телефоне — обычные [side], на широком экране контент идёт колонкой
/// не шире [max] по центру (фон и прокрутка — во всю ширину).
EdgeInsets pageInsets(BuildContext context, {double max = 760, double side = 16, double top = 8, double bottom = 16}) {
  final width = MediaQuery.sizeOf(context).width;
  final h = width > max + side * 2 ? (width - max) / 2 : side;
  return EdgeInsets.fromLTRB(h, top, h, bottom);
}
