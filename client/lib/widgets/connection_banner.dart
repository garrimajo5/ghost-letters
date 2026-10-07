import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/realtime.dart';
import '../core/theme.dart';

/// Плашка «Нет связи — переподключаемся». Появляется, если связь с хабом пропала дольше чем на [delay],
/// чтобы короткие переподключения не мигали.
class ConnectionBanner extends ConsumerStatefulWidget {
  const ConnectionBanner({super.key, this.delay = const Duration(milliseconds: 1500)});

  final Duration delay;

  @override
  ConsumerState<ConnectionBanner> createState() => _ConnectionBannerState();
}

class _ConnectionBannerState extends ConsumerState<ConnectionBanner> {
  StreamSubscription<bool>? _sub;
  Timer? _timer;
  bool _offline = false;
  bool _retrying = false;

  @override
  void initState() {
    super.initState();
    _sub = ref.read(realtimeProvider).connected.listen((ok) {
      _timer?.cancel();
      if (ok) {
        if (mounted && _offline) setState(() => _offline = false);
      } else {
        _timer = Timer(widget.delay, () {
          if (mounted) setState(() => _offline = true);
        });
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _sub?.cancel();
    super.dispose();
  }

  Future<void> _retry() async {
    setState(() => _retrying = true);
    try {
      await ref.read(realtimeProvider).resync();
    } catch (_) {
      // Не вышло — хаб продолжит попытки сам.
    }
    if (mounted) setState(() => _retrying = false);
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedSize(
      duration: const Duration(milliseconds: 200),
      child: !_offline
          ? const SizedBox(width: double.infinity)
          : Container(
              key: const Key('offline-banner'),
              width: double.infinity,
              color: AppColors.red,
              padding: const EdgeInsets.fromLTRB(16, 6, 8, 6),
              child: Row(children: [
                const Icon(Icons.wifi_off, size: 18, color: Colors.white),
                const SizedBox(width: 10),
                const Expanded(
                  child: Text('Нет связи с сервером — переподключаемся…', style: TextStyle(fontSize: 13, color: Colors.white)),
                ),
                if (_retrying)
                  const Padding(
                    padding: EdgeInsets.all(10),
                    child: SizedBox.square(dimension: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white)),
                  )
                else
                  TextButton(
                    onPressed: _retry,
                    style: TextButton.styleFrom(foregroundColor: Colors.white),
                    child: const Text('Повторить'),
                  ),
              ]),
            ),
    );
  }
}
