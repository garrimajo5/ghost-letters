import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api.dart';
import '../../core/card_catalog.dart';
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
                badge: Icons.close,
                color: AppColors.red,
                label: 'Проверяли — подсказки не было',
                value: mark.crosses,
                onChanged: (v) => _set(mark.copyWith(crosses: v)),
              ),
              SourceChips(
                key: const Key('cross-by'),
                screen: widget.screen,
                label: 'Кто проверял:',
                selected: mark.crossBy,
                color: AppColors.red,
                onToggle: (id) => _set(mark.toggleSource(id, cross: true)),
              ),
              const Divider(height: 1, color: AppColors.surface2),
              _Counter(
                key: const Key('mark-checks'),
                badge: Icons.check,
                color: AppColors.green,
                label: 'Подсказки указывают сюда',
                value: mark.checks,
                onChanged: (v) => _set(mark.copyWith(checks: v)),
              ),
              SourceChips(
                key: const Key('check-by'),
                screen: widget.screen,
                label: 'Чьи подсказки:',
                selected: mark.checkBy,
                color: AppColors.green,
                onToggle: (id) => _set(mark.toggleSource(id, cross: false)),
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
                      child: Row(mainAxisSize: MainAxisSize.min, children: [
                        Icon(
                          mark.believed ? Icons.radio_button_checked : Icons.radio_button_unchecked,
                          size: 18,
                          color: mark.believed ? Colors.white : AppColors.greenSoft,
                        ),
                        const SizedBox(width: 8),
                        Text(
                          'Считаю истинной',
                          style: TextStyle(
                            fontSize: 15,
                            fontWeight: FontWeight.w600,
                            color: mark.believed ? Colors.white : AppColors.greenSoft,
                          ),
                        ),
                      ]),
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

  final IconData badge;
  final Color color;
  final String label;
  final int value;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    Widget button(IconData icon, VoidCallback? onTap, String tooltip) => SizedBox(
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
            child: Tooltip(message: tooltip, child: Icon(icon, size: 18)),
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
          child: Icon(badge, size: 16, color: Colors.white),
        ),
        const SizedBox(width: 10),
        Expanded(child: Text(label, style: const TextStyle(fontSize: 14, height: 1.3))),
        button(Icons.remove, value > 0 ? () => onChanged(value - 1) : null, 'Меньше'),
        SizedBox(width: 30, child: Text('$value', textAlign: TextAlign.center, style: heading(20, spacing: 0))),
        button(Icons.add, () => onChanged(value + 1), 'Больше'),
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
  int _suspicion = 0;
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
        _suspicion = ((mine['suspicion'] as num?) ?? 0).toInt();
        _body.text = mine['body'] as String? ?? '';
      }
    });
  }

  @override
  void dispose() {
    _body.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    await runAction(
      context,
      () => ref.read(apiProvider).saveNote(widget.screen.widget.gameId, widget.userId, _suspicion, _body.text),
    );
    widget.screen.setSuspicion(widget.userId, _suspicion);
    if (mounted) Navigator.pop(context);
  }

  /// Что я отметил о нём на картах: какое письмо он назвал своим, что проверял (✕) и на что указывали его подсказки (✓).
  List<Widget> _intel(TextStyle label) {
    final marks = widget.screen.marks;
    final id = widget.userId;
    final claimed = [for (final e in marks.entries) if (e.value.claimedBy == id) e.key];
    final crossed = [for (final e in marks.entries) if (e.value.crossBy.contains(id)) e.key];
    final checked = [for (final e in marks.entries) if (e.value.checkBy.contains(id)) e.key];
    if (claimed.isEmpty && crossed.isEmpty && checked.isEmpty) return const [];
    Widget row(String title, List<String> cards, {Color? color}) => Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Row(children: [
            SizedBox(width: 120, child: Text(title, style: TextStyle(fontSize: 13, color: color ?? AppColors.text))),
            Expanded(
              child: Wrap(spacing: 6, runSpacing: 6, children: [
                for (final c in cards)
                  GestureDetector(onTap: () => showCardZoom(context, c), child: CardImage(cardId: c, size: 40, radius: 6)),
              ]),
            ),
          ]),
        );
    return [
      const SizedBox(height: 14),
      Text('О ПИСЬМАХ — ПО МОИМ ПОМЕТКАМ', style: label),
      const SizedBox(height: 8),
      if (claimed.isNotEmpty) row('Говорит, что отправил', claimed, color: AppColors.ice),
      if (crossed.isNotEmpty) row('Проверял — не то ✕', crossed, color: AppColors.redSoft),
      if (checked.isNotEmpty) row('Его подсказки указывают ✓', checked, color: AppColors.greenSoft),
    ];
  }

  /// Что известно о игроке без заметок: роль (если видна), письма, голоса в финале.
  List<String> _facts() {
    final screen = widget.screen;
    final v = screen.view;
    if (v == null) return const [];
    final info = v.player(widget.userId);
    final facts = <String>[
      if (info?.knownRole != null) 'Роль: ${T.role(info!.knownRole)}',
      if (info != null && info.hasActed && v.phase == 'Mailbox') 'Уже отправил письмо в этом раунде',
      if (v.currentSpeaker == widget.userId) 'Сейчас говорит по рации',
      if (v.raisedHands.contains(widget.userId)) 'Поднял руку',
    ];
    final finale = v.finale;
    if (finale != null) {
      for (final r in finale.votes.where((r) => r.voter == widget.userId)) {
        if (r.column != null && finale.outcomes.any((o) => o.stage == r.stage && o.kind == 'Row')) {
          final o = finale.outcomes.firstWhere((o) => o.stage == r.stage);
          facts.add('Голосовал: ${T.category(v.board[o.row].category)} — карта ${r.column! + 1}');
        } else if (r.suspect != null) {
          facts.add('Голосовал за арест: ${screen.nick(r.suspect)}');
        }
      }
    }
    if (facts.isEmpty) facts.add('Пока ничего примечательного');
    return facts;
  }

  @override
  Widget build(BuildContext context) {
    final screen = widget.screen;
    final r = screen.rosterOf(widget.userId);
    final said = screen.chat.where((m) => m.authorId == widget.userId && (m.text ?? '').isNotEmpty).toList().reversed.take(3).toList();
    const label = TextStyle(fontSize: 12, color: AppColors.muted, letterSpacing: 1);
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Row(children: [
            Avatar(nickname: r?.nickname ?? '?', color: r?.avatarColor ?? '#3D6A99', size: 52),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text((r?.nickname ?? '?').toUpperCase(), style: heading(24, spacing: 1)),
                const Text('Заметки видите только вы', style: TextStyle(fontSize: 12, color: AppColors.muted)),
              ]),
            ),
          ]),
          if (!_loaded) const Padding(padding: EdgeInsets.only(top: 8), child: LinearProgressIndicator()),
          const SizedBox(height: 16),
          const Text('ПОДОЗРЕВАЮ, ЧТО ОН', style: label),
          const SizedBox(height: 8),
          Wrap(spacing: 6, runSpacing: 6, children: [
            for (final e in _labels.entries)
              ChoiceChip(
                key: Key('suspicion-${e.key}'),
                label: Text(e.value),
                selected: _suspicion == e.key,
                showCheckmark: false,
                selectedColor: e.key > 0 ? AppColors.red : (e.key < 0 ? AppColors.green : AppColors.border),
                labelStyle: TextStyle(color: _suspicion == e.key ? Colors.white : AppColors.text, fontFamily: AppFonts.body),
                onSelected: (_) => setState(() => _suspicion = e.key),
              ),
          ]),
          const SizedBox(height: 18),
          const Text('ФАКТЫ ПАРТИИ · АВТОМАТИЧЕСКИ', style: label),
          const SizedBox(height: 6),
          for (final f in _facts())
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Text('•  ', style: TextStyle(color: AppColors.ice)),
                Expanded(child: Text(f, style: const TextStyle(fontSize: 13))),
              ]),
            ),
          ..._intel(label),
          if (said.isNotEmpty) ...[
            const SizedBox(height: 14),
            const Text('ИЗ ЧАТА', style: label),
            const SizedBox(height: 6),
            for (final m in said)
              Container(
                margin: const EdgeInsets.only(bottom: 6),
                padding: const EdgeInsets.fromLTRB(12, 6, 12, 6),
                decoration: BoxDecoration(
                  color: AppColors.panel,
                  borderRadius: const BorderRadius.horizontal(right: Radius.circular(10)),
                  border: Border(left: BorderSide(color: colorFromHex(r?.avatarColor ?? '#3D6A99'), width: 3)),
                ),
                child: Row(children: [
                  Expanded(child: Text('«${m.text}»', style: const TextStyle(fontSize: 13, height: 1.4))),
                  for (final c in m.cardIds.take(2))
                    Padding(padding: const EdgeInsets.only(left: 6), child: CardImage(cardId: c, size: 36, radius: 6)),
                ]),
              ),
          ],
          const SizedBox(height: 14),
          const Text('МОЯ ЗАМЕТКА', style: label),
          const SizedBox(height: 6),
          TextField(
            controller: _body,
            minLines: 3,
            maxLines: 6,
            maxLength: 2000,
            decoration: const InputDecoration(hintText: 'Что говорил, что отправлял, в чём путался…'),
          ),
          FilledButton(
            key: const Key('save-note'),
            style: FilledButton.styleFrom(
              backgroundColor: AppColors.ice,
              foregroundColor: AppColors.bg,
              textStyle: const TextStyle(fontFamily: AppFonts.body, fontSize: 15, fontWeight: FontWeight.w600),
            ),
            onPressed: _save,
            child: const Text('Готово'),
          ),
        ]),
      ),
    );
  }
}

