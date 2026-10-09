# Reviewed audio source scores

These are the original standalone preview generators used to compose the reviewed effects. They write to their own previews/ and previews_v3/ directories, not to game assets.

Install Python 3 dependencies: `python -m pip install numpy scipy tinysoundfont imageio-ffmpeg`. Place `GeneralUser-GS.sf2` from [the instrument author](https://github.com/mrbumpy409/GeneralUser-GS) beside these scripts. The bank is not bundled with the client. See [the instrument license](../../assets/audio/GENERALUSER-GS-LICENSE.txt).

Run `python create_previews.py`, then `python create_revision3.py`. The latter contains the revised cheerful victory cue. The scripts also recreate earlier music previews for provenance; those previews are not automatically installed into the game.

[reviewed-sfx.json](../../assets/audio/reviewed-sfx.json) maps the reviewed filenames to the installed assets and their SHA-256 hashes. The older `../generate_audio.py` creates the initial palette and must not be used to regenerate the reviewed effects.

## Music loop and loudness

`menu.mp3` is cut into a seamless loop: `python make_loop.py menu_source.mp3 menu_loop.wav 3.68 161.634` finds the phase near the given points where the end sounds like the start and crossfades 2.5 s there. Both music tracks are then normalized to about -23.5 LUFS so the menu and the game sound equally loud: `ffmpeg -i menu_loop.wav -af volume=-7.9dB -b:a 96k menu.mp3`, `ffmpeg -i game_old.mp3 -af volume=4.5dB -b:a 96k game.mp3`.
