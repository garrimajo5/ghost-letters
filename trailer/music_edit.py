"""Apply the optional night pause without altering the supplied music master."""
import hashlib
import json
import subprocess

import numpy as np


def timeline_markers(cfg, root):
    markers = json.loads((root / cfg['markers']).read_text(encoding='utf-8'))
    edit = cfg.get('music_edit')
    if not edit:
        return markers
    at, pause = edit['at'], edit['pause_seconds']
    bar = markers['bar_seconds']
    count = round(pause / bar)
    assert abs(count * bar - pause) < 1e-6, 'Night pause must use whole bars'
    markers['bars'] = [round(t if t < at else t + pause, 6) for t in markers['bars']]
    markers['bars'] = sorted(markers['bars'] + [round(at + i * bar, 6) for i in range(count)])
    markers['music_cuts_in_edit'] = [round(t if t < at else t + pause, 6) for t in markers['music_cuts_in_edit']]
    for section in markers['sections']:
        # The brief rounds section endpoints; map with the same rounding tolerance.
        if section['from'] >= at - .005:
            section['from'] = round(section['from'] + pause, 6)
            section['to'] = round(section['to'] + pause, 6)
    markers['sections'].append({'name': 'night_pause', 'from': at, 'to': round(at + pause, 6),
                                'note': 'Город засыпает; Убийца выбирает карты; затем резкий вход музыки'})
    markers['sections'].sort(key=lambda section: section['from'])
    markers['duration'] = cfg['duration']
    markers['source_master'] = markers.pop('file')
    markers['edit'] = edit
    return markers


def build_music(cfg, root, work, ff):
    source = root / cfg['music']
    edit = cfg.get('music_edit')
    if not edit:
        return source
    fingerprint = hashlib.sha256(source.read_bytes() + json.dumps(
        {'edit': edit, 'duration': cfg['duration'], 'revision': 1}, sort_keys=True).encode()).hexdigest()[:16]
    output = work / f'night-music-{fingerprint}.wav'
    if output.exists():
        return output
    sr = 48000
    data = subprocess.check_output([ff, '-v', 'error', '-i', str(source),
                                    '-ar', str(sr), '-ac', '2', '-f', 'f32le', '-'])
    pcm = np.frombuffer(data, dtype='<f4').reshape(-1, 2).copy()
    cut = round(edit['at'] * sr)
    fade_start = round(edit['fade_out_start'] * sr)
    pause = round(edit['pause_seconds'] * sr)
    assert 0 <= fade_start < cut < len(pcm)
    assert edit['resume_declick_seconds'] <= .02, 'Keep the musical entrance sharp'
    # Half-cosine reaches zero with zero slope, avoiding a step at the pause.
    fade = .5 + .5 * np.cos(np.linspace(0, np.pi, cut - fade_start))
    pcm[fade_start:cut] *= fade[:, None]
    declick = round(edit['resume_declick_seconds'] * sr)
    if declick:
        pcm[cut:cut + declick] *= np.linspace(0, 1, declick)[:, None]
    edited = np.concatenate([pcm[:cut], np.zeros((pause, 2), dtype='<f4'), pcm[cut:]])
    length = round(cfg['duration'] * sr)
    assert abs(len(edited) - length) < sr / 10, 'Timeline and music duration disagree'
    edited = np.pad(edited, ((0, max(0, length - len(edited))), (0, 0)))[:length]
    # Lossless intermediate: only the final AAC mux introduces a new lossy encode.
    subprocess.run([ff, '-y', '-v', 'error', '-f', 'f32le', '-ar', str(sr), '-ac', '2',
                    '-i', '-', '-c:a', 'pcm_s24le', str(output)],
                   input=edited.astype('<f4').tobytes(), check=True)
    return output
