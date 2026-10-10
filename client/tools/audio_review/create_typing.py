"""Original CC0-1.0 typing click. Run with the path to ffmpeg as argument."""
import math
from pathlib import Path
import random
import struct
import subprocess
import sys
import tempfile
import wave

rng = random.Random(10)
target = Path(__file__).resolve().parents[2] / 'assets/audio/sfx/typing.mp3'
with tempfile.TemporaryDirectory() as folder:
    source = Path(folder) / 'typing.wav'
    with wave.open(str(source), 'wb') as audio:
        audio.setparams((1, 2, 22050, 0, 'NONE', 'not compressed'))
        audio.writeframes(b''.join(struct.pack('<h', int(
            3500 * (rng.random() * 2 - 1) * math.exp(-i / 85) * min(1, i / 10)
        )) for i in range(700)))
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'ffmpeg', '-v', 'error',
                    '-y', '-i', str(source), '-b:a', '48k', str(target)], check=True)
