import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:record/record.dart';

import 'api.dart';
import 'platform/voice_files.dart';

/// Голосовые: запись с микрофона (AAC, до 60 секунд) и прослушивание.
class Voice {
  Voice(this._api);

  static const maxDuration = Duration(seconds: 60);

  final Api _api;
  // Плагины создаются при первом использовании — открыть чат можно и без звука.
  AudioRecorder? _recorderInstance;
  AudioPlayer? _playerInstance;
  AudioRecorder get _recorder => _recorderInstance ??= AudioRecorder();
  AudioPlayer get _player => _playerInstance ??= AudioPlayer();
  final Stopwatch _clock = Stopwatch();
  String? _path;
  String _mime = 'audio/mp4';

  final _playing = StreamController<String?>.broadcast();

  /// id голосового, которое играет сейчас (null — тишина).
  Stream<String?> get playing => _playing.stream;

  Duration get elapsed => _clock.elapsed;

  Future<bool> start() async {
    if (!await _recorder.hasPermission()) return false;
    final path = await recordingPath();
    // AAC (m4a) понимают все; браузеры без него (Chrome, Firefox) пишут Opus в WebM.
    final aac = await _recorder.isEncoderSupported(AudioEncoder.aacLc);
    _mime = aac ? 'audio/mp4' : 'audio/webm';
    await _recorder.start(
      RecordConfig(encoder: aac ? AudioEncoder.aacLc : AudioEncoder.opus, bitRate: 64000, sampleRate: aac ? 22050 : 48000),
      path: path,
    );
    _path = path;
    _clock
      ..reset()
      ..start();
    return true;
  }

  /// Остановить и вернуть запись с длительностью; null — запись слишком короткая.
  Future<({MultipartFile file, int durationMs})?> stop() async {
    _clock.stop();
    final path = await _recorder.stop() ?? _path;
    final ms = _clock.elapsedMilliseconds.clamp(0, maxDuration.inMilliseconds);
    if (path == null || path.isEmpty || ms < 500) return null;
    return (file: await recordingUpload(path, _mime), durationMs: ms);
  }

  Future<void> cancel() async {
    _clock.stop();
    await _recorder.cancel();
  }

  Future<void> play(String mediaId) async {
    final source = await playableVoice(mediaId, () => _api.downloadVoice(mediaId));

    await _player.stop();
    _playing.add(mediaId);
    await _player.play(source);
    unawaited(_player.onPlayerComplete.first.then((_) => _playing.add(null)));
  }

  Future<void> stopPlaying() async {
    await _player.stop();
    _playing.add(null);
  }

  Future<void> dispose() async {
    // Плагины создаются лениво: если чатом со звуком не пользовались — освобождать нечего
    // (иначе dispose сам создавал бы рекордер и дёргал платформу уже при закрытии).
    await _recorderInstance?.dispose();
    await _playerInstance?.dispose();
    await _playing.close();
  }
}

final voiceProvider = Provider<Voice>((ref) {
  final voice = Voice(ref.read(apiProvider));
  ref.onDispose(voice.dispose);
  return voice;
});
