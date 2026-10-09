"""Exercise key provisioning against a fake SSH server, never the real deployment."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('android-signing.sh').resolve()
MOCK = r'''#!/usr/bin/env python3
import os, pathlib, shutil, subprocess, sys
tool = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
remote = os.environ['FAKE_REMOTE']
def path(value):
    return pathlib.Path(value.split(':', 1)[1].replace('/opt/ghost-letters', remote)) if value.startswith('tester@host:') else pathlib.Path(value)
if tool == 'ssh':
    sys.exit(subprocess.run(['bash', '-c', args[-1].replace('/opt/ghost-letters', remote)]).returncode)
if tool == 'scp':
    sources, dest = args[2:-1], path(args[-1])
    for source in sources:
        src = path(source)
        shutil.copyfile(src, dest / src.name if dest.is_dir() else dest)
elif tool == 'openssl':
    print('test-password-not-for-production')
elif tool == 'keytool':
    key = pathlib.Path(args[args.index('-keystore') + 1])
    if '-genkeypair' in args:
        key.write_bytes(b'test-key-not-for-production')
    elif not key.is_file():
        sys.exit(1)
'''


@unittest.skipIf(os.name == 'nt', 'Linux release runner script')
class AndroidSigningTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.remote = self.root / 'server'
        self.remote.mkdir()
        self.client = self.root / 'client'
        (self.client / 'android/app').mkdir(parents=True)
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        for tool in ['ssh', 'scp', 'openssl', 'keytool']:
            file = self.bin / tool
            file.write_text(MOCK)
            file.chmod(0o700)

    def run_script(self):
        return subprocess.run(['bash', str(SCRIPT)], cwd=self.client, text=True, capture_output=True,
            env=dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ['PATH'],
                     HOST='host', SSH_USER='tester', SSH_KEY_FILE='fake', RUNNER_TEMP=str(self.root), FAKE_REMOTE=str(self.remote)))

    def test_first_release_creates_private_key_and_next_reuses_it(self):
        first = self.run_script()
        self.assertEqual(first.returncode, 0, first.stderr)
        key = self.remote / 'android-signing/upload.jks'
        self.assertEqual(key.stat().st_mode & 0o777, 0o600)
        original = key.read_bytes()
        second = self.run_script()
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertEqual(key.read_bytes(), original)
        self.assertEqual((self.client / 'android/app/upload.jks').read_bytes(), original)
        self.assertIn('keyAlias=ghostletters', (self.client / 'android/key.properties').read_text())

    def test_existing_apk_without_original_key_is_not_resigned(self):
        apk = self.remote / 'www/download/ghost-letters.apk'
        apk.parent.mkdir(parents=True)
        apk.write_bytes(b'existing')
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('automatic key rotation is forbidden', result.stdout)
        self.assertFalse((self.remote / 'android-signing').exists())

    def test_partial_signing_directory_is_not_overwritten(self):
        (self.remote / 'android-signing').mkdir()
        self.assertNotEqual(self.run_script().returncode, 0)


if __name__ == '__main__':
    unittest.main()
