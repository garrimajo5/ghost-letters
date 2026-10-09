# Audio sources and licenses

Reviewed effects replace the first synthesized palette; their original preview filenames and SHA-256 hashes are listed in [reviewed-sfx.json](reviewed-sfx.json). The menu uses the Suno track supplied by the project owner.

## Current music

| File | Source | License |
| --- | --- | --- |
| music/menu.mp3 | [Untitled by garrimajo5 on Suno](https://suno.com/s/45xEtZbI4p8SxEA4), owner-supplied Untitled.wav | Owner-supplied for this project; subject to applicable Suno terms, not CC0 |
| music/game.mp3 | Approved **1C — Два голоса** and **2C — Далёкие голоса**, [original score v5](../../tools/audio_review/create_variants5.py), [loop assembly](../../tools/audio_review/build_game_music.py) | Original compositions and synthesized vocals; GeneralUser GS instrument license for piano samples (see below) |

The game soundtrack alternates the two owner-approved 48-second recordings with 5-second crossfades, including the return from 2C to 1C. The resulting loop is 86 seconds, stereo MP3 at 128 kbps, 1,377,218 bytes, approximately -24 LUFS. It replaces only `music/game.mp3`; menu audio is unchanged. The approved source recordings are preserved in [sources](../../tools/audio_review/sources/), outside the bundled application assets. Source and output hashes, measured loudness and loop details are in [game.source.json](music/game.source.json). These are original piano and procedurally synthesized wordless folk-vocal compositions, not Suno recordings or recordings of a singer. GeneralUser GS 2.0.3 permits private and commercial music creation; [instrument source](https://github.com/mrbumpy409/GeneralUser-GS), [full license](GENERALUSER-GS-LICENSE.txt). No CC0 claim is made for the instrument samples.

The owner explicitly requested inclusion of the menu track and supplied the WAV. No public-domain or blanket commercial-use license is claimed for it. Rights under the creator's Suno plan have not been independently verified. This track is an explicit owner-selected exception to the original CC0 asset selection. The complete 179.8935-second recording is encoded as stereo MP3 at 80 kbps (1,799,620 bytes), without trimming. It repeats through the existing music player; the recording has not been edited into a seamless musical loop. Source and asset hashes are in [menu.source.json](music/menu.source.json).

## Reviewed action effects

Sources: [preview score v2](../../tools/audio_review/create_previews.py) and [revised victory score v3](../../tools/audio_review/create_revision3.py). These are original compositions, not downloaded songs.

| File | Event / sound | License |
| --- | --- | --- |
| sfx/phase.mp3 | Phase change; soft chime | CC0-1.0, original synthesis |
| sfx/yourTurn.mp3 | Your turn; rising piano | Original composition, GeneralUser GS instruments |
| sfx/letterSent.mp3 | Own letter submitted; paper and click | CC0-1.0, original synthesis |
| sfx/reveal.mp3 | Hints or letter revealed; airy chime | CC0-1.0, original synthesis |
| sfx/vanish.mp3 | Letter vanished; fading air | CC0-1.0, original synthesis |
| sfx/chat.mp3 | New live message with chat closed | CC0-1.0, original synthesis |
| sfx/tick.mp3 | Last ten seconds of discussion | CC0-1.0, original synthesis |
| sfx/timeUp.mp3 | Discussion deadline reached | CC0-1.0, original synthesis |
| sfx/vote.mp3 | Own vote accepted | CC0-1.0, original synthesis |
| sfx/correct.mp3 | Correct row; rising piano | Original composition, GeneralUser GS instruments |
| sfx/incorrect.mp3 | Incorrect row; falling piano | Original composition, GeneralUser GS instruments |
| sfx/victory.mp3 | Cheerful major-key victory; piano and pizzicato | Original composition, GeneralUser GS instruments |
| sfx/defeat.mp3 | Defeat; piano and strings | Original composition, GeneralUser GS instruments |

CC0 items and their original synthesis code are dedicated, to the extent any rights exist, under [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/).

Sample-based effects use GeneralUser GS 2.0.3 by S. Christian Collins. [Author/source](https://github.com/mrbumpy409/GeneralUser-GS), [full instrument license](GENERALUSER-GS-LICENSE.txt). GeneralUser GS License v2.0 permits private and commercial music creation. The rendered effects may be used in this application; the SoundFont bank is not included. Its license remains applicable to the instrument samples; those samples are not re-licensed as CC0.

[Reproduction instructions](../../tools/audio_review/README.md). The historical generator creates the initial palette, so it should not overwrite these reviewed effects.

Effects are stereo MP3 at 128 kbps, all below 100 KB. Music is stereo MP3, each below 2 MB. Settings default to music at 18% and effects at 45%.

## Controls and behavior

Open **Звук и музыка** with the home screen speaker button or the game menu (available to every player). Music and effects have independent switches and volume sliders, saved only on this device. Playback unlocks on the first pointer/keyboard gesture, music pauses in the background and ducks during voice playback or recording.

Game cues use differences between live snapshots, with one prioritized cue per change. Initial loads, duplicate snapshots and reconnect baselines remain silent.

