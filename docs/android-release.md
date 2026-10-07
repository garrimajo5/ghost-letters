# Выпуск под Android (этап 5)

## 1. Ключ подписи — один раз

Google Play требует, чтобы все версии были подписаны одним ключом. Создайте его на своём компьютере
(нужна Java; она есть в Android Studio — `"C:\Program Files\Android\Android Studio\jbr\bin\keytool"`):

```
keytool -genkey -v -keystore upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload
```

Храните `upload.jks` и пароли в надёжном месте (не в репозитории). Потеряете — новую версию в Play не выложить.

## 2. Секреты в GitHub

Settings → Secrets and variables → Actions:

| Секрет | Что положить |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | содержимое `upload.jks` в base64: PowerShell `[Convert]::ToBase64String([IO.File]::ReadAllBytes("upload.jks"))` |
| `ANDROID_KEYSTORE_PASSWORD` | пароль keystore |
| `ANDROID_KEY_ALIAS` | `upload` |
| `ANDROID_KEY_PASSWORD` | пароль ключа |

Там же на вкладке **Variables** — `API_URL` = адрес сервера после этапа 4 (например `https://ghost.example.com`).

## 3. Сборка

- Actions → **Release Android** → Run workflow (можно указать другой адрес сервера) — в артефактах будут `.aab` и `.apk`.
- Или тег: `git tag v0.2.0 && git push origin v0.2.0` — дополнительно появится GitHub Release с файлами.

`.apk` можно сразу поставить на телефон (разрешить установку из неизвестных источников).
`.aab` загружается в Google Play Console.

## 4. Локально

Положите `android/key.properties` (в git не попадает):

```
storeFile=upload.jks
storePassword=...
keyAlias=upload
keyPassword=...
```

и `upload.jks` в `client/android/app/`, затем `flutter build apk --release --dart-define=API_URL=https://...`.
Без `key.properties` релиз подписывается отладочным ключом — для проверки на своём телефоне этого хватает.

## Что ещё нужно для Google Play

- Аккаунт разработчика Google Play (разовый взнос).
- Политика конфиденциальности (страница по ссылке): приложение записывает голосовые сообщения и хранит ник.
- Скриншоты, описание, иконка 512×512 — `tools/app-icon/icon_1024.png` подойдёт после уменьшения.
- Для релиза сервер должен быть по HTTPS (этап 4); http-адреса оставлены только для локальной отладки.