/// Чат партии: общий канал и, для команды Убийцы, свой канал. Текст, голосовые и упоминания карт.
class ChatSheet extends ConsumerStatefulWidget {
  const ChatSheet({super.key, required this.screen, this.embedded = false});

  final GameScreenState screen;

  /// Чат встроен сбоку игрового стола (широкий экран), а не открыт шторкой.
  final bool embedded;

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
      final mediaId = await api.uploadVoice(take.file, take.durationMs);
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
      padding: EdgeInsets.only(bottom: widget.embedded ? 0 : MediaQuery.of(context).viewInsets.bottom),
      child: SizedBox(
        height: widget.embedded ? null : MediaQuery.of(context).size.height * 0.75,
        child: Column(children: [
          if (widget.embedded)
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 4),
              child: Align(alignment: Alignment.centerLeft, child: Text('ЧАТ', style: sectionLabel(size: 13))),
            ),
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
                        constraints: BoxConstraints(maxWidth: widget.embedded ? 280 : MediaQuery.of(context).size.width * 0.72),
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
                              child: Wrap(spacing: 6, runSpacing: 6, children: [
                                for (var i = 0; i < m.cardIds.length; i++)
                                  GestureDetector(
                                    key: Key('chat-card-${m.id}-$i'),
                                    onTap: () => showCardZoom(context, m.cardIds[i], caption: m.noteFor(i)),
                                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                                      CardImage(cardId: m.cardIds[i], size: 44, radius: 8),
                                      if (m.noteFor(i) case final note?)
                                        SizedBox(
                                          width: 56,
                                          child: Text(
                                            note,
                                            textAlign: TextAlign.center,
                                            maxLines: 2,
                                            style: TextStyle(
                                              fontSize: 10,
                                              height: 1.15,
                                              color: note == 'кидал эту' ? AppColors.amber : AppColors.muted,
                                            ),
                                          ),
                                        ),
                                    ]),
                                  ),
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

/// Игроки-источники пометки: нажатие добавляет или убирает игрока (и подправляет счётчик).
class SourceChips extends StatelessWidget {
  const SourceChips({
    super.key,
    required this.screen,
    required this.label,
    required this.selected,
    required this.onToggle,
    this.color = AppColors.amber,
    this.single = false,
  });

  final GameScreenState screen;
  final String label;
  final List<String> selected;
  final ValueChanged<String> onToggle;
  final Color color;
  final bool single;

  @override
  Widget build(BuildContext context) {
    final v = screen.view;
    if (v == null) return const SizedBox.shrink();
    final players = [...v.players]..sort((a, b) => a.seat.compareTo(b.seat));
    return Padding(
      padding: const EdgeInsets.fromLTRB(14, 0, 14, 10),
      // Все игроки видны сразу: чипы переносятся на следующую строку, а не уезжают за край.
      child: Wrap(spacing: 6, runSpacing: 6, crossAxisAlignment: WrapCrossAlignment.center, children: [
        Padding(
          padding: const EdgeInsets.only(right: 2),
          child: Text(label, style: const TextStyle(fontSize: 12, color: AppColors.muted)),
        ),
        for (final p in players.where((p) => !p.isGhost))
                GestureDetector(
                    key: Key('src-${p.id}'),
                    onTap: () => onToggle(p.id),
                    child: AnimatedContainer(
                      duration: const Duration(milliseconds: 150),
                      padding: const EdgeInsets.fromLTRB(3, 3, 10, 3),
                      decoration: BoxDecoration(
                        color: selected.contains(p.id) ? color : AppColors.surface2,
                        borderRadius: BorderRadius.circular(99),
                      ),
                      child: Row(mainAxisSize: MainAxisSize.min, children: [
                        Avatar(nickname: screen.nick(p.id), color: screen.colorOf(p.id), size: 22),
                        const SizedBox(width: 6),
                        Text(
                          p.id == v.me?.id ? 'Я' : screen.nick(p.id),
                          style: TextStyle(fontSize: 12, color: selected.contains(p.id) ? Colors.white : AppColors.text),
                        ),
                      ]),
                    ),
                  ),
      ]),
    );
  }
}

/// Подсказка: крупно и «кто сказал, что это его письмо» — сведения попадут в заметку об игроке.
class HintSheet extends StatefulWidget {
  const HintSheet({super.key, required this.screen, required this.cardId, required this.round});

  final GameScreenState screen;
  final String cardId;
  final int round;

  static Future<void> show(BuildContext context, GameScreenState screen, String cardId, int round) =>
      showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        builder: (_) => HintSheet(screen: screen, cardId: cardId, round: round),
      );

  @override
  State<HintSheet> createState() => _HintSheetState();
}

class _HintSheetState extends State<HintSheet> {
  late CardMark mark = widget.screen.marks[widget.cardId] ?? const CardMark();

