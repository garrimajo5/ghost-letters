# Reviewed audio source scores

These are the original standalone preview generators used to compose the reviewed effects. They write to their own previews/ and previews_v3/ directories, not to game assets.

Install Python 3 dependencies: `python -m pip install numpy scipy tinysoundfont imageio-ffmpeg`. Place `GeneralUser-GS.sf2` from [the instrument author](https://github.com/mrbumpy409/GeneralUser-GS) beside these scripts. The bank is not bundled with the client. See [the instrument license](../../assets/audio/GENERALUSER-GS-LICENSE.txt).

Run `python create_previews.py`, then `python create_revision3.py`. The latter contains the revised cheerful victory cue. The scripts also recreate earlier music previews for provenance; those previews are not automatically installed into the game.

[reviewed-sfx.json](../../assets/audio/reviewed-sfx.json) maps the reviewed filenames to the installed assets and their SHA-256 hashes. The older `../generate_audio.py` creates the initial palette and must not be used to regenerate the reviewed effects.
