import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'sound.dart';

/// One lifecycle owner across routes; the first pointer/key gesture unlocks audio.
class SoundScope extends ConsumerStatefulWidget {
  const SoundScope({super.key, required this.router, required this.child});
  final GoRouter router;
  final Widget child;
  @override
  ConsumerState<SoundScope> createState() => _SoundScopeState();
}

class _SoundScopeState extends ConsumerState<SoundScope> {
  late final Sound _sound;
  late final AppLifecycleListener _lifecycle;
  @override
  void initState() {
    super.initState();
    _sound = ref.read(soundProvider);
    _lifecycle = AppLifecycleListener(onStateChange: (state) {
      _sound.active(state == AppLifecycleState.resumed);
    });
    widget.router.routeInformationProvider.addListener(_route);
    HardwareKeyboard.instance.addHandler(_key);
    _route();
  }

  bool _key(KeyEvent event) {
    if (event is KeyDownEvent) _sound.unlock();
    return false;
  }

  void _route() {
    _sound.scene(widget.router.routeInformationProvider.value.uri.path
            .startsWith('/game/')
        ? Music.game
        : Music.menu);
  }

  @override
  void dispose() {
    widget.router.routeInformationProvider.removeListener(_route);
    HardwareKeyboard.instance.removeHandler(_key);
    _lifecycle.dispose();
    _sound.active(false);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Listener(
        behavior: HitTestBehavior.translucent,
        onPointerDown: (_) => _sound.unlock(),
        child: widget.child,
      );
}
