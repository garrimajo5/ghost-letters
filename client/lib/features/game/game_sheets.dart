import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/realtime.dart';
import '../../core/texts.dart';
import '../../core/theme.dart';
import '../../core/voice.dart';
import '../../models/models.dart';
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
        isScrollControlled: true,
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
    final v = widget.screen.view;
    String? label;
    if (v != null) {
      for (var r = 0; r < v.board.length; r++) {
        final c = v.board[r].cards.indexOf(widget.cardId);
        if (c >= 0) label = '${T.category(v.board[r].category)}, карта ${c + 1}';
      }
    }
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          AnimatedContainer(
            duration: const Duration(milliseconds: 200),
            foregroundDecoration: BoxDecoration(
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: mark.believed ? AppColors.believed : AppColors.border, width: mark.believed ? 4 : 2),
            ),
            child: CardImage(cardId: widget.cardId, size: 200, radius: 20),
          ),
          const SizedBox(height: 10),
          Text('${label ?? 'Карта'} · пометки видите только вы', style: const TextStyle(fontSize: 13, color: AppColors.muted)),
          const SizedBox(height: 14),
          Container(
            decoration: BoxDecoration(color: AppColors.surface, borderRadius: BorderRadius.circular(16)),
            child: Column(children: [
              _Counter(
                key: const Key('mark-crosses'),
                badge: '✕',
                color: AppColors.red,
                label: 'Проверяли — подсказки не было',
                value: mark.crosses,
                onChanged: (v) => _set(mark.copyWith(crosses: v)),
              ),
              const Divider(height: 1, color: AppColors.surface2),
              _Counter(
                key: const Key('mark-checks'),
                badge: '✓',
                color: AppColors.green,
                label: 'Подсказки указывают сюда',
                value: mark.checks,
                onChanged: (v) => _set(mark.copyWith(checks: v)),
              ),
              Material(
                color: mark.believed ? AppColors.green : Colors.transparent,
                borderRadius: const BorderRadius.vertical(bottom: Radius.circular(16)),
                child: InkWell(
                  key: const Key('mark-believed'),
                  borderRadius: const BorderRadius.vertical(bottom: Radius.circular(16)),
                  onTap: () => _set(mark.copyWith(believed: !mark.believed)),
                  child: SizedBox(
                    height: 52,
                    child: Center(
                      child: Text(
                        mark.believed ? '● Считаю истинной' : '○ Считаю истинной',
                        style: TextStyle(
                          fontSize: 15,
                          fontWeight: FontWeight.w600,
                          color: mark.believed ? Colors.white : AppColors.greenSoft,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ]),
          ),
          const SizedBox(height: 10),
          const Text(
            'На поле подсветка включается удержанием карты',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 12, color: AppColors.muted),
          ),
          const SizedBox(height: 12),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: AppColors.ice,
              foregroundColor: AppColors.bg,
              minimumSize: const Size(140, 48),
              shape: const StadiumBorder(),
              textStyle: const TextStyle(fontFamily: AppFonts.body, fontSize: 15, fontWeight: FontWeight.w600),
            ),
            onPressed: () => Navigator.pop(context),
            child: const Text('Готово'),
          ),
        ]),
      ),
    );
  }
}

class _Counter extends StatelessWidget {
  const _Counter({super.key, required this.badge, required this.color, required this.label, required this.value, required this.onChanged});

