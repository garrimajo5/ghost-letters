# Reviewed audio source scores

These are the original standalone preview generators used to compose the reviewed effects. They write to their own previews/ and previews_v3/ directories, not to game assets.

Install Python 3 dependencies: `python -m pip install numpy scipy tinysoundfont imageio-ffmpeg`. Place `GeneralUser-GS.sf2` from [the instrument author](https://github.com/mrbumpy409/GeneralUser-GS) beside these scripts. The bank is not bundled with the client. See [the instrument license](../../assets/audio/GENERALUSER-GS-LICENSE.txt).

Run `python create_previews.py`, then `python create_revision3.py`. The latter contains the revised cheerful victory cue. The scripts also recreate earlier music previews for provenance; those previews are not automatically installed into the game.

[reviewed-sfx.json](../../assets/audio/reviewed-sfx.json) maps the reviewed filenames to the installed assets and their SHA-256 hashes. The older `../generate_audio.py` creates the initial palette and must not be used to regenerate the reviewed effects.

## Music loop and loudness

`menu.mp3` is cut into a seamless loop: `python make_loop.py menu_source.mp3 menu_loop.wav 3.68 161.634` finds the phase near the given points where the end sounds like the start and crossfades 2.5 s there. The menu is normalized to about -23.5 LUFS: `ffmpeg -i menu_loop.wav -af volume=-7.9dB -b:a 96k menu.mp3`.

## Approved game music: 1C and 2C

The exact approved MP3 recordings are in `sources/`. They were composed by `create_variants5.py` (which uses `create_revision4.py` and `create_previews.py`); instrument requirements and licensing are as above. Synthesized vocals do not use recorded voices.

Run `python build_game_music.py` with NumPy and FFmpeg installed, or pass `--ffmpeg /path/to/ffmpeg`. This rebuilds only `assets/audio/music/game.mp3` and its `game.source.json` provenance record. The 86-second circular arrangement uses 5-second equal-power crossfades in both directions. It starts five seconds into 1C; its beginning is heard during the crossfade at the end. Constant gain targets the existing quiet music level without changing the melodies. The script verifies decoding, duration, loudness, peak levels and the 2 MB size limit.

The game already selects `game.mp3` on `/game/` routes. It retains the existing background pause, voice ducking, settings and fades between menu/game scenes. Do not run the historical `generate_audio.py` over the approved assets.
