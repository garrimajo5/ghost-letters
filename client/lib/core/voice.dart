import 'dart:async';
import 'dart:io';

import 'package:audioplayers/audioplayers.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';
import 'package:record/record.dart';

import 'api.dart';

/// Голосовые: запись с микрофона (AAC, до 60 секунд) и прослушивание.
class Voice {
  Voice(this._api);

  static const maxDuration = Duration(seconds: 60);

  final Api _api;
  final AudioRecorder _recorder = AudioRecorder();
  final AudioPlayer _player = AudioPlayer();
  final Stopwatch _clock = Stopwatch();
  final Map<String, String> _cache = {};
  String? _path;

  final _playing = StreamController<String?>.broadcast();

  /// id голосового, которое играет сейчас (null — тишина).
  Stream<String?> get playing => _playing.stream;

  Duration get elapsed => _clock.elapsed;

  Future<bool> start() async {
    if (!await _recorder.hasPermission()) return false;
    final dir = await getTemporaryDirectory();
    final path = '${dir.path}/voice-${DateTime.now().millisecondsSinceEpoch}.m4a';
    await _recorder.start(const RecordConfig(encoder: AudioEncoder.aacLc, bitRate: 64000, sampleRate: 22050), path: path);
    _path = path;
    _clock
      ..reset()
      ..start();
    return true;
  }

  /// Остановить и вернуть файл с длительностью; null — запись слишком короткая.
  Future<({String path, int durationMs})?> stop() async {
    _clock.stop();
    final path = await _recorder.stop() ?? _path;
    final ms = _clock.elapsedMilliseconds.clamp(0, maxDuration.inMilliseconds);
    if (path == null || ms < 500) return null;
    return (path: path, durationMs: ms);
  }

  Future<void> cancel() async {
    _clock.stop();
    await _recorder.cancel();
  }

  Future<void> play(String mediaId) async {
    var path = _cache[mediaId];
    if (path == null || !File(path).existsSync()) {
      final dir = await getTemporaryDirectory();
      path = '${dir.path}/media-$mediaId.m4a';
      await _api.downloadVoice(mediaId, path);
      _cache[mediaId] = path;
    }

    await _player.stop();
    _playing.add(mediaId);
    await _player.play(DeviceFileSource(path));
    unawaited(_player.onPlayerComplete.first.then((_) => _playing.add(null)));
  }

  Future<void> stopPlaying() async {
    await _player.stop();
    _playing.add(null);
  }

  Future<void> dispose() async {
    await _recorder.dispose();
    await _player.dispose();
    await _playing.close();
  }
}

final voiceProvider = Provider<Voice>((ref) {
  final voice = Voice(ref.read(apiProvider));
  ref.onDispose(voice.dispose);
  return voice;
});
