import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/texts.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';

/// Список загружается при открытии; вход в выбранную партию всегда зрителем.
class WatchGamesSheet extends ConsumerStatefulWidget {
  const WatchGamesSheet({super.key});

  @override
  ConsumerState<WatchGamesSheet> createState() => _WatchGamesSheetState();
}

class _WatchGamesSheetState extends ConsumerState<WatchGamesSheet> {
  late Future<List<WatchableGame>> _games;
  bool _admin = false;

  @override
  void initState() {
    super.initState();
    _games = ref.read(apiProvider).watchableGames();
    // Админ может закрыть любую зависшую партию.
    ref.read(apiProvider).isAdmin().then((v) {
      if (mounted) setState(() => _admin = v);
    }).catchError((Object _) {});
  }

  Future<void> _close(WatchableGame game) async {
    if (!await confirmCloseGame(context) || !mounted) return;
    await runAction(context, () => ref.read(apiProvider).closeGame(game.gameId));
    if (mounted) _refresh();
  }

  void _refresh() => setState(() { _games = ref.read(apiProvider).watchableGames(); });

  @override
  Widget build(BuildContext context) => SafeArea(
        child: SizedBox(
          height: MediaQuery.sizeOf(context).height * 0.7,
          child: Column(children: [
            ListTile(
              title: const Text('Смотреть идущие партии'),
              subtitle: const Text('Поле, открытые подсказки и общий чат — без скрытых ролей и улик.'),
              trailing: IconButton(tooltip: 'Обновить', onPressed: _refresh, icon: const Icon(Icons.refresh)),
            ),
            Expanded(
              child: FutureBuilder<List<WatchableGame>>(
                future: _games,
                builder: (context, snapshot) {
                  if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
                  if (snapshot.hasError) return ErrorRetry(message: ApiError.from(snapshot.error!).message, onRetry: _refresh);
                  final games = snapshot.data!;
                  if (games.isEmpty) return const Center(child: Text('Сейчас нет доступных партий'));
                  return ListView.builder(
                    itemCount: games.length,
                    itemBuilder: (context, i) {
                      final game = games[i];
                      return ListTile(
                        key: Key('watch-game-${game.gameId}'),
                        leading: const Icon(Icons.visibility_outlined),
                        title: Text(game.title, maxLines: 2, overflow: TextOverflow.ellipsis),
                        subtitle: Text('${T.phase(game.phase)} · ${T.players(game.players)} · ${game.code}'),
                        trailing: _admin
                            ? IconButton(
                                key: Key('admin-close-${game.gameId}'),
                                tooltip: 'Закрыть партию (админ)',
                                onPressed: () => _close(game),
                                icon: const Icon(Icons.close),
                              )
                            : const Icon(Icons.chevron_right),
                        onTap: () => Navigator.pop(context, game.code),
                      );
                    },
                  );
                },
              ),
            ),
          ]),
        ),
      );
}
