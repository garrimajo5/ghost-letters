import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/texts.dart';
import '../../core/voice.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'game_screen.dart';

bool theorySource(String note) => note == 'улика' || note.startsWith('кидал');
bool theoryNegative(String note) =>
    note.startsWith('исключ') || note.startsWith('не эта');
bool theoryLinked(ChatMessage message, int index) =>
    index > 0 &&
    message.cardNotes.isNotEmpty &&
    theorySource(message.cardNotes.first) &&
    ((index < message.cardNotes.length &&
            message.cardNotes[index].endsWith(':0')) ||
        (message.cardNotes.first.startsWith('кидал') &&
            (message.noteFor(index) ?? '').startsWith('проверял')));
String theoryLabel(String note) => theorySource(note)
    ? 'Улика'
    : theoryNegative(note)
        ? 'Против'
        : note.startsWith('проверял') && !note.contains('думаю')
            ? 'Проверка'
            : 'За';

/// Public statements only: selecting a speaker never reveals their private notes.
class TableStatements extends StatefulWidget {
  const TableStatements({super.key, required this.screen});
  final GameScreenState screen;
  @override
  State<TableStatements> createState() => _TableStatementsState();
}

class _TableStatementsState extends State<TableStatements> {
  ChatMessage? _message;
  Timer? _timer;
  int _visible = 0;
  final _seen = <String>{};
  final _queue = <ChatMessage>[];

  @override
  void initState() {
    super.initState();
    widget.screen.chatChanges.addListener(_changed);
    _changed(initial: true);
  }

  List<ChatMessage> get _messages => widget.screen.chat
      .where((m) =>
          m.channel == 'public' && m.cardIds.isNotEmpty && m.authorId != null)
      .toList();

  void _changed({bool initial = false}) {
    final messages = _messages;
    final fresh = messages.where((m) => !_seen.contains(m.id)).toList();
    _seen.addAll(messages.map((m) => m.id));
    if (initial || fresh.length > 5) {
      if (messages.isNotEmpty) _show(messages.last, animate: false);
    } else {
      _queue.addAll(fresh
          .where((m) => DateTime.now().difference(m.createdAt).inSeconds < 30));
      if (_queue.length > 5) _queue.removeRange(0, _queue.length - 5);
      if (_timer == null && _queue.isNotEmpty) _show(_queue.removeAt(0));
      if (_message == null && messages.isNotEmpty) {
        _show(messages.last, animate: false);
      }
    }
  }

  void _show(ChatMessage message, {bool animate = true}) {
    _timer?.cancel();
    _timer = null;
    void update() {
      _message = message;
      _visible = animate ? 1 : message.cardIds.length;
    }

    if (mounted) {
      setState(update);
    } else {
      update();
    }
    if (!animate) return;
    _timer = Timer.periodic(const Duration(seconds: 2), (timer) {
      if (_visible < message.cardIds.length) {
        setState(() => _visible++);
      } else {
        timer.cancel();
        _timer = null;
        if (_queue.isNotEmpty) _show(_queue.removeAt(0));
      }
    });
  }

