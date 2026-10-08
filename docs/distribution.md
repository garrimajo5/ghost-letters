# Раздача приложения друзьям (без магазинов)

Всё лежит на нашем сервере: **https://193-233-137-48.sslip.io/download/** — одна ссылка для всех.

- **Android** — APK по кнопке «Скачать приложение». Обновления ставятся поверх старой версии.
- **iPhone** — веб-версия: открыть ссылку в Safari → «Поделиться» → «На экран „Домой“».

## Как это обновляется

| Что | Когда | Workflow |
|---|---|---|
| Веб-версия (`/`) | каждый мерж изменений клиента в main | Deploy web |
| APK (`/download/ghost-letters.apk`) | каждый мерж изменений клиента в main, тег `v*`, ручной запуск | Release Android |
| Сервер, Caddy, страница загрузки | изменения `server/**` или `deploy/**` в main | Release server image → Deploy server |

APK выкладывается, только если заданы секреты подписи: иначе каждая версия была бы подписана
другим ключом, и Android не дал бы обновиться поверх старой.

## Разовая настройка (владелец)

1. Создать ключ подписи (Windows, PowerShell):
   ```
   & "C:\Program Files\Android\Android Studio\jbr\bin\keytool.exe" -genkey -v -keystore $HOME\ghost-letters-upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload
   ```
2. Перевести ключ в base64:
   ```
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("$HOME\ghost-letters-upload.jks")) | Set-Clipboard
   ```
3. GitHub → Settings → Secrets and variables → Actions → Secrets:
   `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` (= `upload`), `ANDROID_KEY_PASSWORD`.
4. Там же, вкладка Variables: `API_URL` = `https://193-233-137-48.sslip.io`.
5. Actions → Release Android → Run workflow.

Файл `.jks` и пароли — сохранить в надёжном месте (облако, менеджер паролей). Потеряете ключ — друзьям
придётся удалить приложение и поставить заново.