  void _set(CardMark m) {
    setState(() => mark = m);
    widget.screen.saveMark(widget.cardId, m);
  }

  @override
  Widget build(BuildContext context) {
    final mine = widget.screen.view?.me?.letters.any((l) => l.cardId == widget.cardId) == true;
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          GestureDetector(
            onTap: () => showCardZoom(context, widget.cardId),
            child: CardImage(cardId: widget.cardId, size: 200, radius: 20),
          ),
          const SizedBox(height: 10),
          Text(
            widget.round == 0 ? 'Первая зацепка Призрака' : 'Подсказка раунда ${widget.round}${mine ? ' · это ваше письмо' : ''}',
            style: const TextStyle(fontSize: 13, color: AppColors.muted),
          ),
          if (widget.round > 0) ...[
            const SizedBox(height: 14),
            Container(
              padding: const EdgeInsets.only(top: 12),
              decoration: BoxDecoration(color: AppColors.surface, borderRadius: BorderRadius.circular(16)),
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                const Padding(
                  padding: EdgeInsets.fromLTRB(14, 0, 14, 8),
                  child: Text('Кто говорит, что это его письмо?', style: TextStyle(fontSize: 14)),
                ),
                SourceChips(
                  key: const Key('claimed-by'),
                  screen: widget.screen,
                  label: '',
                  selected: [if (mark.claimedBy != null) mark.claimedBy!],
                  onToggle: (id) => _set(mark.claimedBy == id ? mark.copyWith(clearClaimedBy: true) : mark.copyWith(claimedBy: id)),
                ),
              ]),
            ),
          ],
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

/// Моё письмо: что я говорю другим, что отправил. Команде Убийцы бывает выгодно соврать — и не забыть, что соврал.
class LetterSheet extends StatefulWidget {
  const LetterSheet({super.key, required this.screen, required this.letter});

