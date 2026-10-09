# Развёртывание сервера (этап 4)

Схема: **Caddy** (HTTPS, сертификат Let's Encrypt) → **API** (образ из GitHub Container Registry) → **PostgreSQL**. Всё в Docker Compose на одном VPS.


## Автоматически через GitHub Actions (рекомендуется)

Workflow **Deploy server** сам ставит Docker на чистый Ubuntu, создаёт `.env` с паролями, скачивает образ и запускает
сервер с HTTPS. Дальше обновляет сервер после каждого изменения `server/` в main. Каждую ночь делает копию базы (14 дней).

1. Отдельный ключ для деплоя (PowerShell на своём компьютере):
   ```
   ssh-keygen -t ed25519 -f $HOME\.ssh\ghost_deploy -N '""' -C ghost-deploy
   type $HOME\.ssh\ghost_deploy.pub | ssh root@<IP> "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
   ```
2. GitHub → Settings → Secrets and variables → Actions:
   - **Secrets** → `DEPLOY_SSH_KEY` = всё содержимое файла `ghost_deploy` (приватный ключ, без `.pub`);
   - **Variables** → `DEPLOY_HOST` = IP сервера; при необходимости `DEPLOY_USER` (по умолчанию `root`)
     и `DEPLOY_DOMAIN` (пока домена нет — не задавайте: будет `<ip-через-дефисы>.sslip.io`, HTTPS работает и так).
3. Actions → **Deploy server** → Run workflow. В конце шага Health check — адрес сервера.

Клиент на этот сервер: `flutter run --dart-define=API_URL=https://<адрес>`.

## Вручную

Если без GitHub Actions — ниже ручная установка.

## Что нужно

- VPS с Ubuntu 22.04/24.04: 1 vCPU, 1–2 ГБ памяти, 20 ГБ диска — для компании друзей достаточно.
- Домен (или поддомен), A-запись которого указывает на IP сервера. HTTPS нужен приложениям: Android и iOS по умолчанию не ходят на http.
- Открытые порты 80 и 443.

## Образ сервера

Workflow **Release server image** собирает образ при каждом изменении `server/` в `main` и публикует
`ghcr.io/garrimajo5/ghost-letters-api:latest`. Пакет приватный: на сервере нужен вход в GHCR
токеном GitHub с правом `read:packages` (Settings → Developer settings → Personal access tokens).

## Первый запуск

```bash
# на сервере
curl -fsSL https://get.docker.com | sh
mkdir -p ~/ghost-letters && cd ~/ghost-letters
# скопируйте сюда из репозитория: deploy/docker-compose.prod.yml, deploy/Caddyfile, deploy/.env.example
# и client/assets/cards/cards.json + tags.json + details.json (признаки картинок для ботов)
cp .env.example .env && nano .env          # домен, пароль базы, ключ JWT
echo <токен> | docker login ghcr.io -u garrimajo5 --password-stdin
docker compose -f docker-compose.prod.yml up -d
curl https://<домен>/health               # → Healthy
```

Миграции базы применяются при старте API. Голосовые хранятся в томе `media`, база — в `pgdata`.

## Обновление

```bash
docker compose -f docker-compose.prod.yml pull api
docker compose -f docker-compose.prod.yml up -d api
```

## Клиент с этим сервером

```bash
flutter run --dart-define=API_URL=https://<домен>
flutter build apk --release --dart-define=API_URL=https://<домен>
```

## Публикация Android

Workflow `Release Android` публикует `/download/ghost-letters.apk` и сведения о версии.
При наличии секретов `ANDROID_KEYSTORE_*` используется заданный ключ. Без них, если
настроены `DEPLOY_HOST` и `DEPLOY_SSH_KEY`, первая публикация создаёт постоянный ключ
в `/opt/ghost-letters/android-signing/` на сервере. Следующие сборки используют тот же ключ.
Каталог закрыт правами 700, файлы — 600; он находится вне папки веб-сервера.

Сохраните этот каталог в защищённой резервной копии вместе с базой. Если APK уже существует,
а ключ отсутствует, workflow останавливается: нужно восстановить исходный ключ, а не создавать новый.
Незавершённый каталог `android-signing.pending` также требует проверки перед повтором.
PR проверяет сборку и не создаёт ключей на сервере; публикация выполняется после слияния в main.
При отсутствии APK страница загрузки сообщает об этом и предлагает веб-версию.

## Резервная копия базы

```bash
docker compose -f docker-compose.prod.yml exec postgres pg_dump -U ghost ghost_letters | gzip > backup-$(date +%F).sql.gz
```

## Безопасность

- Ключ JWT и пароль базы — только в `.env` на сервере, не в репозитории.
- Вход (`/api/v1/auth/*`) ограничен 30 запросами в минуту с одного адреса (`AUTH_PER_MINUTE`).
- Порт PostgreSQL наружу не открыт: база доступна только API внутри Docker-сети.
