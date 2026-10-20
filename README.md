Quest Launcher

Небольшой Windows-лаунчер для выполнения игровых Discord Quests без скачивания самих игр.

Необязательно устанавливать игру на десятки гигабайт ради одного задания. Quest Launcher позволяет создавать и запускать EXE-копии с нужными именами, которые Discord в некоторых случаях может распознать как запущенную игру.

""Downloads" (https://img.shields.io/github/downloads/NekoPoint/QuestLauncher/total?style=flat-square&color=blue&label=Downloads)" (https://github.com/NekoPoint/QuestLauncher/releases)
""Latest Release" (https://img.shields.io/github/v/release/NekoPoint/QuestLauncher?style=flat-square&label=Release)" (https://github.com/NekoPoint/QuestLauncher/releases/latest)
"Windows" (https://img.shields.io/badge/Windows-10%20%2F%2011-blue?style=flat-square)
"License" (https://img.shields.io/badge/License-MIT-green?style=flat-square)

Скачать

Актуальная версия доступна на странице "последнего релиза" (https://github.com/NekoPoint/QuestLauncher/releases/latest).

Скачайте архив с лаунчером, распакуйте его в отдельную папку и запустите "QuestLauncher.exe". Установка не требуется.

Использование

1. Откройте Discord и найдите квест, связанный с запуском игры.
2. Укажите в Quest Launcher имя исполняемого файла нужной игры, например "Endfield.exe".
3. Выберите папку и нажмите «Создать и запустить».
4. Проверьте прогресс квеста в Discord.

Созданные файлы автоматически добавляются в библиотеку и доступны для повторного запуска.

Обратите внимание: Quest Launcher не гарантирует выполнение всех квестов. Результат зависит от того, как Discord распознаёт запущенные игры. Секундомер в лаунчере показывает время работы процесса, а не подтверждённый прогресс задания.

Возможности

- Создание и запуск EXE-копий под выбранным именем.
- Работа без установки оригинальных игр.
- Библиотека созданных файлов.
- Поиск, фильтры и избранное.
- Импорт существующих копий.
- Обновление отдельных файлов и всей библиотеки.
- Отслеживание времени работы процессов.
- Портативный запуск без установки.

Требования

- Windows 10/11
- .NET Framework 4.8

Обновление

Скачайте актуальную сборку из "Releases" (https://github.com/NekoPoint/QuestLauncher/releases).

Перед заменой файлов закройте лаунчер и запущенные копии. Сохраните "launcher.cfg", "QuestLauncher.library" и папки с созданными файлами.

Если обновление было прервано, не удаляйте файлы ".qlbak" до завершения восстановления.

Сборка из исходников

powershell -ExecutionPolicy Bypass -File .\build.ps1

Требуется компилятор .NET Framework 4.x ("csc.exe").

Скрипт собирает приложение, выполняет предусмотренные проектом проверки и сохраняет результат в "dist/".

Для пропуска проверок используйте "-SkipTests".

Лицензия

Quest Launcher распространяется под лицензией "MIT" (LICENSE).

Иконки взяты из "Lucide" (https://github.com/lucide-icons/lucide). Условия их использования приведены в "LUCIDE-LICENSE.txt" (LUCIDE-LICENSE.txt).

---

Разработчик: vaelit.