  @override
  void dispose() {
    widget.screen.chatChanges.removeListener(_changed);
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final view = widget.screen.view!;
    if (!const {'Discussion', 'Night', 'Voting', 'VoteTie'}
        .contains(view.phase)) {
      return const SizedBox.shrink();
    }
    final message = _message;
    return Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Wrap(
                spacing: 8,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  if (view.me != null && view.me!.role != 'Ghost')
                    OutlinedButton.icon(
                        key: const Key('show-table-theory'),
                        icon: const Icon(Icons.account_tree_outlined),
                        label: const Text('Показать версию'),
                        onPressed: () => showModalBottomSheet<void>(
                            context: context,
                            isScrollControlled: true,
                            useSafeArea: true,
                            builder: (_) =>
                                TableTheorySheet(screen: widget.screen))),
                  if (message != null)
                    PopupMenuButton<ChatMessage>(
                        tooltip: 'Версии игроков',
                        itemBuilder: (_) => _messages.reversed
                            .take(20)
                            .map((m) => PopupMenuItem(
                                value: m,
                                child: Text(
                                    '${widget.screen.nick(m.authorId)} · раунд ${m.round}')))
                            .toList(),
                        onSelected: (m) {
                          _queue.clear();
                          _show(m);
                        },
                        child: Padding(
                            padding: const EdgeInsets.all(8),
                            child: Text(
                                'Версия: ${widget.screen.nick(message.authorId)} ▾'))),
                ]),
            if (message != null) ...[
              const Text('Мнение игрока, не подтверждённая истина',
                  style: TextStyle(fontSize: 11)),
              const SizedBox(height: 6),
              Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    for (var i = 0;
                        i < _visible && i < message.cardIds.length;
                        i++)
                      if (i != 0 ||
                          !List.generate(
                              _visible.clamp(0, message.cardIds.length),
                              (j) => j).any((j) => theoryLinked(message, j)))
                        Row(mainAxisSize: MainAxisSize.min, children: [
                          if (theoryLinked(message, i)) ...[
                            CardImage(cardId: message.cardIds.first, size: 52),
                            Icon(
                                theoryNegative(message.noteFor(i) ?? '')
                                    ? Icons.block
                                    : Icons.arrow_forward,
                                size: 18),
                          ],
                          Column(mainAxisSize: MainAxisSize.min, children: [
                            GestureDetector(
                                onTap: () =>
                                    showCardZoom(context, message.cardIds[i]),
                                child: CardImage(
                                    cardId: message.cardIds[i], size: 52)),
                            SizedBox(
                                width: 68,
                                child: Text(
                                    theoryLabel(message.noteFor(i) ?? ''),
                                    textAlign: TextAlign.center,
                                    maxLines: 2,
                                    style: const TextStyle(fontSize: 10))),
                          ]),
                        ]),
                  ]),
              if (message.isVoice)
                Consumer(
                    builder: (context, ref, _) => TextButton.icon(
                        icon: const Icon(Icons.play_arrow),
                        label: const Text('Послушать пояснение'),
                        onPressed: () => runAction(
                            context,
                            () => ref
                                .read(voiceProvider)
                                .play(message.mediaId!)))),
            ],
          ],
        ));
  }
}

class TableTheorySheet extends ConsumerStatefulWidget {
  const TableTheorySheet({super.key, required this.screen, this.initialSource});
  final String? initialSource;
  final GameScreenState screen;
  @override
  ConsumerState<TableTheorySheet> createState() => _TableTheorySheetState();
}

class _TableTheorySheetState extends ConsumerState<TableTheorySheet> {
  String? _source, _media;
  final _targets = <String, bool>{};
  final _text = TextEditingController();
  String _mode = 'source';
  bool _busy = false, _recording = false;
  Timer? _limit;
  late final Voice _voice;
  late final String _phase, _channel;
  late final int _round;

  @override
  void initState() {
    super.initState();
    _voice = ref.read(voiceProvider);
    _source = widget.initialSource;
    if (_source != null) _mode = 'support';
    final v = widget.screen.view!;
    _phase = v.phase;
    _round = v.round;
    _channel = v.phase == 'Night' &&
            const {'Killer', 'Accomplice'}.contains(v.me?.role)
        ? 'killer_team'
        : 'public';
  }

  @override
  void dispose() {
    _limit?.cancel();
    if (_recording) unawaited(_voice.cancel());
    _text.dispose();
    super.dispose();
  }

  void _pick(String id, {bool sourceOnly = false}) {
    setState(() {
      if (_mode == 'source' || sourceOnly) {
        _source = _source == id ? null : id;
        _targets.remove(id);
        _mode = 'support';
      } else if (id != _source) {
        if (_targets[id] == (_mode == 'support')) {
          _targets.remove(id);
        } else if (_targets.containsKey(id) || _targets.length < 4) {
          _targets[id] = _mode == 'support';
        }
      }
    });
  }

  Future<void> _record() async {
    if (!_recording) {
      final ok = await runAction(context, _voice.start);
      if (!mounted) {
        if (ok == true) await _voice.cancel();
        return;
      }
      if (ok != true) return;
      setState(() {
        _recording = true;
        _media = null;
      });
      _limit = Timer(Voice.maxDuration, _record);
    } else {
      _limit?.cancel();
      setState(() {
        _recording = false;
        _busy = true;
      });
      final media = await runAction(context, () async {
        final take = await _voice.stop();
        return take == null
            ? null
            : ref.read(apiProvider).uploadVoice(take.file, take.durationMs);
      });
      if (mounted) {
        setState(() {
          _busy = false;
          _media = media;
        });
      }
    }
  }