  final String badge;
  final Color color;
  final String label;
  final int value;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    Widget button(String text, VoidCallback? onTap, String tooltip) => SizedBox(
          width: 40,
          height: 40,
          child: OutlinedButton(
            style: OutlinedButton.styleFrom(
              padding: EdgeInsets.zero,
              minimumSize: const Size(40, 40),
              backgroundColor: AppColors.bg,
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
              textStyle: const TextStyle(fontSize: 18),
            ),
            onPressed: onTap,
            child: Tooltip(message: tooltip, child: Text(text)),
          ),
        );
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      child: Row(children: [
        Container(
          width: 28,
          height: 28,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          alignment: Alignment.center,
          child: Text(badge, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700)),
        ),
        const SizedBox(width: 10),
        Expanded(child: Text(label, style: const TextStyle(fontSize: 14, height: 1.3))),
        button('−', value > 0 ? () => onChanged(value - 1) : null, 'Меньше'),
        SizedBox(width: 30, child: Text('$value', textAlign: TextAlign.center, style: heading(20, spacing: 0))),
        button('+', () => onChanged(value + 1), 'Больше'),
      ]),
    );
  }
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
            widget.screen.setSuspicion(widget.userId, _suspicion.round());
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

  /// Долгое нажатие на сообщение: цитата уходит в личную заметку об авторе.
  Future<void> _quoteToNote(ChatMessage m) async {
    final api = ref.read(apiProvider);
    final authorId = m.authorId!;
    final saved = await runAction(context, () async {
      final notes = await api.notes(_gameId);
      final note = notes.where((n) => n['targetUserId'] == authorId).firstOrNull;
      final body = (note?['body'] as String? ?? '').trim();
      final quote = 'Раунд ${m.round}: «${m.text}»';
      final text = body.isEmpty ? quote : '$body\n$quote';
      await api.saveNote(_gameId, authorId, ((note?['suspicion'] as num?) ?? 0).toInt(),
          text.length > 2000 ? text.substring(text.length - 2000) : text);
      return true;
    });
    if (saved == true && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Цитата добавлена в заметку о ${widget.screen.nick(authorId)}')),
      );
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
                    final quotable = m.authorId != null && m.authorId != screen.view?.me?.id && (m.text ?? '').isNotEmpty;
                    final mine = m.authorId != null && m.authorId == screen.view?.me?.id;
                    if (m.authorId == null) {
                      return Center(
                        child: Container(
                          margin: const EdgeInsets.symmetric(vertical: 4),
                          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 5),
                          decoration: BoxDecoration(color: AppColors.surface, borderRadius: BorderRadius.circular(99)),
                          child: Text(m.text ?? '', style: const TextStyle(fontSize: 12, color: AppColors.muted)),
                        ),
                      );
                    }
                    final bubble = GestureDetector(
                      onLongPress: quotable ? () => _quoteToNote(m) : null,
                      child: Container(
                        constraints: BoxConstraints(maxWidth: MediaQuery.of(context).size.width * 0.72),
                        padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
                        decoration: BoxDecoration(
                          color: mine ? AppColors.border : AppColors.surface,
                          borderRadius: BorderRadius.only(
                            topLeft: const Radius.circular(14),
                            topRight: const Radius.circular(14),
                            bottomLeft: Radius.circular(mine ? 14 : 4),
                            bottomRight: Radius.circular(mine ? 4 : 14),
                          ),
                        ),
                        child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                          if (!mine)
                            Padding(
                              padding: const EdgeInsets.only(bottom: 2),
                              child: Text(author?.nickname ?? '?', style: const TextStyle(fontSize: 12, color: AppColors.ice)),
                            ),
                          if (m.isVoice)
                            _VoiceTile(voice: voice, mediaId: m.mediaId!, durationMs: m.durationMs ?? 0)
                          else if ((m.text ?? '').isNotEmpty)
                            Text(m.text!, style: const TextStyle(fontSize: 14, height: 1.4)),
                          if (m.cardIds.isNotEmpty)
                            Padding(
                              padding: const EdgeInsets.only(top: 6),
                              child: Wrap(spacing: 4, runSpacing: 4, children: [
                                for (final c in m.cardIds) CardImage(cardId: c, size: 44, radius: 8),
                              ]),
                            ),
                        ]),
                      ),
                    );
                    return Padding(
                      padding: const EdgeInsets.symmetric(vertical: 5),
                      child: Row(
                        mainAxisAlignment: mine ? MainAxisAlignment.end : MainAxisAlignment.start,
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          if (!mine) ...[
                            Avatar(nickname: author?.nickname ?? '?', color: author?.avatarColor ?? '#3D6A99', size: 28),
                            const SizedBox(width: 8),
                          ],
                          Flexible(child: bubble),
                        ],
                      ),
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
                      Expanded(
                        child: TextField(
                          controller: _text,
                          maxLength: 1000,
                          minLines: 1,
                          maxLines: 4,
                          decoration: InputDecoration(
                            hintText: _channel == 'public' ? 'Сообщение всем' : 'Сообщение команде',
                            counterText: '',
                            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                            border: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: const BorderSide(color: AppColors.border)),
                            enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: const BorderSide(color: AppColors.border)),
                            focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: const BorderSide(color: AppColors.amber)),
                          ),
                          onSubmitted: (_) => _send(),
                        ),
                      ),
                      const SizedBox(width: 6),
                      _RoundButton(tooltip: 'Упомянуть карту', icon: Icons.style_outlined, onPressed: _pickCards),
                      const SizedBox(width: 6),
                      if (_sending)
                        const SizedBox(width: 44, child: Center(child: SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2))))
                      else
                        _RoundButton(tooltip: 'Голосовое', icon: Icons.mic_none, onPressed: _startRecording, filled: true),
                      const SizedBox(width: 6),
                      _RoundButton(tooltip: 'Отправить', icon: Icons.send, onPressed: _send),
                    ]),
            ),
        ]),
      ),
    );
  }
}

class _RoundButton extends StatelessWidget {
  const _RoundButton({required this.tooltip, required this.icon, required this.onPressed, this.filled = false});

  final String tooltip;
  final IconData icon;
  final VoidCallback onPressed;
  final bool filled;

  @override
  Widget build(BuildContext context) => Tooltip(
        message: tooltip,
        child: Material(
          color: filled ? AppColors.amber : AppColors.surface,
          shape: CircleBorder(side: filled ? BorderSide.none : const BorderSide(color: AppColors.border)),
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: onPressed,
            child: SizedBox.square(dimension: 44, child: Icon(icon, size: 20, color: filled ? AppColors.onAmber : AppColors.ice)),
          ),
        ),
      );
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
