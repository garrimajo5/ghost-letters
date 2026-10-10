"""Operator-only recovery using existing SSH + PostgreSQL access, never a public API.

Run only after AccountRecoveryKeys migration. Password is read by getpass, not argv.
SQL sent over SSH contains only a random salt and PBKDF2 hash. No schema changes.
"""
import argparse
import base64
import getpass
import hashlib
import re
import secrets
import subprocess
import sys
import unicodedata
import uuid


def reset_sql(user_id, login, word):
    user_id = str(uuid.UUID(user_id))
    login = login.strip().lower()
    if not re.fullmatch(r"[a-z0-9_-]{3,32}", login):
        raise ValueError("Login must contain 3–32 ASCII letters, digits, _ or -")
    if not 5 <= len(word) <= 128 or not word.strip():
        raise ValueError("Key must contain 5–128 characters")
    salt = secrets.token_bytes(16)
    secret = "word:" + unicodedata.normalize("NFC", word)
    hashed = base64.b64encode(hashlib.pbkdf2_hmac("sha256", secret.encode(), salt, 600000)).decode()
    salt = base64.b64encode(salt).decode()
    return f"""BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('{user_id}', 1));
SELECT pg_advisory_xact_lock(hashtextextended('{login}', 2));
DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM users WHERE id = '{user_id}' AND NOT is_bot) THEN
  RAISE EXCEPTION 'Human account not found';
 END IF;
END $$;
INSERT INTO recovery_credentials (user_id, login, kind, salt, key_hash, iterations, failed_attempts, locked_until)
VALUES ('{user_id}', '{login}', 'word', '{salt}', '{hashed}', 600000, 0, NULL)
ON CONFLICT (user_id) DO UPDATE SET login = EXCLUDED.login, kind = EXCLUDED.kind,
 salt = EXCLUDED.salt, key_hash = EXCLUDED.key_hash, iterations = EXCLUDED.iterations,
 failed_attempts = 0, locked_until = NULL;
UPDATE refresh_tokens SET revoked_at = now() WHERE user_id = '{user_id}' AND revoked_at IS NULL;
DELETE FROM auth_identities WHERE user_id = '{user_id}' AND provider = 'link';
COMMIT;
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--user-id", required=True)
    parser.add_argument("--login", required=True)
    parser.add_argument("--ssh", required=True, help="SSH destination, e.g. root@example.com")
    parser.add_argument("--identity", required=True, help="Path to SSH key")
    parser.add_argument("--password-stdin", action="store_true", help="For an authorized operator's automation")
    args = parser.parse_args()
    word = sys.stdin.readline().rstrip("\r\n") if args.password_stdin else getpass.getpass("New word/phrase: ")
    sql = reset_sql(args.user_id, args.login, word)
    result = subprocess.run(["ssh", "-o", "BatchMode=yes", "-i", args.identity, args.ssh,
        "docker exec -i ghost-letters-postgres-1 psql -X -q -v ON_ERROR_STOP=1 -U ghost -d ghost_letters"],
        input=sql, text=True, encoding="utf-8", capture_output=True)
    if result.returncode:
        # PostgreSQL may include the SQL statement in an error. Do not echo credentials.
        raise SystemExit("Recovery failed; verify migration, account ID, unique login and SSH access. No partial reset committed.")
    print("Key updated for the requested account. Old refresh sessions and link codes revoked.")


if __name__ == "__main__":
    main()