  Future<void> _send() async {
    final v = widget.screen.view!;
    if (v.phase != _phase || v.round != _round) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('Этап изменился. Откройте редактор версии заново.')));
      return;
    }
    setState(() => _busy = true);
    final sent = await runAction(
        context,
        () => ref.read(apiProvider).sendChat(
            v.gameId,
            _text.text.trim().isEmpty
                ? 'Моя версия: показываю связи на столе. Это предположение.'
                : _text.text.trim(),
            channel: _channel,
            cards: [if (_source != null) _source!, ..._targets.keys],
            cardNotes: [
              if (_source != null) 'улика',
              for (final support in _targets.values)
                (support ? 'думаю, эта' : 'исключаю') +
                    (_source != null ? ':0' : '')
            ],
            mediaId: _media));
    if (!mounted) return;
    setState(() => _busy = false);
    if (sent != null) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    final v = widget.screen.view!;
    final sources = <String>{
      for (final h in v.hints) ...h.cards,
      for (final l in v.me?.letters ?? <MyLetter>[]) l.cardId,
      for (final m in widget.screen.chat)
        if (m.channel == 'public') ...m.cardIds
    };
    Widget card(String id, {bool sourceOnly = false}) => Semantics(
        label: 'Карта $id',
        selected: _source == id || _targets.containsKey(id),
        button: true,
        child: InkWell(
            key: ValueKey('theory-$id'),
            onTap: _busy ? null : () => _pick(id, sourceOnly: sourceOnly),
            child: Container(
                padding: const EdgeInsets.all(3),
                decoration: BoxDecoration(
                    border: Border.all(
                        width: 2,
                        color: _source == id
                            ? Colors.amber
                            : _targets[id] == true
                                ? Colors.green
                                : _targets[id] == false
                                    ? Colors.red
                                    : Colors.transparent)),
                child: CardImage(cardId: id, size: 56))));
    return SizedBox(
        height: MediaQuery.sizeOf(context).height * .88,
        child: Padding(
          padding: EdgeInsets.fromLTRB(
              12, 12, 12, MediaQuery.viewInsetsOf(context).bottom + 12),
          child: SingleChildScrollView(
              child: Column(children: [
            const Text('Моя версия на столе', style: TextStyle(fontSize: 20)),
            const Text(
                'Выберите улику, затем до четырёх карт: поддержать или исключить. Исчезнувшее письмо — тоже ваше предположение.'),
            Wrap(spacing: 6, children: [
              for (final mode in const {
                'source': 'Улика',
                'support': 'Поддерживает',
                'exclude': 'Исключает'
              }.entries)
                ChoiceChip(
                    label: Text(mode.value),
                    selected: _mode == mode.key,
                    onSelected:
                        _busy ? null : (_) => setState(() => _mode = mode.key)),
            ]),
            SizedBox(
                height: (MediaQuery.sizeOf(context).height * .38)
                    .clamp(140.0, 400.0),
                child: ListView(children: [
                  for (final row in v.board) ...[
                    Text(T.category(row.category)),
                    Wrap(
                        spacing: 3,
                        runSpacing: 3,
                        children: row.cards.map(card).toList()),
                  ],
                  if (sources.isNotEmpty) ...[
                    const Text('Открытые улики, мои письма и показанные карты'),
                    Wrap(
                        spacing: 3,
                        runSpacing: 3,
                        children: sources
                            .where((id) =>
                                !v.board.any((r) => r.cards.contains(id)))
                            .map((id) => card(id, sourceOnly: true))
                            .toList()),
                  ],
                ])),
            TextField(
                controller: _text,
                maxLength: 1000,
                maxLines: 2,
                decoration:
                    const InputDecoration(labelText: 'Почему я так думаю')),
            Wrap(spacing: 8, children: [
              TextButton.icon(
                  onPressed: _busy ? null : _record,
                  icon: Icon(_recording ? Icons.stop : Icons.mic),
                  label: Text(_recording
                      ? 'Закончить запись'
                      : _media != null
                          ? 'Перезаписать пояснение'
                          : 'Записать пояснение')),
              FilledButton(
                  onPressed:
                      _busy || _recording || _targets.isEmpty ? null : _send,
                  child: Text(_channel == 'killer_team'
                      ? 'Показать команде'
                      : 'Показать всем')),
            ]),
          ])),
        ));
  }
}
