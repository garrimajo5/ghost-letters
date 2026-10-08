import '../../core/sound.dart';
import '../../models/models.dart';
import 'game_state.dart';

/// One most relevant cue per live transition. Snapshots establish a silent baseline.
class GameAudio {
  GameAudio(this.sound);
  final Sound sound;
  GameView? _previous;
  DateTime? _deadline;
  int? _seconds;

  void baseline(GameView view, DateTime? deadline) {
    _previous = view;
    _deadline = deadline;
    _seconds = null;
  }

  void update(GameView next, DateTime? deadline) {
    final old = _previous;
    if (old == null || old.gameId != next.gameId) {
      baseline(next, deadline);
      return;
    }
    if (next.version <= old.version) return;
    _previous = next;
    if (deadline != _deadline) {
      _deadline = deadline;
      _seconds = null;
    }
    // A gap represents missed events, not a queue of sounds to replay.
    if (next.version != old.version + 1) return;
    final cue = transition(old, next);
    if (cue != null) sound.play(cue);
  }

  static Sfx? transition(GameView old, GameView next) {
    final result = next.finale?.result;
    if (old.finale?.result == null && result != null && next.me != null) {
      return result.winners.contains(next.me!.id) ? Sfx.victory : Sfx.defeat;
    }
    for (final outcome in next.finale?.outcomes ?? <VoteOutcome>[]) {
      if (outcome.kind != 'Row' || outcome.correct == null) continue;
      final known = old.finale?.outcomes.any((o) =>
              o.stage == outcome.stage && o.correct == outcome.correct) ??
          false;
      if (!known) return outcome.correct! ? Sfx.correct : Sfx.incorrect;
    }
    for (final letter in next.me?.letters ?? <MyLetter>[]) {
      final previous = old.me?.letters
          .where((l) => l.round == letter.round && l.cardId == letter.cardId)
          .firstOrNull;
      if (previous == null) return Sfx.letterSent;
      if (previous.revealed != letter.revealed) {
        if (letter.revealed == true) return Sfx.reveal;
        if (letter.revealed == false) return Sfx.vanish;
      }
    }
    final oldHints = {
      for (final h in old.hints)
        for (final card in h.cards) '${h.round}:$card'
    };
    if (next.hints
        .any((h) => h.cards.any((c) => !oldHints.contains('${h.round}:$c')))) {
      return Sfx.reveal;
    }
    if (next.vanishedCount > old.vanishedCount) return Sfx.vanish;
    if (next.finale?.hasMyVote == true && old.finale?.hasMyVote != true) {
      return Sfx.vote;
    }
    final oldVotes = old.finale?.votes ?? <VoteRecord>[];
    if (next.me != null &&
        (next.finale?.votes.any((v) =>
                v.voter == next.me!.id &&
                !oldVotes.any((o) =>
                    o.voter == v.voter &&
                    o.stage == v.stage &&
                    o.attempt == v.attempt)) ??
            false)) {
      return Sfx.vote;
    }
    final phaseChanged = old.phase != next.phase || old.round != next.round;
    if (needsMe(next) &&
        (!needsMe(old) ||
            phaseChanged ||
            (next.currentSpeaker == next.me?.id &&
                old.currentSpeaker != next.currentSpeaker))) {
      return Sfx.yourTurn;
    }
    if (phaseChanged) return Sfx.phase;
    return null;
  }

  void tick(DateTime now) {
    final end = _deadline;
    if (_previous?.phase != 'Discussion' || end == null) {
      _seconds = null;
      return;
    }
    final remaining =
        (end.difference(now).inMilliseconds / 1000).ceil().clamp(0, 1000000);
    final old = _seconds;
    _seconds = remaining;
    // No catch-up ticks on load/resume or after a delayed callback.
    if (old == null || old - remaining != 1) return;
    if (remaining == 0) {
      sound.play(Sfx.timeUp);
    } else if (remaining <= 10) {
      sound.play(Sfx.tick);
    }
  }
}
