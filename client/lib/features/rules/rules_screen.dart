import 'package:flutter/material.dart';

import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../widgets/common.dart';

/// Памятка по правилам: цель, ход раунда, финал и роли — открывается с главной и из меню партии.
class RulesScreen extends StatelessWidget {
  const RulesScreen({super.key});

  static const _roles = ['Ghost', 'Detective', 'Killer', 'Accomplice', 'Witness', 'Expert', 'Blackmailer', 'Imitator'];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('ПРАВИЛА')),
      body: ListView(padding: const EdgeInsets.fromLTRB(16, 4, 16, 32), children: [
        const _Section(
          icon: Icons.flag_outlined,
          title: 'ЦЕЛЬ',
          lines: [
            'На столе ряды улик: Мотив, Место, Способ и — с дополнением — Тайна. В каждом ряду одна карта истинная.',
            'Детективы побеждают, если угадают все истинные улики — или все, кроме одной, но арестуют Убийцу.',
            'Призрак знает истину, но говорить не может: он помогает только письмами-подсказками.',
          ],
        ),
        const _Section(
          icon: Icons.nights_stay_outlined,
          title: 'НОЧЬ',
          lines: [
            'Каждый тайно смотрит свою роль.',
            'Убийца выбирает по одной истинной улике в каждом ряду. Сообщники видят его выбор, остальные — нет.',
            'Призрак может выложить первую зацепку с руки.',
          ],
        ),
        const _Section(
          icon: Icons.mail_outline,
          title: 'РАУНД',
          lines: [
            'Письма: каждый кладёт в почтовый ящик карту с руки (вдвоём — по две). Обычно — ту, что похожа на истинную улику.',
            'Призрак читает письма и открывает те, что указывают на истину. Остальные исчезают.',
            'Сброс: можно сбросить одну карту и добрать новую.',
            'Обсуждение: по рации по кругу или свободно в чате — текстом и голосовыми. Призрак молчит.',
          ],
        ),
        const _Section(
          icon: Icons.how_to_vote_outlined,
          title: 'ФИНАЛ',
          lines: [
            'Голосуют по каждому ряду: какая карта истинная. Затем — кто Убийца. Можно воздержаться.',
            'Ничья — короткое обсуждение и переголосование между лидерами, до трёх раз; дальше решает жребий.',
            'Если в игре Свидетель или Эксперт, Убийца пытается его вычислить. Угадал — команда Убийцы забирает победу.',
            'Шантажист побеждает один, если дело не раскрыто, его не нашли, а он сам назвал все истинные улики.',
          ],
        ),
        const _Section(
          icon: Icons.edit_note,
          title: 'ПОДСКАЗКИ В ПРИЛОЖЕНИИ',
          lines: [
            'Нажмите на карту поля, чтобы поставить ✕ (проверяли — не то) или ✓ (подсказки указывают сюда). Пометки видите только вы.',
            'Удержание карты — «считаю истинной», карта подсвечивается зелёным.',
            'Нажмите на игрока, чтобы записать заметку и степень подозрения. Долгое нажатие на сообщение в чате добавит цитату в заметку.',
          ],
        ),
        const SizedBox(height: 8),
        Text('РОЛИ', style: heading(18, color: AppColors.ice, spacing: 2)),
        const SizedBox(height: 8),
        for (final r in _roles)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Panel(
              padding: const EdgeInsets.all(10),
              child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                AppImage(roleImage(r), width: 56, height: 80, radius: 8),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(
                      T.role(r).toUpperCase(),
                      style: heading(17, color: isKillerTeam(r) ? AppColors.redSoft : AppColors.amber, spacing: 1),
                    ),
                    const SizedBox(height: 4),
                    Text(T.roleHints[r] ?? '', style: const TextStyle(fontSize: 14, height: 1.4)),
                  ]),
                ),
              ]),
            ),
          ),
      ]),
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({required this.icon, required this.title, required this.lines});

  final IconData icon;
  final String title;
  final List<String> lines;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: Panel(
          padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Icon(icon, size: 20, color: AppColors.amber),
              const SizedBox(width: 8),
              Text(title, style: sectionLabel(size: 13)),
            ]),
            const SizedBox(height: 8),
            for (final l in lines)
              Padding(
                padding: const EdgeInsets.only(bottom: 6),
                child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  const Padding(
                    padding: EdgeInsets.only(top: 7, right: 8),
                    child: SizedBox.square(dimension: 5, child: DecoratedBox(decoration: BoxDecoration(color: AppColors.dim, shape: BoxShape.circle))),
                  ),
                  Expanded(child: Text(l, style: const TextStyle(fontSize: 14, height: 1.45))),
                ]),
              ),
          ]),
        ),
      );
}
