# Письма призрака

Мобильная версия настольной игры «Письма призрака» (с рядом «Тайна» из дополнения) для игры с друзьями: Android и iPhone, онлайн или за одним столом, живая или походовая партия.

| Часть | Технологии | Папка |
| --- | --- | --- |
| Сервер | ASP.NET Core 8, SignalR, EF Core, PostgreSQL 16 | `server/` |
| Клиент | Flutter (этап 2) | `client/` |
| Инструменты | Нарезка карт из спрайт-листов (Python + Pillow) | `tools/cards/` |

Документы проекта: UX-концепт (этап 0) и архитектура, схема БД и API (этап 00) — в проекте «Письма призрака» в claude.ai.

## Запуск локально

Нужен Docker Desktop.

```bash
docker compose up --build
```

- API: http://localhost:8080 — `GET /health`, `GET /api/v1/version`, `GET /api/v1/rules/roles?players=7`
- PostgreSQL: `localhost:5432`, база `ghost_letters`, пользователь и пароль `ghost`

Без Docker (нужен .NET 8 SDK и запущенный PostgreSQL):

```bash
cd server
dotnet run --project src/GhostLetters.Api   # Swagger: http://localhost:5xxx/swagger
```

## Тесты

```bash
cd server && dotnet test
python -m unittest discover -s tools/cards -p "test_*.py"
```

CI (GitHub Actions) собирает сервер, гоняет тесты, собирает Docker-образ и тестирует инструменты на каждый PR.

## Карты

Улики нарезаются из спрайт-листов папки `Resource`:

```bash
pip install pillow
python tools/cards/cut_cards.py --resource "D:/AI/GhostLetters/Resource" --out client/assets/cards
```

Каталог листов и сеток — `tools/cards/sheets.json`. Пока все карты попадают в набор «Оригинальный».

## Структура сервера

```
server/src/GhostLetters.Domain          правила игры, без зависимостей
server/src/GhostLetters.Application     сценарии: лобби, партия, итоги
server/src/GhostLetters.Infrastructure  PostgreSQL, файлы, пуши
server/src/GhostLetters.Api             REST + SignalR
server/tests/...                        unit- и интеграционные тесты
```

## Миграции БД

Миграции EF Core генерируются в GitHub Actions: запишите имя миграции в `server/ef-migration.request`
и запушьте ветку — workflow «EF migration» создаст миграцию и закоммитит её в ту же ветку.
Локально с установленным .NET SDK можно и напрямую:

```bash
cd server
dotnet ef migrations add <Name> --project src/GhostLetters.Infrastructure --startup-project src/GhostLetters.Api --output-dir Persistence/Migrations
```

При запуске в окружении Development (и в docker compose) миграции применяются автоматически.

## Вход

- `POST /api/v1/auth/guest` `{deviceId, nickname, avatarColor?}` → `{accessToken, accessTokenExpiresAt, refreshToken, user}`
- `POST /api/v1/auth/refresh` `{refreshToken}` — новая пара токенов, старый refresh отзывается
- `POST /api/v1/auth/logout` `{refreshToken}`
- `GET /api/v1/me`, `PATCH /api/v1/me` `{nickname?, avatarColor?}` — с `Authorization: Bearer <accessToken>`

Ключ подписи — `Jwt:SigningKey` (не короче 32 символов). В Development задан тестовый ключ, в продакшене — только через секреты.
