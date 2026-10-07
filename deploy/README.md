# Развёртывание сервера (этап 4)

Схема: **Caddy** (HTTPS, сертификат Let's Encrypt) → **API** (образ из GitHub Container Registry) → **PostgreSQL**. Всё в Docker Compose на одном VPS.

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
# и client/assets/cards/cards.json + tags.json (теги картинок для ботов; например, scp с вашего компьютера)
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

## Резервная копия базы

```bash
docker compose -f docker-compose.prod.yml exec postgres pg_dump -U ghost ghost_letters | gzip > backup-$(date +%F).sql.gz
```

## Безопасность

- Ключ JWT и пароль базы — только в `.env` на сервере, не в репозитории.
- Вход (`/api/v1/auth/*`) ограничен 30 запросами в минуту с одного адреса (`AUTH_PER_MINUTE`).
- Порт PostgreSQL наружу не открыт: база доступна только API внутри Docker-сети.
