@echo off
rem Только сервер: пересобрать и запустить (или перезапустить после правок в server/).
chcp 65001 >nul
cd /d "%~dp0.."
docker compose up -d --build || (pause & exit /b 1)
docker compose ps
echo Логи сервера: docker compose logs -f api
