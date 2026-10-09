#!/usr/bin/env bash
# CI fallback for direct APK distribution when GitHub signing secrets are absent.
# Keep this directory in server backups: changing the key breaks Android updates.
# Fixed server paths intentionally expand on the runner before SSH.
# shellcheck disable=SC2029
set -euo pipefail
umask 077
: "${HOST:?DEPLOY_HOST is required}"
: "${SSH_USER:?DEPLOY_USER is required}"
: "${SSH_KEY_FILE:?SSH key file is required}"
: "${RUNNER_TEMP:?Runner temporary directory is required}"

remote="$SSH_USER@$HOST"
key_dir=/opt/ghost-letters/android-signing
local_dir=$(mktemp -d "$RUNNER_TEMP/android-signing.XXXXXX")
trap 'rm -rf -- "$local_dir"' EXIT

if ssh -i "$SSH_KEY_FILE" "$remote" "test -s $key_dir/upload.jks && test -s $key_dir/password"; then
  scp -i "$SSH_KEY_FILE" "$remote:$key_dir/upload.jks" "$local_dir/upload.jks"
  scp -i "$SSH_KEY_FILE" "$remote:$key_dir/password" "$local_dir/password"
else
  # Never replace an existing app's identity or a partially saved key.
  ssh -i "$SSH_KEY_FILE" "$remote" \
    "test ! -e $key_dir && test ! -f /opt/ghost-letters/www/download/ghost-letters.apk" || {
      echo '::error::APK or signing directory already exists. Restore the original signing key; automatic key rotation is forbidden.'
      exit 1
    }
  openssl rand -hex 32 > "$local_dir/password"
  keytool -genkeypair -noprompt -keystore "$local_dir/upload.jks" -storetype PKCS12 \
    -storepass:file "$local_dir/password" -keypass:file "$local_dir/password" \
    -alias ghostletters -keyalg RSA -keysize 3072 -validity 10000 \
    -dname 'CN=Ghost Letters, O=Ghost Letters'
  # Workflow concurrency serializes releases; a staging directory prevents partial keys from being used.
  ssh -i "$SSH_KEY_FILE" "$remote" "umask 077; mkdir $key_dir.pending"
  scp -i "$SSH_KEY_FILE" "$local_dir/upload.jks" "$local_dir/password" "$remote:$key_dir.pending/"
  ssh -i "$SSH_KEY_FILE" "$remote" "chmod 700 $key_dir.pending; chmod 600 $key_dir.pending/*; mv $key_dir.pending $key_dir"
fi

password=$(cat "$local_dir/password")
echo "::add-mask::$password"
keytool -list -keystore "$local_dir/upload.jks" -storepass:file "$local_dir/password" -alias ghostletters > /dev/null
cp "$local_dir/upload.jks" android/app/upload.jks
printf 'storeFile=upload.jks\nstorePassword=%s\nkeyAlias=ghostletters\nkeyPassword=%s\n' \
  "$password" "$password" > android/key.properties
