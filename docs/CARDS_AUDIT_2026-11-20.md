# Проверка разметки карт — 20 ноября 2026

Просмотрены обзорные листы всех 761 изображений четырёх локальных наборов; подозрительные предметы дополнительно рассмотрены крупнее. Это обзор с точечными исправлениями, а не новая исчерпывающая разметка каждой мелкой детали. Серверные административные переопределения и кастомные наборы не проверялись.

Исправлены 23 карты. Меняются исходные строки `retag_review.csv`, профили и полученные JSON, поэтому пересборка сохраняет исправления. Изображения, ID, распределение по наборам и правила игры сохранены.

| Карта | Исправление |
| --- | --- |
| `orig_0213` — дискета | profile: `recording` → `data-storage` |
| `orig_0302` — жёсткий диск | profile: `recording` → `data-storage` |
| `orig_0346` — флешка | profile: `recording` → `data-storage` |
| `orig_0375` — принтер | profile: `appliance` → `electrical-tool` |
| `orig_0379` — триммер | profile: `appliance` → `electrical-tool` |
| `orig_0389` — три д принтер | profile: `appliance` → `electrical-tool` |
| `orig_0428` — вентилятор | profile: `appliance` → `electrical-tool` |
| `orig_0484` — компрессор | profile: `appliance` → `electrical-tool` |
| `orig_0550` — стиральная машина | profile: `appliance` → `electrical-tool` |
| `orig_0598` — швейная машинка | profile: `appliance` → `electrical-tool` |
| `orig_0677` — робот пылесос | profile: `appliance` → `electrical-tool` |
| `orig_0696` — шредер | profile: `appliance` → `electrical-tool` |
| `orig_0242` — мангал | features: `metal cook heat hot open-top` → `metal cook open-top` |
| `orig_0442` — камин | profile: `appliance` → `fuel-heater` |
| `orig_0624` — печка | profile: `appliance` → `fuel-heater` |
| `orig_0689` — утюг | features: `metal heavy heat hold-in-hand` → `metal heavy hold-in-hand clothes` |
| `orig_0718` — люлька | profile: `bedding` → `work-platform`; features: `wood child rock` → `climb barrier`; colors: `yellow` → `orange`; association: `family` → `building:0.9` |
| `orig_0463` — скорпион | profile: `insect` → `jewelry`; features: `hard sharp` → `metal hard`; association: `poison` → `poison:0.3` |
| `orig_0076` — паук | profile: `insect` → `arachnid` |
| `orig_0614` — билеты | features: `transport` → `music concert`; association: `travel` → `celebration` |
| `orig_0262` — конверт с деньгами | profile: `money-item` → `paper-money` |
| `orig_0599` — пачка денег | profile: `money-item` → `paper-money` |
| `orig_0680` — электрошокер | profile: `gun` → `hand-tool`; features: `plastic electric` → `plastic electric weapon` |

Основные причины: хранение данных не означает звук; электрический прибор не обязательно кухонный; угольный утюг, мангал и дровяная печь не электрические; бумажные деньги не твёрдые; строительная люлька не детская кровать; подвеска не живое животное; контактный электрошокер не стреляет.

## Проверки

- `test_reviewed_tags.py`: 10/10, включая три новых регрессионных проверки и воспроизводимость всех строк каталога.
- `check_tags.py --structure-only`: 0 ошибок на 761 карте.
- Полные контрольные пары: 17/18 как до изменений. Прежний сбой нунчаки → пистолет сохранён; значения и состав кандидатов не подгонялись. Отчёт `CARDS_RETAG_REPORT.json` пересчитан.
- Полный Python-прогон в текущем Windows runtime ограничен отсутствующим SciPy и отказом доступа к временным каталогам теста нарезки. Профильный прогон разметки завершён успешно.
- Серверный `ReviewedCardCatalogTests` запущен, но результата в этой среде не получено; прохождение C#-проверки не подтверждено.

В рабочем приложении возможны административные переопределения из PostgreSQL поверх этих файлов. Этот аудит меняет репозиторий, а не живую базу.

Проект следующего набора: [«Жижа»](card-sets/zhizha.md).
