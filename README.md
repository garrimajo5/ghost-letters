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

## Режим зрителя

На главной выберите «Зритель» при входе по коду или «Смотреть идущие партии»
для выбора чужой игры. Подключиться можно до старта и во время партии.
В лобби зрители перечислены отдельно: они не занимают места, не отмечают
готовность, не получают роли и не участвуют в рейтинге.

Зритель видит поле, открытые подсказки, публичные голоса и общий чат (включая
голосовые сообщения). До общей развязки скрыты роли всех, кроме Призрака,
истинные улики, руки, закрытые письма, подсказки Сообщника и командный чат.
После объявления результата открываются те же публичные итоги, что детективам.
Писать в чат, ходить и голосовать зритель не может; «Завершить просмотр» в меню
партии удаляет его из лобби.

API: `GET /api/v1/lobbies/watchable` возвращает до 50 последних активных чужих
партий (без состояния и ролей); `POST /api/v1/lobbies/{code}/join` принимает
`{"mode":"spectator"}`. Права проверяются на сервере для HTTP, SignalR и аудио.
Существующий режим `table` сохранён. Миграция базы не требуется.

## Карты

Улики нарезаются из спрайт-листов папки `Resource`:

```bash
pip install pillow
python tools/cards/cut_cards.py --resource "D:/AI/GhostLetters/Resource" --out client/assets/cards
```

Каталог листов и сеток — `tools/cards/sheets.json`. Пока все карты попадают в набор «Оригинальный».

## Характеры и объяснения ботов

В кабинете ботов «Память» задаёт глубину истории: 0 — только текущая партия,
1 — все завершённые совместные партии. Каждые 20 более свежих партий уменьшают
вес старого воспоминания вдвое. Промежуточные значения ограничивают окно истории.
Прошлые роли дают боту предубеждение, а не знание текущей роли другого игрока.

«Внимание к мелким деталям»: 0 — общий образ, 1 — детали важнее общего образа.
Теги главных предметов остаются в `tags.json`, детали с весами и русскими подписями —
в `details.json`. Для 24 карт вручную проверены мелкие предметы и узоры;
для всего каталога выделены небольшие цветовые участки (4–18% предмета).
Это эвристическая разметка, а не распознавание скрытых объектов на каждой карте.
Пересборка: `python tools/cards/detail_tags.py client/assets/cards`
(зависимости: Pillow, numpy, scipy); ручные дополнения — `details_manual.json`.

Бот объясняет проверку письмом, признаки сходства, результат и текущую версию,
затем может один раз за раунд ответить союзнику и предложить совместное голосование.
Подозрения основаны на доступных ему подсказках, публичных репликах и голосах.
Повтор одного совета не увеличивает его вес; проверка карты сама по себе не считается советом.

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

## Клиент (Flutter)

Код — в `client/`. Платформенные папки (`android/`, `ios/`) создаются workflow «Flutter scaffold».

Локальный запуск на эмуляторе Android:

```bash
docker compose up -d --build        # сервер и база на http://localhost:8080
cd client
flutter pub get
flutter run                          # эмулятор видит компьютер по адресу 10.0.2.2
```

Подробно — запуск, несколько игроков, отладка и частые проблемы: [docs/local-dev.md](docs/local-dev.md).

Другой адрес сервера (например, телефон в той же Wi-Fi сети):
`flutter run --dart-define=API_URL=http://192.168.1.10:8080`.
