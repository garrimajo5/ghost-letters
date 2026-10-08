import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'session.dart';

enum Music { menu, game }

enum Sfx {
  phase,
  yourTurn,
  letterSent,
  reveal,
  vanish,
  chat,
  tick,
  timeUp,
  vote,
  correct,
  incorrect,
  victory,
  defeat
}

class SoundSettings {
  const SoundSettings(
      {this.music = true,
      this.effects = true,
      this.musicVolume = .18,
      this.effectsVolume = .45});
  final bool music;
  final bool effects;
  final double musicVolume;
  final double effectsVolume;
}

final soundSettingsProvider =
    NotifierProvider<SoundSettingsController, SoundSettings>(
        SoundSettingsController.new);

class SoundSettingsController extends Notifier<SoundSettings> {
  @override
  SoundSettings build() {
    final prefs = ref.read(prefsProvider);
    return SoundSettings(
      music: prefs.getBool('audio.music') ?? true,
      effects: prefs.getBool('audio.effects') ?? true,
      musicVolume: (prefs.getDouble('audio.musicVolume') ?? .18).clamp(0, 1),
      effectsVolume:
          (prefs.getDouble('audio.effectsVolume') ?? .45).clamp(0, 1),
    );
  }

  Future<void> update(
      {bool? music,
      bool? effects,
      double? musicVolume,
      double? effectsVolume}) async {
    state = SoundSettings(
      music: music ?? state.music,
      effects: effects ?? state.effects,
      musicVolume: (musicVolume ?? state.musicVolume).clamp(0, 1),
      effectsVolume: (effectsVolume ?? state.effectsVolume).clamp(0, 1),
    );
    final prefs = ref.read(prefsProvider);
    await Future.wait([
      prefs.setBool('audio.music', state.music),
      prefs.setBool('audio.effects', state.effects),
      prefs.setDouble('audio.musicVolume', state.musicVolume),
      prefs.setDouble('audio.effectsVolume', state.effectsVolume),
    ]);
  }
}

/// Replace this provider in tests: no platform player is constructed eagerly.
abstract class SoundOutput {
  void music(Music? track, double volume);
  void effect(Sfx effect, double volume);
  void dispose();
}

final soundOutputProvider = Provider<SoundOutput>((ref) {
  final output = PlayerSoundOutput();
  ref.onDispose(output.dispose);
  return output;
});

final soundProvider = Provider<Sound>((ref) {
  final sound =
      Sound(ref.read(soundOutputProvider), ref.read(soundSettingsProvider));
  ref.listen(soundSettingsProvider, (_, next) => sound.configure(next));
  return sound;
});

class Sound {
  Sound(this.output, this.settings);
  final SoundOutput output;
  SoundSettings settings;
  Music _track = Music.menu;
  bool _unlocked = false;
  bool _active = true;
  bool _recording = false;
  bool _playingVoice = false;

  void configure(SoundSettings value) {
    settings = value;
    _sync();
  }

  void unlock() {
    _unlocked = true;
    _sync(); // Also retries a browser-denied start on a subsequent gesture.
  }

  void scene(Music value) {
    if (_track != value) {
      _track = value;
      _sync();
    }
  }

  void active(bool value) {
    _active = value;
    _sync();
  }

  void recording(bool value) {
    _recording = value;
    _sync();
  }

  void playingVoice(bool value) {
    _playingVoice = value;
    _sync();
  }

  void _sync() {
    output.music(
        _unlocked && _active && settings.music && settings.musicVolume > 0
            ? _track
            : null,
        settings.musicVolume * (_recording || _playingVoice ? .18 : 1));
  }

  void play(Sfx effect) {
    if (_unlocked &&
        _active &&
        settings.effects &&
        settings.effectsVolume > 0) {
      output.effect(effect, settings.effectsVolume);
    }
  }
}

/// Two lazy players, bounded effects, and a cancellable fade between scenes.
/// Plugin failures (including a browser rejecting playback) stay in this boundary.
class PlayerSoundOutput implements SoundOutput {
  AudioPlayer? _music;
  AudioPlayer? _effect;
  Music? _loaded;
  Music? _sourceTrack;
  Music? _wanted;
  double _volume = 0;
  double _target = 0;
  bool _running = false;
  bool _disposed = false;
  DateTime? _lastEffect;
  int _effectGeneration = 0;

  @override
  void music(Music? track, double volume) {
    if (_disposed) return;
    _wanted = track;
    _target = volume;
    if (!_running) unawaited(_reconcile());
  }

  Future<void> _reconcile() async {
    _running = true;
    try {
      while (!_disposed) {
        if (_loaded != _wanted) {
          if (_music != null && _volume > 0) {
            _volume = (_volume - .025).clamp(0, 1);
            await _music!.setVolume(_volume);
          } else {
            if (_music != null) await _music!.pause();
            _loaded = null;
            final wanted = _wanted;
            if (wanted == null) break;
            final player = _music ??= AudioPlayer();
            await player.setReleaseMode(ReleaseMode.loop);
            if (_sourceTrack == wanted) {
              await player.setVolume(0);
              await player.resume();
            } else {
              await player.play(AssetSource('audio/music/${wanted.name}.mp3'),
                  volume: 0);
              _sourceTrack = wanted;
            }
            _loaded = wanted;
          }
        } else {
          if (_loaded == null || (_volume - _target).abs() < .001) break;
          _volume = _volume < _target
              ? (_volume + .025).clamp(0, _target)
              : (_volume - .025).clamp(_target, 1);
          await _music!.setVolume(_volume);
        }
        await Future<void>.delayed(const Duration(milliseconds: 40));
      }
    } catch (_) {
      _loaded = null;
      _volume = 0;
    } finally {
      _running = false;
    }
  }

  @override
  void effect(Sfx effect, double volume) {
    if (_disposed) return;
    final now = DateTime.now();
    if (_lastEffect != null &&
        now.difference(_lastEffect!) < const Duration(milliseconds: 150)) {
      return;
    }
    _lastEffect = now;
    unawaited(_playEffect(effect, volume, ++_effectGeneration));
  }

  Future<void> _playEffect(Sfx effect, double volume, int generation) async {
    try {
      final player = _effect ??= AudioPlayer();
      await player.stop();
      if (!_disposed && generation == _effectGeneration) {
        await player.play(AssetSource('audio/sfx/${effect.name}.mp3'),
            volume: volume);
      }
    } catch (_) {/* Audio must never interrupt gameplay. */}
  }

  @override
  void dispose() {
    _disposed = true;
    _effectGeneration++;
    unawaited(_close());
  }

  Future<void> _close() async {
    while (_running) {
      await Future<void>.delayed(const Duration(milliseconds: 40));
    }
    try {
      await _music?.dispose();
      await _effect?.dispose();
    } catch (_) {/* Teardown is best effort. */}
  }
}
