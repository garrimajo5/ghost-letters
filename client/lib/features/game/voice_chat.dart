import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:livekit_client/livekit_client.dart';
import 'package:record/record.dart';

import '../../core/api.dart';
import '../../core/platform/voice_files.dart';

/// Живой голосовой чат партии (LiveKit). Слушать можно всегда, говорить — только когда сервер дал право
/// (рация у меня, мне дали слово, свободное обсуждение, голосование, итоги). Пока говорю — сказанное
/// кусками до 25 с уходит на распознавание: текст попадает в чат, и боты понимают, что я сказал.
class VoiceChat extends ChangeNotifier {
  VoiceChat(this._api, this.gameId);

  final Api _api;
  final String gameId;

  Room? _room;
  EventsListener<RoomEvent>? _listener;
  bool connecting = false;
  bool get joined => _room != null;

  /// Сервер разрешает мне говорить сейчас.
  bool canSpeak = false;

  /// Я хочу, чтобы меня слышали, когда можно (по умолчанию да — как рация в руке).
  bool micWanted = true;

  /// Кто говорит прямо сейчас (id игроков).
  Set<String> speaking = {};

  String? error;

  bool get live => joined && canSpeak && micWanted;

  AudioRecorder? _recorder;
  Timer? _chunk;
  bool _recording = false;
  bool _disposed = false;

  Future<void> join() async {
    if (joined || connecting) return;
    connecting = true;
    error = null;
    _notify();
    try {
      final access = await _api.voiceAccess(gameId);
      final room = Room(roomOptions: const RoomOptions(adaptiveStream: true, dynacast: true));
      final listener = room.createListener();
      listener
        ..on<ParticipantPermissionsUpdatedEvent>((e) {
          if (e.participant is LocalParticipant) {
            canSpeak = e.permissions.canPublish;
            unawaited(_applyMic());
          }
        })
        ..on<ActiveSpeakersChangedEvent>((e) {
          speaking = {for (final p in e.speakers) p.identity};
          _notify();
        })
        ..on<RoomDisconnectedEvent>((_) {
          unawaited(_reset());
        });
      await room.connect(access.url, access.token);
      _room = room;
      _listener = listener;
      canSpeak = access.canSpeak;
      await _applyMic();
    } catch (e) {
      error = 'Голосовой чат недоступен';
      await _reset();
    } finally {
      connecting = false;
      _notify();
    }
  }

  Future<void> leave() async {
    await _stopRecording(upload: true);
    await _room?.disconnect();
    await _reset();
  }

  Future<void> toggleMic() async {
    micWanted = !micWanted;
    await _applyMic();
  }

  Future<void> _applyMic() async {
    final local = _room?.localParticipant;
    if (local == null) return;
    try {
      await local.setMicrophoneEnabled(live);
    } catch (_) {
      error = 'Нет доступа к микрофону';
      micWanted = false;
    }
    if (live) {
      await _startRecording();
    } else {
      await _stopRecording(upload: true);
    }
    _notify();
  }

  // ---------- Расшифровка для ботов ----------

  Future<void> _startRecording() async {
    if (_recording) return;
    final recorder = _recorder ??= AudioRecorder();
    try {
      if (!await recorder.hasPermission()) return;
      await recorder.start(const RecordConfig(encoder: AudioEncoder.wav, sampleRate: 16000, numChannels: 1),
          path: await recordingPath());
      _recording = true;
      // Распознавание принимает до 30 с — режем на куски по 25.
      _chunk = Timer(const Duration(seconds: 25), () async {
        await _stopRecording(upload: true);
        if (live) await _startRecording();
      });
    } catch (_) {
      // На некоторых телефонах микрофон нельзя делить с голосовым чатом — тогда без расшифровки.
      _recording = false;
    }
  }

  Future<void> _stopRecording({required bool upload}) async {
    _chunk?.cancel();
    _chunk = null;
    if (!_recording) return;
    _recording = false;
    try {
      final path = await _recorder?.stop();
      if (!upload || path == null || path.isEmpty) return;
      final file = await recordingUpload(path, 'audio/wav');
      unawaited(_api.voiceTranscript(gameId, file).catchError((Object _) {}));
    } catch (_) {}
  }

  Future<void> _reset() async {
    _chunk?.cancel();
    await _listener?.dispose();
    _listener = null;
    final room = _room;
    _room = null;
    await room?.dispose();
    canSpeak = false;
    speaking = {};
    _notify();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    unawaited(leave().whenComplete(() => _recorder?.dispose()));
    super.dispose();
  }
}
