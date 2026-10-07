import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../core/voice.dart';
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

/// Чат партии: общий канал и, для команды Убийцы, свой канал. Текст, голосовые и упоминания карт.
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
  final _cards = <String>[];
  String _channel = 'public';
  bool _recording = false;
  bool _sending = false;
  Timer? _ticker;
  late final Voice _voice;

  @override
  void initState() {
    super.initState();
    _voice = ref.read(voiceProvider);
  }

  @override
  void dispose() {
    _ticker?.cancel();
    if (_recording) _voice.cancel();
    _text.dispose();
    super.dispose();
  }

  String get _gameId => widget.screen.widget.gameId;

  bool get _killerTeam => const {'Killer', 'Accomplice'}.contains(widget.screen.view?.me?.role);

  Future<void> _send() async {
    final text = _text.text.trim();
    if (text.isEmpty && _cards.isEmpty) return;
    final sent = await runAction(
      context,
      () => ref.read(apiProvider).sendChat(_gameId, text.isEmpty ? '🃏' : text, channel: _channel, cards: List.of(_cards)),
    );
    if (sent != null && mounted) {
      _text.clear();
      setState(_cards.clear);
    }
  }

  Future<void> _startRecording() async {
    final ok = await runAction(context, _voice.start);
    if (ok != true) {
      if (ok == false && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Нет доступа к микрофону')));
      }
      return;
    }

    setState(() => _recording = true);
    _ticker = Timer.periodic(const Duration(milliseconds: 250), (_) {
      if (!mounted) return;
      if (_voice.elapsed >= Voice.maxDuration) {
        _stopRecording(send: true);
      } else {
        setState(() {});
      }
    });
  }

  Future<void> _stopRecording({required bool send}) async {
    _ticker?.cancel();
    final voice = _voice;
    setState(() => _recording = false);
    if (!send) {
      await voice.cancel();
      return;
    }

    final take = await voice.stop();
    if (take == null || !mounted) return;
    setState(() => _sending = true);
    await runAction(context, () async {
      final api = ref.read(apiProvider);
      final mediaId = await api.uploadVoice(take.path, take.durationMs);
      await api.sendChat(_gameId, '', channel: _channel, cards: List.of(_cards), mediaId: mediaId);
    });
    if (mounted) {
      setState(() {
        _sending = false;
        _cards.clear();
      });
    }
  }

  Future<void> _pickCards() async {
    final v = widget.screen.view;
    if (v == null) return;
    final options = <String>{
      for (final row in v.board) ...row.cards,
      for (final h in v.hints) ...h.cards,
      ...?v.me?.hand,
    }.toList();
    final picked = await showModalBottomSheet<String>(
      context: context,
      builder: (context) => GridView.count(
        crossAxisCount: 6,
        padding: const EdgeInsets.all(8),
        mainAxisSpacing: 4,
        crossAxisSpacing: 4,
        children: [
          for (final c in options)
            GestureDetector(onTap: () => Navigator.pop(context, c), child: CardImage(cardId: c, size: 56)),
        ],
      ),
    );
    if (picked != null && !_cards.contains(picked) && _cards.length < 5) setState(() => _cards.add(picked));
  }

  @override
  Widget build(BuildContext context) {
    final screen = widget.screen;
    final canWrite = screen.view?.me != null;
    final voice = _voice;
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
                        if (m.isVoice)
                          _VoiceTile(voice: voice, mediaId: m.mediaId!, durationMs: m.durationMs ?? 0)
                        else if ((m.text ?? '').isNotEmpty)
                          Text(m.text!),
                        if (m.cardIds.isNotEmpty)
                          Wrap(spacing: 4, children: [for (final c in m.cardIds) CardImage(cardId: c, size: 36)]),
                      ]),
                    );
                  },
                );
              },
            ),
          ),
          if (canWrite && _cards.isNotEmpty)
            SizedBox(
              height: 52,
              child: ListView(scrollDirection: Axis.horizontal, padding: const EdgeInsets.symmetric(horizontal: 8), children: [
                for (final c in _cards)
                  Padding(
                    padding: const EdgeInsets.only(right: 4),
                    child: GestureDetector(onTap: () => setState(() => _cards.remove(c)), child: CardImage(cardId: c, size: 48)),
                  ),
              ]),
            ),
          if (canWrite)
            Padding(
              padding: const EdgeInsets.all(8),
              child: _recording
                  ? Row(children: [
                      const Icon(Icons.fiber_manual_record, color: AppTheme.danger),
                      const SizedBox(width: 8),
                      Expanded(child: Text('Запись ${voice.elapsed.inSeconds} / ${Voice.maxDuration.inSeconds} с')),
                      TextButton(onPressed: () => _stopRecording(send: false), child: const Text('Отмена')),
                      FilledButton.icon(
                        onPressed: () => _stopRecording(send: true),
                        icon: const Icon(Icons.send),
                        label: const Text('Отправить'),
                      ),
                    ])
                  : Row(children: [
                      IconButton(tooltip: 'Упомянуть карту', onPressed: _pickCards, icon: const Icon(Icons.style_outlined)),
                      Expanded(
                        child: TextField(
                          controller: _text,
                          maxLength: 1000,
                          decoration: const InputDecoration(hintText: 'Сообщение', counterText: ''),
                          onSubmitted: (_) => _send(),
                        ),
                      ),
                      if (_sending)
                        const Padding(padding: EdgeInsets.all(12), child: SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2)))
                      else
                        IconButton(tooltip: 'Голосовое', onPressed: _startRecording, icon: const Icon(Icons.mic_none)),
                      IconButton(onPressed: _send, icon: const Icon(Icons.send)),
                    ]),
            ),
        ]),
      ),
    );
  }
}

/// Голосовое в ленте: кнопка воспроизведения и длительность.
class _VoiceTile extends StatelessWidget {
  const _VoiceTile({required this.voice, required this.mediaId, required this.durationMs});

  final Voice voice;
  final String mediaId;
  final int durationMs;

  @override
  Widget build(BuildContext context) => StreamBuilder<String?>(
        stream: voice.playing,
        builder: (context, snap) {
          final playing = snap.data == mediaId;
          return Row(mainAxisSize: MainAxisSize.min, children: [
            IconButton(
              icon: Icon(playing ? Icons.stop_circle_outlined : Icons.play_circle_outline),
              onPressed: () => runAction(context, () => playing ? voice.stopPlaying() : voice.play(mediaId)),
            ),
            Text('🎤 ${(durationMs / 1000).toStringAsFixed(0)} с'),
          ]);
        },
      );
}
