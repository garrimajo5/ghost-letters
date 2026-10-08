@echo off
rem Обновить код, пересобрать и запустить сервер, запустить клиент на эмуляторе.
rem Запуск: двойной щелчок или из консоли. Аргументы уходят во flutter run, например: dev.bat -d emulator-5554
rem   dev.bat --no-pull   — не делать git pull (проверить свои локальные правки)
chcp 65001 >nul
setlocal EnableDelayedExpansion
cd /d "%~dp0.."

set PULL=1
set ARGS=
for %%a in (%*) do (
  if /i "%%~a"=="--no-pull" (set PULL=0) else (set ARGS=!ARGS! %%a)
)

if "%PULL%"=="1" (
  echo === Обновляю код из GitHub ===
  git pull --ff-only || goto :error
)

echo === Проверяю Docker ===
docker info >nul 2>&1
if errorlevel 1 (
  echo Docker не запущен — запускаю Docker Desktop...
  start "" "%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
  for /l %%i in (1,1,60) do (
    timeout /t 3 >nul
    docker info >nul 2>&1 && goto :docker_ready
  )
  echo Docker Desktop не запустился за 3 минуты.
  goto :error
)
:docker_ready

echo === Сервер: сборка и запуск ===
docker compose up -d --build || goto :error

echo === Жду, пока сервер ответит ===
for /l %%i in (1,1,40) do (
  curl -fs http://localhost:8080/health >nul 2>&1 && goto :server_ready
  timeout /t 2 >nul
)
echo Сервер не ответил за 80 секунд. Последние логи:
docker compose logs --tail 50 api
goto :error
:server_ready
echo Сервер работает: http://localhost:8080

echo === Клиент ===
cd client
call flutter pub get || goto :error
call flutter run%ARGS%
goto :eof

:error
echo.
echo Что-то пошло не так — смотрите сообщения выше.
pause
exit /b 1
