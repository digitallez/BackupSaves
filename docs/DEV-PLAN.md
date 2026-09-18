# BackupSaves — план разработки

## Цели MVP
1. Профили бэкапа: имя, список источников (файлы/папки), папка назначения, расписание, retention.
2. Ручной бэкап → локальный ZIP или 7z.
3. Расписание через Task Scheduler → тот же код без UI (`--backup <profileId>`).
4. Открытый UI видит новые архивы **в реальном времени** (в т.ч. от Scheduler).
5. Restore «в один клик» по всем путям профиля из выбранного архива.
6. История запусков + понятные ошибки.
7. Трей (опционально toast — пока без toast).

## Вне MVP
Cloud/NAS, VSS, инкременты, автодетект игр, шифрование, миграция на WinUI/Avalonia.
UAC/инсталлятор в Program Files — не требуется (рекомендуемая установка: `%LocalAppData%\BackupSaves`).

## Слои
| Слой | Ответственность |
|------|-----------------|
| Core | Сбор файлов, ZIP/7z, restore, retention, модели, лог |
| Scheduler | CRUD задач Task Scheduler, CLI-запуск |
| App (WPF) | UI, tray, live-список архивов, автообновление |

## Данные
```
%AppData%\BackupSaves\settings.json
%LocalAppData%\BackupSaves\logs\yyyy-MM-dd.log
%LocalAppData%\BackupSaves\logs\update-apply.log
%LocalAppData%\BackupSaves\updates\          (скачанные zip обновлений)
BackupRoot/<ProfileSlug>/<slug>_yyyy-MM-dd_HH-mm-ss.zip|.7z
```
В архиве обязательно `manifest.json` + данные под `data/...`. Формат: ZIP или 7z.

## Live UI
FileSystemWatcher (`*.zip`, `*.7z`) + debounce + polling 5–10 с; старт = полная перечитка; marshal на Dispatcher.

## Backup
`*.tmp` → atomic rename → retention только после успеха. Locked files: retry, затем fail + лог.

## Restore
Только по манифесту; confirm overwrite; best-effort + отчёт; запрет path traversal.

## Этапы
- [x] 0. Каркас слоёв + settings
- [x] 1. ZIP/7z + manifest + CLI `--backup`
- [x] 2. CRUD профилей + ручной бэкап
- [x] 3. One-click restore
- [x] 4. Task Scheduler
- [x] 5. Live FSW + tray
- [x] 6. Полировка (тема, title bar, логи, GitHub Releases, автообновление)

## DoD
- [x] Несколько путей → ZIP/7z с манифестом
- [x] Scheduler создаёт архив при закрытом UI
- [x] Открытый UI показывает архив без F5
- [x] One-click restore во все исходные папки
- [x] Retention после успеха
- [x] Понятная ошибка «файл занят»
- [x] Headless exit codes корректны
- [x] Логи в `%LocalAppData%\BackupSaves\logs` + кнопка «Логи» в UI
- [x] Публикация релиза (`upload-release.bat`) и автообновление из `BackupSaves-Releases`
