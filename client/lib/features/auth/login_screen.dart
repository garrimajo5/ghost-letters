import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/app_version.dart';
import '../../core/session.dart';
import '../../core/theme.dart';
import '../../widgets/common.dart';

/// Палитра заглушек-аватаров — та же, что на сервере (приглушённые цвета из дизайна).
const avatarPalette = ['#3E7C6E', '#6A5A9E', '#8A5A44', '#3D6A99', '#7A6A3A', '#9A4F6E', '#4F7F3F', '#5A6E82'];

/// Вход гостем: имя и цвет аватара. С того же устройства вернётся тот же игрок.
class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _nick = TextEditingController();
  String _color = avatarPalette.first;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    // После выхода из аккаунта подставляем прежние ник и цвет.
    final last = ref.read(sessionProvider.notifier).lastProfile;
    if (last != null) {
      _nick.text = last.nickname;
      if (avatarPalette.contains(last.avatarColor)) _color = last.avatarColor;
    }
  }

  @override
  void dispose() {
    _nick.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final nick = _nick.text.trim();
    if (nick.length < 2 || nick.length > 20) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Ник — от 2 до 20 символов')));
      return;
    }

    setState(() => _busy = true);
    final session = ref.read(sessionProvider.notifier);
    await runAction(context, () async {
      final tokens = await ref.read(apiProvider).loginGuest(session.deviceId, nick, _color);
      session.signIn(tokens);
    });
    if (mounted) setState(() => _busy = false);
  }

  /// Уже играю на другом устройстве: ввожу код оттуда — и это тот же аккаунт.
  Future<void> _loginByCode() async {
    final code = await showDialog<String>(context: context, builder: (_) => const _CodeDialog());
    if (code == null || !mounted) return;
    setState(() => _busy = true);
    final session = ref.read(sessionProvider.notifier);
    await runAction(context, () async {
      final tokens = await ref.read(apiProvider).loginByCode(session.deviceId, code);
      session.signIn(tokens);
    });
    if (mounted) setState(() => _busy = false);
  }

  @override
  Widget build(BuildContext context) {
    final nick = _nick.text.trim();
    const label = TextStyle(fontSize: 13, color: AppColors.muted, letterSpacing: 1);
    return Scaffold(
      body: SingleChildScrollView(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 480),
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              SizedBox(
                height: 300,
                child: Stack(fit: StackFit.expand, children: [
                  const AppImage('mailbox', fit: BoxFit.cover),
                  const DecoratedBox(
                    decoration: BoxDecoration(
                      gradient: LinearGradient(
                        begin: Alignment.topCenter,
                        end: Alignment.bottomCenter,
                        stops: [0.45, 1],
                        colors: [Colors.transparent, AppColors.bg],
                      ),
                    ),
                  ),
                  Align(
                    alignment: Alignment.bottomCenter,
                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                      FittedBox(
                        child: Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 16),
                          child: Text('ПИСЬМА ПРИЗРАКА', style: heading(46, color: AppColors.ice, spacing: 3, weight: FontWeight.w600)),
                        ),
                      ),
                      const SizedBox(height: 6),
                      const Text('Детективная игра на 2–12 человек', style: TextStyle(fontSize: 14, color: AppColors.muted)),
                    ]),
                  ),
                ]),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(24, 24, 24, 32),
                child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  const Text('ВАШЕ ИМЯ', style: label),
                  const SizedBox(height: 8),
                  TextField(
                    key: const Key('nickname'),
                    controller: _nick,
                    maxLength: 20,
                    style: const TextStyle(fontSize: 17),
                    decoration: const InputDecoration(hintText: 'Как вас называть', counterText: ''),
                    onChanged: (_) => setState(() {}),
                    onSubmitted: (_) => _submit(),
                  ),
                  const SizedBox(height: 20),
                  const Text('ЦВЕТ АВАТАРА', style: label),
                  const SizedBox(height: 10),
                  Wrap(spacing: 10, runSpacing: 10, children: [
                    for (final c in avatarPalette)
                      GestureDetector(
                        key: Key('color-$c'),
                        onTap: () => setState(() => _color = c),
                        child: Avatar(nickname: nick, color: c, size: 44, highlight: c == _color),
                      ),
                  ]),
                  const SizedBox(height: 8),
                  const Text(
                    'Портреты персонажей не используем — их легко спутать с картами персонажей в игре.',
                    style: TextStyle(fontSize: 12, color: AppColors.muted),
                  ),
                  const SizedBox(height: 32),
                  FilledButton(
                    key: const Key('login'),
                    style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56), textStyle: heading(20, spacing: 2)),
                    onPressed: _busy ? null : _submit,
                    child: _busy
                        ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2))
                        : const Text('ВОЙТИ'),
                  ),
                  const SizedBox(height: 8),
                  TextButton(
                    key: const Key('login-by-code'),
                    onPressed: _busy ? null : _loginByCode,
                    child: const Text('Уже играю на другом устройстве — войти по коду'),
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'Вы играете как гость на этом устройстве.',
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 13, color: AppColors.muted, height: 1.4),
                  ),
                  const SizedBox(height: 12),
                  Text(
                    AppVersion.current.label,
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 12, color: AppColors.dim),
                  ),
                ]),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}

class _CodeDialog extends StatefulWidget {
  const _CodeDialog();

  @override
  State<_CodeDialog> createState() => _CodeDialogState();
}

class _CodeDialogState extends State<_CodeDialog> {
  final _code = TextEditingController();

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  String get _clean => _code.text.toUpperCase().replaceAll(RegExp('[^A-Z0-9]'), '');

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('Вход по коду'),
        content: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text(
            'На устройстве, где вы уже играете, откройте свой профиль → «Войти на другом устройстве» и введите код.',
            style: TextStyle(fontSize: 13, color: AppColors.muted, height: 1.4),
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('link-code-input'),
            controller: _code,
            autofocus: true,
            textCapitalization: TextCapitalization.characters,
            style: heading(22, spacing: 3),
            decoration: const InputDecoration(hintText: 'ABCD-EFGH'),
            onChanged: (_) => setState(() {}),
          ),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Отмена')),
          FilledButton(
            key: const Key('link-code-submit'),
            onPressed: _clean.length == 8 ? () => Navigator.pop(context, _clean) : null,
            child: const Text('Войти'),
          ),
        ],
      );
}
