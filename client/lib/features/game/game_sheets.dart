import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../widgets/common.dart';
import 'game_screen.dart';
import 'game_state.dart';

/// Пометки на карте поля: счётчики ✕ и ✓ и «считаю истинной».
class MarkSheet extends StatefulWidget {
  const MarkSheet({super.key, required this.screen, required this.cardId});

  final GameScreenState screen;
  final String cardId;

  static Future<void> show(BuildContext context, GameScreenState screen, String cardId) => showModalBottomSheet<void>(
        context: context,
        builder: (_) => MarkSheet(screen: screen, cardId: cardId),
      );

  @override
  State<MarkSheet> createState() => _MarkSheetState();
}

class _MarkSheetState extends State<MarkSheet> {
  late CardMark mark = widget.screen.marks[widget.cardId] ?? const CardMark();

  void _set(CardMark m) {
    setState(() => mark = m);
    widget.screen.saveMark(widget.cardId, m);
  }

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          CardImage(cardId: widget.cardId, size: 160),
          const SizedBox(height: 12),
          _Counter(label: '✕ против', value: mark.crosses, onChanged: (v) => _set(mark.copyWith(crosses: v))),
          _Counter(label: '✓ за', value: mark.checks, onChanged: (v) => _set(mark.copyWith(checks: v))),
          SwitchListTile(
            title: const Text('Считаю истинной'),
            activeThumbColor: AppTheme.believed,
            value: mark.believed,
            onChanged: (v) => _set(mark.copyWith(believed: v)),
          ),
          const Text('Пометки видите только вы. Удержание карты на поле — быстрое «считаю истинной».',
              style: TextStyle(fontSize: 12, color: Colors.white60)),
        ]),
      ),
    );
  }
}

class _Counter extends StatelessWidget {
  const _Counter({required this.label, required this.value, required this.onChanged});

  final String label;
  final int value;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) => ListTile(
        title: Text(label),
        trailing: Row(mainAxisSize: MainAxisSize.min, children: [
          IconButton(onPressed: value > 0 ? () => onChanged(value - 1) : null, icon: const Icon(Icons.remove)),
          Text('$value'),
          IconButton(onPressed: () => onChanged(value + 1), icon: const Icon(Icons.add)),
        ]),
      );
}

/// Личная заметка об игроке: степень подозрения и текст.
class NoteSheet extends ConsumerStatefulWidget {
  const NoteSheet({super.key, required this.screen, required this.userId});

  final GameScreenState screen;
  final String userId;

  static Future<void> show(BuildContext context, GameScreenState screen, String userId) => showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        builder: (_) => NoteSheet(screen: screen, userId: userId),
      );

  @override
  ConsumerState<NoteSheet> createState() => _NoteSheetState();
}

class _NoteSheetState extends ConsumerState<NoteSheet> {
  final _body = TextEditingController();
  double _suspicion = 0;
  bool _loaded = false;

  static const _labels = {-2: 'Точно чист', -1: 'Скорее чист', 0: 'Не знаю', 1: 'Подозреваю', 2: 'Это он!'};

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final notes = await runAction(context, () => ref.read(apiProvider).notes(widget.screen.widget.gameId));
    final mine = notes?.where((n) => n['targetUserId'] == widget.userId).firstOrNull;
    if (!mounted) return;
    setState(() {
      _loaded = true;
      if (mine != null) {
        _suspicion = ((mine['suspicion'] as num?) ?? 0).toDouble();
        _body.text = mine['body'] as String? ?? '';
      }
    });
  }

  @override
  void dispose() {
    _body.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final r = widget.screen.rosterOf(widget.userId);
    final info = widget.screen.view?.player(widget.userId);
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(context).viewInsets.bottom + 16),
      child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(children: [
          Avatar(nickname: r?.nickname ?? '?', color: r?.avatarColor ?? '#5C7C99'),
          const SizedBox(width: 12),
          Expanded(child: Text(r?.nickname ?? '?', style: Theme.of(context).textTheme.titleMedium)),
          if (info?.knownRole != null) Chip(label: Text(T.role(info!.knownRole))),
        ]),
        const SizedBox(height: 12),
        if (!_loaded) const LinearProgressIndicator(),
        Text('Подозрение: ${_labels[_suspicion.round()]}'),
        Slider(value: _suspicion, min: -2, max: 2, divisions: 4, onChanged: (v) => setState(() => _suspicion = v)),
        TextField(
          controller: _body,
          maxLines: 4,
          maxLength: 2000,
          decoration: const InputDecoration(hintText: 'Что говорил, что отправлял, в чём путался…'),
        ),
        FilledButton(
          onPressed: () async {
            await runAction(
              context,
              () => ref.read(apiProvider).saveNote(widget.screen.widget.gameId, widget.userId, _suspicion.round(), _body.text),
            );
            if (context.mounted) Navigator.pop(context);
          },
          child: const Text('Сохранить заметку'),
        ),
      ]),
    );
  }
}