  final GameScreenState screen;
  final MyLetter letter;

  static Future<void> show(BuildContext context, GameScreenState screen, MyLetter letter) => showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        builder: (_) => LetterSheet(screen: screen, letter: letter),
      );

  @override
  State<LetterSheet> createState() => _LetterSheetState();
}

class _LetterSheetState extends State<LetterSheet> {
  late CardMark mark = widget.screen.marks[widget.letter.cardId] ?? const CardMark();

  void _set(CardMark m) {
    setState(() => mark = m);
    widget.screen.saveMark(widget.letter.cardId, m);
  }

  Future<void> _pickAny() async {
    final l = widget.letter;
    final sets = widget.screen.lobby?.settings.cardSets;
    final picked = await AnyCardPicker.show(context, sets: sets, exclude: l.cardId, selected: mark.claim);
    if (picked != null && mounted) _set(picked == l.cardId ? mark.copyWith(clearClaim: true) : mark.copyWith(claim: picked));
  }

  Widget _option(String c, String label) => _ClaimOption(
        key: Key('claim-$c'),
        selected: mark.claim == c,
        label: label,
        child: CardImage(cardId: c, size: 56, radius: 8),
        onTap: () => _set(mark.copyWith(claim: c)),
      );

  @override
  Widget build(BuildContext context) {
    final v = widget.screen.view!;
    final l = widget.letter;
    final me = v.me;
    // В приоритете — карты, которые у меня были: на руке и сброшенные.
    final hand = [for (final c in me?.hand ?? const <String>[]) if (c != l.cardId) c];
    final discarded = [
      for (final c in (me?.discarded ?? const <String>[]).reversed.toSet()) if (c != l.cardId && !hand.contains(c)) c,
    ];
    final claim = mark.claim;
    final other = claim != null && !hand.contains(claim) && !discarded.contains(claim) ? claim : null;
    const hint = TextStyle(fontSize: 12, color: AppColors.muted);
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Center(child: CardImage(cardId: l.cardId, size: 160, radius: 18)),
          const SizedBox(height: 8),
          Text(
            'Моё письмо, раунд ${l.round} · ${l.revealed == null ? 'ждём Призрака' : (l.revealed! ? 'открылось' : 'исчезло')}',
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 13, color: AppColors.muted),
          ),
          const SizedBox(height: 16),
          Text('ЧТО Я ГОВОРЮ ДРУГИМ', style: sectionLabel()),
          const SizedBox(height: 4),
          const Text('Видите только вы. Если называете другую карту — запомните, какую.', style: hint),
          const SizedBox(height: 10),
          Wrap(spacing: 8, runSpacing: 8, children: [
            _ClaimOption(
              key: const Key('claim-truth'),
              selected: claim == null,
              label: 'Правду',
              child: CardImage(cardId: l.cardId, size: 56, radius: 8),
              onTap: () => _set(mark.copyWith(clearClaim: true)),
            ),
            if (other != null) _option(other, 'Выбрана'),
          ]),
          if (hand.isNotEmpty) ...[
            const SizedBox(height: 14),
            const Text('С РУКИ', key: Key('claim-hand'), style: hint),
            const SizedBox(height: 6),
            Wrap(spacing: 8, runSpacing: 8, children: [for (final c in hand) _option(c, 'Эту')]),
          ],
          if (discarded.isNotEmpty) ...[
            const SizedBox(height: 14),
            const Text('СБРОШЕННЫЕ', key: Key('claim-discarded'), style: hint),
            const SizedBox(height: 6),
            Wrap(spacing: 8, runSpacing: 8, children: [for (final c in discarded) _option(c, 'Эту')]),
          ],
          const SizedBox(height: 14),
          OutlinedButton.icon(
            key: const Key('claim-any'),
            icon: const Icon(Icons.grid_view, size: 18),
            label: const Text('Любая карта из набора…'),
            onPressed: _pickAny,
          ),
          const SizedBox(height: 12),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: AppColors.ice,
              foregroundColor: AppColors.bg,
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

/// Выбор любой карты из наборов партии: вкладки по наборам и сетка карт.
class AnyCardPicker extends ConsumerStatefulWidget {
  const AnyCardPicker({super.key, this.sets, this.exclude, this.selected});

