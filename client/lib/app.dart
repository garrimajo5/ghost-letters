import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'core/session.dart';
import 'core/theme.dart';
import 'features/auth/login_screen.dart';
import 'features/game/game_screen.dart';
import 'features/history/history_screen.dart';
import 'features/home/home_screen.dart';
import 'features/lobby/lobby_screen.dart';
import 'features/profile/profile_screen.dart';
import 'features/rules/rules_screen.dart';

/// Переходы: без сессии — на вход, после входа — на главную.
final routerProvider = Provider<GoRouter>((ref) {
  final signedIn = ValueNotifier<bool>(ref.read(sessionProvider).isSignedIn);
  ref.listen(sessionProvider, (_, next) => signedIn.value = next.isSignedIn);
  ref.onDispose(signedIn.dispose);

  return GoRouter(
    initialLocation: '/',
    refreshListenable: signedIn,
    redirect: (context, state) {
      final atLogin = state.matchedLocation == '/login';
      if (!signedIn.value) return atLogin ? null : '/login';
      return atLogin ? '/' : null;
    },
    routes: [
      GoRoute(path: '/login', builder: (context, state) => const LoginScreen()),
      GoRoute(path: '/', builder: (context, state) => const HomeScreen()),
      GoRoute(path: '/lobby/:id', builder: (_, s) => LobbyScreen(lobbyId: s.pathParameters['id']!)),
      GoRoute(path: '/game/:id', builder: (_, s) => GameScreen(gameId: s.pathParameters['id']!)),
      GoRoute(path: '/history', builder: (_, __) => const HistoryScreen()),
      GoRoute(path: '/rules', builder: (_, __) => const RulesScreen()),
      GoRoute(path: '/profile/:id', builder: (_, s) => ProfileScreen(userId: s.pathParameters['id']!)),
    ],
  );
});

class GhostLettersApp extends ConsumerWidget {
  const GhostLettersApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => MaterialApp.router(
        title: 'Письма призрака',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.build(),
        routerConfig: ref.watch(routerProvider),
      );
}