/// Чат партии: общий канал и, для команды Убийцы, свой канал.
class ChatSheet extends ConsumerStatefulWidget {
  const ChatSheet({super.key, required this.screen});

  final GameScreenState screen;

  static Future<void> show(BuildContext context, GameScreenState screen) => showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        useSafeArea: true,
        builder: (_) => ChatSheet(screen: screen),
      );

  @override
  ConsumerState<ChatSheet> createState() => _ChatSheetState();
}

class _ChatSheetState extends ConsumerState<ChatSheet> {
  final _text = TextEditingController();
  String _channel = 'public';

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  bool get _killerTeam => const {'Killer', 'Accomplice'}.contains(widget.screen.view?.me?.role);

  Future<void> _send() async {
    final text = _text.text.trim();
    if (text.isEmpty) return;
    final sent = await runAction(
      context,
      () => ref.read(apiProvider).sendChat(widget.screen.widget.gameId, text, channel: _channel),
    );
    if (sent != null) _text.clear();
  }

  @override
  Widget build(BuildContext context) {
    final screen = widget.screen;
    final canWrite = screen.view?.me != null;
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SizedBox(
        height: MediaQuery.of(context).size.height * 0.75,
        child: Column(children: [
          if (_killerTeam || screen.view?.isGhost == true)
            Padding(
              padding: const EdgeInsets.all(8),
              child: SegmentedButton<String>(
                segments: const [
                  ButtonSegment(value: 'public', label: Text('Общий')),
                  ButtonSegment(value: 'killer_team', label: Text('Команда Убийцы')),
                ],
                selected: {_channel},
                onSelectionChanged: (v) => setState(() => _channel = v.first),
              ),
            ),
          Expanded(
            child: StreamBuilder<Object?>(
              stream: ref.read(realtimeProvider).chat,
              builder: (context, _) {
                final messages = screen.chat.where((m) => m.channel == _channel).toList();
                return ListView.builder(
                reverse: true,
                padding: const EdgeInsets.all(8),
                itemCount: messages.length,
                itemBuilder: (context, i) {
                  final m = messages[messages.length - 1 - i];
                  final author = screen.rosterOf(m.authorId);
                  return ListTile(
                    leading: Avatar(nickname: author?.nickname ?? '?', color: author?.avatarColor ?? '#5C7C99', size: 32),
                    title: Text(author?.nickname ?? 'Система', style: Theme.of(context).textTheme.labelMedium),
                    subtitle: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(m.kind == 'voice' ? '🎤 Голосовое сообщение' : (m.text ?? '')),
                      if (m.cardIds.isNotEmpty)
                        Wrap(spacing: 4, children: [for (final c in m.cardIds) CardImage(cardId: c, size: 36)]),
                    ]),
                  );
                },
              );
              },
            ),
          ),
          if (canWrite)
            Padding(
              padding: const EdgeInsets.all(8),
              child: Row(children: [
                Expanded(
                  child: TextField(
                    controller: _text,
                    maxLength: 1000,
                    decoration: const InputDecoration(hintText: 'Сообщение', counterText: ''),
                    onSubmitted: (_) => _send(),
                  ),
                ),
                IconButton(onPressed: _send, icon: const Icon(Icons.send)),
              ]),
            ),
        ]),
      ),
    );
  }
}
