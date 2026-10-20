# Quest Launcher

Лаунчер для игровых Discord Quests без необходимости скачивать сами игры.

Некоторые квесты Discord требуют запустить определённую игру.Quest Launcher создаёт и запускает EXE-копии с выбранными именами, и Discord определяет их как игру.

[![Downloads](https://img.shields.io/github/downloads/NekoPoint/QuestLauncher/total?style=flat-square&color=blue&label=Downloads)](https://github.com/NekoPoint/QuestLauncher/releases)
[![Latest Release](https://img.shields.io/github/v/release/NekoPoint/QuestLauncher?style=flat-square&label=Release)](https://github.com/NekoPoint/QuestLauncher/releases/latest)
![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-blue?style=flat-square)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

## Скачать

Скачайте архив из [последнего релиза](https://github.com/NekoPoint/QuestLauncher/releases/latest), распакуйте его в папку с правами на запись и запустите `QuestLauncher.exe`.

Установка не требуется.

## Как пользоваться

1. Найдите в Discord квест, связанный с запуском игры.
2. Укажите имя исполняемого файла игры (например, в Google), например `Endfield.exe`.
3. Выберите папку назначения и запустите.
4. Проверьте прогресс квеста в Discord.

Созданные копии сохраняются в библиотеке лаунчера и доступны для повторного запуска.

> **Важно:** Quest Launcher не запускает оригинальную игру и не гарантирует выполнение квестов. Учитывает ли Discord запущенный процесс, зависит от конкретного задания и механизма распознавания игр. Секундомер лаунчера показывает только время работы процесса.

## Возможности

- Создание и запуск EXE-копий с выбранными именами.
- Библиотека копий, избранное, недавнее и другие функции.
- Импорт уже созданных файлов.
- Обновление отдельных копий или всей библиотеки.
- Просмотр времени работы запущенного процесса.
- Портативный запуск без установки.

## Требования

- Windows 10 или 11.
- .NET Framework 4.8.

.NET SDK для запуска не нужен.

## Обновление

1. Закройте лаунчер и запущенные копии.
2. Скачайте новую сборку из [Releases](https://github.com/NekoPoint/QuestLauncher/releases).
3. Замените основной EXE, сохранив `launcher.cfg`, `QuestLauncher.library` и папки копий (и другие файлы).

Старые копии можно импортировать и обновить через **«…» → «Обновить все»**.

Если обновление прервалось, сохраните файлы `.qlbak` для восстановления.

## Сборка из исходников

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Для сборки нужен компилятор .NET Framework 4.x (`csc.exe`). Скрипт собирает приложение и тестовый раннер, запускает проверки и создаёт контрольные суммы. Результат сохраняется в `dist/`.

Параметр `-SkipTests` пропускает проверки. Обычная сборка не подписана Windows Authenticode.

## Структура проекта

```text
src/                    Исходный код
tests/                  Тесты
assets/lucide/          Иконки и ресурсы
.github/workflows/      CI
QuestLauncher.csproj    Проект .NET Framework
build.ps1               Скрипт сборки
CHANGELOG.md            История изменений
```

Настройки, библиотека и журнал хранятся рядом с EXE, а если папка недоступна для записи — в `%LOCALAPPDATA%\QuestLauncher`.

## Лицензия

Проект распространяется по лицензии [MIT](LICENSE). Автор — **vaelit**.

Иконки — [Lucide](https://github.com/lucide-icons/lucide). Условия лицензирования указаны в [LUCIDE-LICENSE.txt](LUCIDE-LICENSE.txt).
