import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/core/sound.dart';
import 'package:ghost_letters/features/game/game_audio.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  test('typing respects mute, voice, recording and lifecycle', () {
    final output = FakeSoundOutput();
    final sound = Sound(output, const SoundSettings())..unlock();
    sound.play(Sfx.typing);
    expect(output.effects, [Sfx.typing]);
    sound.playingVoice(true);
    sound.play(Sfx.typing);
    sound.playingVoice(false);
    sound.recording(true);
    sound.play(Sfx.typing);
    sound.recording(false);
    sound.active(false);
    sound.play(Sfx.typing);
    sound.active(true);
    sound.configure(const SoundSettings(effects: false));
    sound.play(Sfx.typing);
    expect(output.effects, [Sfx.typing]);
  });
  test('settings persist and disabled music never starts', () async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    final output = FakeSoundOutput();
    final container = ProviderContainer(overrides: [
      prefsProvider.overrideWithValue(prefs),
      soundOutputProvider.overrideWithValue(output),
    ]);
    final controller = container.read(soundSettingsProvider.notifier);
    await controller.update(
        music: false, effects: false, musicVolume: .3, effectsVolume: .7);
    final sound = container.read(soundProvider)..unlock();
    sound.scene(Music.game);
    sound.play(Sfx.yourTurn);
    expect(output.musicCalls.where((c) => c.track != null), isEmpty);
    expect(output.effects, isEmpty);
    container.dispose();
    final restored =
        ProviderContainer(overrides: [prefsProvider.overrideWithValue(prefs)]);
    addTearDown(restored.dispose);
    final settings = restored.read(soundSettingsProvider);
    expect(settings.music, isFalse);
    expect(settings.effects, isFalse);
    expect(settings.musicVolume, .3);
    expect(settings.effectsVolume, .7);
  });

  test('gesture, lifecycle, scene and independent voice ducking', () {
    final output = FakeSoundOutput();
    final sound = Sound(output, const SoundSettings());
    sound.scene(Music.game);
    expect(output.musicCalls.last.track, isNull);
    sound.unlock();
    expect(output.musicCalls.last.track, Music.game);
    sound.recording(true);
    expect(output.musicCalls.last.volume, closeTo(.18 * .18, .0001));
    sound.playingVoice(true);
    sound.recording(false);
    expect(output.musicCalls.last.volume, closeTo(.18 * .18, .0001));
    sound.playingVoice(false);
    expect(output.musicCalls.last.volume, .18);
    sound.active(false);
    sound.play(Sfx.phase);
    expect(output.musicCalls.last.track, isNull);
    expect(output.effects, isEmpty);
    sound.active(true);
    expect(output.musicCalls.last.track, Music.game);
  });

  test('your turn sounds once, baseline and missed snapshots are silent', () {
    final output = FakeSoundOutput();
    final sound = Sound(output, const SoundSettings())..unlock();
    final audio = GameAudio(sound);
    audio.update(
        snapshot(phase: 'Discussion', allowed: [], version: 1).view, null);
    final next = snapshot(phase: 'Voting', version: 2).view;
    audio.update(next, null);
    audio.update(next, null);
    audio.update(
        snapshot(phase: 'Mailbox', allowed: ['SendLetter'], version: 8).view,
        null);
    expect(output.effects, [Sfx.yourTurn]);
    audio.baseline(snapshot(version: 20).view, null);
    audio.update(snapshot(version: 20).view, null);
    expect(output.effects, [Sfx.yourTurn]);
    sound.configure(const SoundSettings(effects: false));
    audio.update(
        snapshot(phase: 'Night', allowed: ['SetTruth'], version: 21).view,
        null);
    expect(output.effects, [Sfx.yourTurn]);
  });

  test('countdown only sounds once for each observed final second', () {
    final output = FakeSoundOutput();
    final sound = Sound(output, const SoundSettings())..unlock();
    final audio = GameAudio(sound);
    final end = DateTime.utc(2030);
    audio.baseline(snapshot(phase: 'Discussion').view, end);
    audio.tick(end.subtract(const Duration(seconds: 11)));
    for (var i = 10; i >= 0; i--) {
      audio.tick(end.subtract(Duration(seconds: i)));
      audio.tick(end.subtract(Duration(seconds: i)));
    }
    expect(output.effects.where((e) => e == Sfx.tick).length, 10);
    expect(output.effects.last, Sfx.timeUp);
    output.effects.clear();
    audio.baseline(snapshot(phase: 'Discussion').view, end);
    audio.tick(end.add(const Duration(seconds: 10)));
    expect(output.effects, isEmpty);
  });

  test('letter, hint, vote, row and result transitions', () {
    GameView view(void Function(Json) change) {
      final json = snapshotJson()['view'] as Json;
      change(json);
      return GameView.fromJson(json);
    }

    final before = view((_) {});
    expect(
        GameAudio.transition(before, view((j) {
          (j['me'] as Json)['letters'] = [
            {'round': 1, 'cardId': 'orig_0300', 'revealed': false},
            {'round': 2, 'cardId': 'orig_0400', 'revealed': null},
          ];
        })),
        Sfx.letterSent);
    expect(
        GameAudio.transition(before, view((j) {
          j['vanishedCount'] = 7;
        })),
        Sfx.vanish);
    expect(
        GameAudio.transition(before, view((j) {
          j['hints'] = [
            {
              'round': 0,
              'cards': ['orig_0100', 'orig_0200']
            }
          ];
        })),
        Sfx.reveal);
    expect(
        GameAudio.transition(before, view((j) {
          (j['finale'] as Json)['myVote'] = {'column': 2};
        })),
        Sfx.vote);
    for (final correct in [true, false]) {
      expect(
          GameAudio.transition(before, view((j) {
            (j['finale'] as Json)['outcomes'] = [
              {'stage': 0, 'kind': 'Row', 'row': 0, 'correct': correct}
            ];
          })),
          correct ? Sfx.correct : Sfx.incorrect);
    }
    for (final won in [true, false]) {
      expect(
          GameAudio.transition(before, view((j) {
            (j['finale'] as Json)['result'] = {
              'winners': won ? ['u2'] : ['u1']
            };
          })),
          won ? Sfx.victory : Sfx.defeat);
    }
  });

  test('lazy output can be disposed without constructing players', () async {
    final output = PlayerSoundOutput();
    output.music(null, 0);
    output.dispose();
    await Future<void>.delayed(Duration.zero);
  });

  testWidgets(
      'live snapshots sound once; reconnect and disabled effects stay silent',
      (tester) async {
    tester.view.physicalSize = const Size(800, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.game = snapshot(phase: 'Discussion', allowed: [], version: 1);
    app.go('/game/g1');
    await tester.pumpAndSettle();
    app.container.read(soundProvider).unlock();
    final output = app.container.read(soundOutputProvider) as FakeSoundOutput;
    expect(output.effects, isEmpty);
    final next = snapshot(phase: 'Voting', version: 2);
    app.realtime.viewsCtl.add((view: next.view, deadline: null));
    await tester.pumpAndSettle();
    expect(output.effects, [Sfx.yourTurn]);
    app.realtime.viewsCtl.add((view: next.view, deadline: null));
    await tester.pumpAndSettle();
    expect(output.effects, [Sfx.yourTurn]);
    app.realtime.connectedCtl.add(false);
    await tester.pump();
    app.realtime.connectedCtl.add(true);
    final restored =
        snapshot(phase: 'Mailbox', allowed: ['SendLetter'], version: 3);
    app.realtime.viewsCtl.add((view: restored.view, deadline: null));
    await tester.pumpAndSettle();
    expect(output.effects, [Sfx.yourTurn]);
    await app.container
        .read(soundSettingsProvider.notifier)
        .update(effects: false);
    final muted = snapshot(phase: 'Voting', version: 4);
    app.realtime.viewsCtl.add((view: muted.view, deadline: null));
    await tester.pumpAndSettle();
    expect(output.effects, [Sfx.yourTurn]);
    await tester.pumpWidget(const SizedBox());
  });

  for (final width in [360.0, 1500.0]) {
    testWidgets('non-host audio settings open and work at width $width',
        (tester) async {
      tester.view.physicalSize = Size(width, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: watson);
      addTearDown(app.container.dispose);
      await tester.pumpWidget(app.widget);
      await tester.pumpAndSettle();
      expect(find.byTooltip('Звук и музыка'), findsOneWidget);
      app.realtime.game = snapshot();
      app.api.snapshotResult = snapshot();
      app.go('/game/g1');
      await tester.pumpAndSettle();
      await tester.tap(find.byType(PopupMenuButton<String>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Звук и музыка'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('music-switch')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('effects-switch')));
      await tester.pumpAndSettle();
      expect(app.container.read(soundSettingsProvider).music, isFalse);
      expect(app.container.read(soundSettingsProvider).effects, isFalse);

      await tester.pumpWidget(const SizedBox());
      await tester.pumpAndSettle();
    });
  }
}
