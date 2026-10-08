# Локальный запуск и отладка (этап 3)

Инструкция для Windows: Docker Desktop, Flutter SDK, Android Studio (только ради SDK и эмулятора), VS Code.


## Одной командой (Windows)

`scripts\dev.bat` — двойной щелчок или из консоли в корне репозитория:

1. `git pull` (пропустить: `scripts\dev.bat --no-pull`);
2. запускает Docker Desktop, если он не запущен;
3. `docker compose up -d --build` и ждёт, пока сервер ответит на `/health`;
4. `flutter pub get` и `flutter run` (аргументы передаются дальше: `scripts\dev.bat -d emulator-5554`).

`scripts\server.bat` — только пересобрать и запустить сервер.

Во время `flutter run`: `r` — горячая перезагрузка (правки в экранах), `R` — перезапуск приложения, `q` — выход.
После смены иконки, шрифтов или картинок нужен полный `flutter run` заново.

## 1. Сервер и база

```powershell
cd D:\AI\GhostLetters\ghost-letters      # папка с клоном репозитория
docker compose up -d --build
```

- Поднимутся PostgreSQL 16 (порт 5432) и API (порт 8080).
- Миграции применяются сами при старте. Карты берутся из `client/assets/cards/cards.json`.
- Проверка: откройте http://localhost:8080/health — должно быть `Healthy`.
- Swagger со всеми методами API: http://localhost:8080/swagger.
- Логи сервера: `docker compose logs -f api`.
- Остановить: `docker compose down`. Стереть базу и голосовые: `docker compose down -v`.

После изменений в коде сервера: `docker compose up -d --build api`.

## 2. Клиент на эмуляторе

1. Запустите эмулятор: Android Studio → Device Manager → ▶ (или `flutter emulators --launch <id>`).
2. В VS Code откройте папку `client`, внизу справа выберите устройство `emulator-5554`.
3. Запуск: F5 (отладка, точки останова, hot reload) или в терминале:

```powershell
cd client
flutter pub get
flutter run
```

Эмулятор видит ваш компьютер по адресу `10.0.2.2`, поэтому по умолчанию клиент ходит на `http://10.0.2.2:8080`.

### Несколько игроков на одном компьютере

Нужно хотя бы два «устройства»: каждое — отдельный игрок (идентификатор устройства хранится в приложении).

- Второй эмулятор: Device Manager → создайте ещё один AVD и запустите оба. `flutter run -d all` поставит приложение на все.
- Или Chrome: `flutter run -d chrome --dart-define=API_URL=http://localhost:8080` — веб-версия годится для отладки.
- Настоящий телефон в той же Wi-Fi сети: узнайте IP компьютера (`ipconfig`, IPv4-адрес) и запустите
  `flutter run -d <телефон> --dart-define=API_URL=http://192.168.x.x:8080`. Брандмауэр Windows должен пропускать порт 8080.

Для партии с Убийцей нужно 4 игрока, с Свидетелем — 7. На 2–3 игроках партия кооперативная.

## 3. Отладка

- Ответ сервера с ошибкой приходит в формате problem+json с полем `code` (`VALIDATION`, `PHASE_MISMATCH`, `NOT_YOUR_TURN`…) — клиент показывает текст ошибки внизу экрана.
- Состояние партии целиком лежит в таблице `games` (колонка `state`, JSON). Подключиться к базе: `docker compose exec postgres psql -U ghost -d ghost_letters`.
  - Текущая фаза и дедлайн: `select id, phase, phase_deadline, version from games order by started_at desc limit 5;`
  - Журнал ходов: `select seq, type, payload from game_events where game_id = '...' order by seq;`
- Таймеры фаз в живой партии короткие (60–90 секунд). Для спокойной отладки выберите в настройках лобби «Походовая» — тогда на фазу даются часы.
- Сервер без Docker (если установлен .NET 8 SDK): `cd server/src/GhostLetters.Api && dotnet run` — нужна запущенная база (`docker compose up -d postgres`).

## 4. Тесты

- Сервер: `cd server && dotnet test` (нужен Docker — тесты поднимают PostgreSQL в контейнере).
- Клиент: `cd client && flutter test`.
- В GitHub Actions то же самое запускается на каждый PR, плюс сборка APK и смоук-проверка `docker compose`.

## Частые проблемы

| Симптом | Что сделать |
|---|---|
| «Нет связи с сервером» в приложении | Проверьте http://localhost:8080/health в браузере; для телефона — IP и брандмауэр |
| Эмулятор не стартует | Включите виртуализацию (VT-x/AMD-V) в BIOS и «Платформа низкоуровневой оболочки Windows» |
| Gradle ругается на версию Java | Поставьте JDK 21 и выполните `flutter config --jdk-dir "<путь к JDK>"` |
| `docker compose` не видит `cards.json` | Запускайте команду из корня репозитория |
