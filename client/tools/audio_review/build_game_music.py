"""Join the approved 1C / 2C recordings into a circular game-only soundtrack."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent
SR = 44100
FADE = 5
SOURCES = [('01C_two_voices_v5.mp3', '1C — Два голоса'),
           ('02C_far_voices_v5.mp3', '2C — Далёкие голоса')]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(ffmpeg, *args):
    return subprocess.run([ffmpeg, '-hide_banner', '-nostdin', *args],
                          check=True, capture_output=True)


def decode(ffmpeg, path):
    result = run(ffmpeg, '-v', 'error', '-i', str(path), '-ac', '2', '-ar', str(SR), '-f', 'f32le', '-')
    return np.frombuffer(result.stdout, dtype='<f4').reshape(-1, 2).astype(np.float64)


def loudness(ffmpeg, path):
    result = run(ffmpeg, '-i', str(path), '-af', 'loudnorm=I=-23.5:TP=-2:LRA=11:print_format=json', '-f', 'null', '-')
    log = result.stderr.decode('utf-8', errors='replace')
    return json.loads(log[log.rfind('{'):log.rfind('}') + 1])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ffmpeg', default='ffmpeg')
    args = parser.parse_args()
    tracks = [decode(args.ffmpeg, ROOT / 'sources' / name) for name, _ in SOURCES]
    n = FADE * SR
    assert all(len(x) > 2 * n and np.isfinite(x).all() for x in tracks)
    t = np.linspace(0, np.pi / 2, n)[:, None]
    a, b = tracks
    # Start at A+5s; the ending crossfade supplies A's first five seconds.
    # Last -> first sample is consequently a natural adjacent pair in A.
    mix = np.concatenate([a[n:-n], a[-n:] * np.cos(t) + b[:n] * np.sin(t),
                          b[n:-n], b[-n:] * np.cos(t) + a[:n] * np.sin(t)])
    assert np.abs(mix).max() < 1, 'Crossfade must not clip before encoding'
    out = ROOT.parent.parent / 'assets/audio/music/game.mp3'
    with tempfile.TemporaryDirectory() as temp:
        wav = Path(temp) / 'game.wav'
        with wave.open(str(wav), 'wb') as stream:
            stream.setnchannels(2)
            stream.setsampwidth(2)
            stream.setframerate(SR)
            stream.writeframes((np.clip(mix, -1, 1) * 32767).astype('<i2').tobytes())
        levels = loudness(args.ffmpeg, wav)
        # Constant gain preserves dynamics and the sample-continuous loop.
        gain = min(-23.5 - float(levels['input_i']), -2 - float(levels['input_tp']))
        run(args.ffmpeg, '-y', '-i', str(wav), '-af', f'volume={gain:.4f}dB',
            '-ar', str(SR), '-ac', '2', '-c:a', 'libmp3lame', '-b:a', '128k', str(out))
    decoded = decode(args.ffmpeg, out)
    levels = loudness(args.ffmpeg, out)
    assert len(decoded) == len(mix)
    assert np.isfinite(decoded).all() and np.abs(decoded).max() < .95
    assert out.stat().st_size < 2_000_000
    assert abs(float(levels['input_i']) + 23.5) < .5
    metadata = {
        'sources': [{'file': name, 'title': title, 'sha256': sha(ROOT / 'sources' / name),
                     'seconds': len(track) / SR}
                    for (name, title), track in zip(SOURCES, tracks)],
        'order': ['1C', '2C', 'repeat'], 'crossfade_seconds': FADE,
        'duration_seconds': len(decoded) / SR, 'integrated_lufs': float(levels['input_i']),
        'true_peak_dbfs': float(levels['input_tp']),
        'encoding': 'Stereo MP3, 128 kbps, 44100 Hz',
        'asset_bytes': out.stat().st_size, 'asset_sha256': sha(out),
        'pcm_loop_jump': float(np.abs(mix[-1] - mix[0]).max()),
        'decoded_loop_jump': float(np.abs(decoded[-1] - decoded[0]).max()),
        'tool': 'client/tools/audio_review/build_game_music.py',
    }
    out.with_suffix('.source.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(metadata, ensure_ascii=True, indent=2))


if __name__ == '__main__':
    main()