  /// Коды наборов партии; null — все наборы.
  final List<String>? sets;
  final String? exclude;
  final String? selected;

  static Future<String?> show(BuildContext context, {List<String>? sets, String? exclude, String? selected}) =>
      showModalBottomSheet<String>(
        context: context,
        isScrollControlled: true,
        builder: (_) => AnyCardPicker(sets: sets, exclude: exclude, selected: selected),
      );

  @override
  ConsumerState<AnyCardPicker> createState() => _AnyCardPickerState();
}

class _AnyCardPickerState extends ConsumerState<AnyCardPicker> {
  String? _set; // null — все наборы партии

  @override
  Widget build(BuildContext context) {
    final catalog = ref.watch(cardCatalogProvider);
    final height = MediaQuery.sizeOf(context).height * 0.8;
    return SafeArea(
      child: SizedBox(
        height: height,
        child: catalog.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => const Center(child: Text('Не удалось загрузить карты', style: TextStyle(color: AppColors.muted))),
          data: (all) {
            final sets = [for (final s in all) if (widget.sets == null || widget.sets!.contains(s.code)) s];
            final shown = [
              for (final s in sets)
                if (_set == null || s.code == _set)
                  for (final c in s.cards) if (c != widget.exclude) c,
            ];
            return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 0, 20, 8),
                child: Text('ЛЮБАЯ КАРТА ИЗ НАБОРА', style: sectionLabel()),
              ),
              if (sets.length > 1)
                // Все наборы видны сразу (переносом строк), без горизонтальной прокрутки.
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Wrap(spacing: 8, runSpacing: 4, children: [
                    for (final s in [null, ...sets])
                      ChoiceChip(
                        key: Key('any-set-${s?.code ?? 'all'}'),
                        label: Text(s?.title ?? 'Все'),
                        showCheckmark: false,
                        selected: _set == s?.code,
                        onSelected: (_) => setState(() => _set = s?.code),
                      ),
                  ]),
                ),
              Expanded(
                child: GridView.builder(
                  key: const Key('any-grid'),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                  gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(maxCrossAxisExtent: 96, mainAxisSpacing: 8, crossAxisSpacing: 8),
                  itemCount: shown.length,
                  itemBuilder: (context, i) {
                    final c = shown[i];
                    final selected = c == widget.selected;
                    return GestureDetector(
                      key: Key('any-$c'),
                      onTap: () => Navigator.pop(context, c),
                      child: Container(
                        foregroundDecoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: selected ? AppColors.amber : AppColors.border, width: selected ? 3 : 1),
                        ),
                        child: LayoutBuilder(builder: (context, box) => CardImage(cardId: c, size: box.maxWidth, radius: 8)),
                      ),
                    );
                  },
                ),
              ),
            ]);
          },
        ),
      ),
    );
  }
}

class _ClaimOption extends StatelessWidget {
  const _ClaimOption({super.key, required this.selected, required this.label, required this.child, required this.onTap});

  final bool selected;
  final String label;
  final Widget child;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => GestureDetector(
        onTap: onTap,
        child: Column(children: [
          Container(
            foregroundDecoration: BoxDecoration(
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: selected ? AppColors.amber : AppColors.border, width: selected ? 3 : 1),
            ),
            child: child,
          ),
          const SizedBox(height: 2),
          Text(label, style: TextStyle(fontSize: 11, color: selected ? AppColors.amber : AppColors.dim)),
        ]),
      );
}
