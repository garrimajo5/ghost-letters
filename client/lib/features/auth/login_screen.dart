import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/session.dart';
import '../../widgets/common.dart';

/// Палитра заглушек-аватаров — та же, что на сервере.
const avatarPalette = ['#7C6CF2', '#E5647A', '#3FB68B', '#F2A541', '#4AA3DF', '#B370D9', '#E57F4F', '#5C7C99'];

/// Вход гостем: ник и цвет аватара. С того же устройства вернётся тот же игрок.
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

  @override
  Widget build(BuildContext context) {
    final nick = _nick.text.trim();
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text('Письма призрака', textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineMedium),
                  const SizedBox(height: 8),
                  const Text('Детективная игра для компании', textAlign: TextAlign.center),
                  const SizedBox(height: 32),
                  Center(child: Avatar(nickname: nick, color: _color, size: 80)),
                  const SizedBox(height: 24),
                  TextField(
                    key: const Key('nickname'),
                    controller: _nick,
                    maxLength: 20,
                    decoration: const InputDecoration(labelText: 'Ваш ник'),
                    onChanged: (_) => setState(() {}),
                    onSubmitted: (_) => _submit(),
                  ),
                  const SizedBox(height: 8),
                  Wrap(
                    alignment: WrapAlignment.center,
                    spacing: 10,
                    runSpacing: 10,
                    children: [
                      for (final c in avatarPalette)
                        GestureDetector(
                          onTap: () => setState(() => _color = c),
                          child: Avatar(nickname: '', color: c, size: 36, highlight: c == _color),
                        ),
                    ],
                  ),
                  const SizedBox(height: 32),
                  FilledButton(
                    key: const Key('login'),
                    onPressed: _busy ? null : _submit,
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: _busy ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Войти'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
