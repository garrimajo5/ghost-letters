import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:record/record.dart';

import 'api.dart';
import 'sound.dart';
import 'platform/voice_files.dart';

/// Голосовые: запись с микрофона (AAC, до 60 секунд) и прослушивание.
class Voice {
  Voice(this._api, {Sound? sound}) : _sound = sound;

  final Sound? _sound;
  StreamSubscription<void>? _completion;
  int _playGeneration = 0;

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
    _sound?.recording(true);
    try {
      await _recorder.start(
        RecordConfig(
            encoder: aac ? AudioEncoder.aacLc : AudioEncoder.opus,
            bitRate: 64000,
            sampleRate: aac ? 22050 : 48000),
        path: path,
      );
    } catch (_) {
      _sound?.recording(false);
      rethrow;
    }
    _path = path;
    _clock
      ..reset()
      ..start();
    return true;
  }

  /// Остановить и вернуть запись с длительностью; null — запись слишком короткая.
  Future<({MultipartFile file, int durationMs})?> stop() async {
    _clock.stop();
    String? path;
    try {
      path = await _recorder.stop() ?? _path;
    } finally {
      _sound?.recording(false);
    }
    final ms = _clock.elapsedMilliseconds.clamp(0, maxDuration.inMilliseconds);
    if (path == null || path.isEmpty || ms < 500) return null;
    return (file: await recordingUpload(path, _mime), durationMs: ms);
  }

  Future<void> cancel() async {
    _clock.stop();
    try {
      await _recorderInstance?.cancel();
    } finally {
      _sound?.recording(false);
    }
  }

  Future<void> play(String mediaId) async {
    final generation = ++_playGeneration;
    late Source source;
    try {
      source = await playableVoice(mediaId, () => _api.downloadVoice(mediaId));
    } catch (_) {
      if (generation == _playGeneration) await stopPlaying();
      rethrow;
    }
    if (generation != _playGeneration) return;
    await _completion?.cancel();
    await _player.stop();
    if (generation != _playGeneration) return;
    _sound?.playingVoice(true);
    _playing.add(mediaId);
    _completion = _player.onPlayerComplete.listen((_) {
      if (generation == _playGeneration) {
        _sound?.playingVoice(false);
        _playing.add(null);
      }
    }, onError: (Object error, StackTrace stack) {
      if (generation == _playGeneration) {
        _sound?.playingVoice(false);
        _playing.add(null);
      }
    });
    try {
      await _player.play(source);
    } catch (_) {
      if (generation == _playGeneration) {
        _sound?.playingVoice(false);
        _playing.add(null);
      }
      rethrow;
    }
  }

  Future<void> stopPlaying() async {
    _playGeneration++;
    await _completion?.cancel();
    try {
      await _playerInstance?.stop();
    } finally {
      _sound?.playingVoice(false);
      _playing.add(null);
    }
  }

  Future<void> dispose() async {
    _playGeneration++;
    await _completion?.cancel();
    _sound?.recording(false);
    _sound?.playingVoice(false);
    // Плагины создаются лениво: если чатом со звуком не пользовались — освобождать нечего
    // (иначе dispose сам создавал бы рекордер и дёргал платформу уже при закрытии).
    await _recorderInstance?.dispose();
    await _playerInstance?.dispose();
    await _playing.close();
  }
}

final voiceProvider = Provider<Voice>((ref) {
  final voice = Voice(ref.read(apiProvider), sound: ref.read(soundProvider));
  ref.onDispose(voice.dispose);
  return voice;
});
