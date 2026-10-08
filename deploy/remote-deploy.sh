#!/usr/bin/env bash
# Выполняется на сервере (его запускает workflow «Deploy server» по SSH).
# Ставит Docker при первом запуске, создаёт .env с паролями, обновляет и перезапускает сервис.
# Ожидает переменные: DOMAIN, GHCR_USER, GHCR_TOKEN, APP_DIR.
set -euo pipefail
cd "$APP_DIR"

if ! command -v docker >/dev/null 2>&1; then
  echo "Устанавливаю Docker…"
  curl -fsSL https://get.docker.com | sh
fi

# Открыть HTTP/HTTPS, если включён брандмауэр.
if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
  ufw allow 22/tcp >/dev/null; ufw allow 80/tcp >/dev/null; ufw allow 443/tcp >/dev/null
fi

if [ ! -f .env ]; then
  echo "Создаю .env с новыми паролями (хранится только на сервере)"
  umask 077
  cat > .env <<ENV
DOMAIN=$DOMAIN
POSTGRES_PASSWORD=$(openssl rand -hex 24)
JWT_SIGNING_KEY=$(openssl rand -base64 48 | tr -d '\n')
ENV
else
  sed -i "s|^DOMAIN=.*|DOMAIN=$DOMAIN|" .env
fi

echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin >/dev/null
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d --remove-orphans
docker logout ghcr.io >/dev/null
docker image prune -f >/dev/null

# Ежедневная копия базы (хранится 14 дней) — ставится один раз.
if ! (crontab -l 2>/dev/null || true) | grep -q ghost-letters-backup; then
  mkdir -p "$APP_DIR/backups"
  # Пустой crontab — не ошибка (при set -e это валило скрипт на первом запуске).
  { crontab -l 2>/dev/null || true
    echo "30 3 * * * cd $APP_DIR && docker compose -f docker-compose.prod.yml exec -T postgres pg_dump -U ghost ghost_letters | gzip > backups/ghost-\$(date +\%F).sql.gz && find backups -name '*.sql.gz' -mtime +14 -delete # ghost-letters-backup"
  } | crontab - || echo "Не удалось поставить ежедневную копию базы (нет cron?) — сервер при этом работает"

fi

docker compose -f docker-compose.prod.yml ps
