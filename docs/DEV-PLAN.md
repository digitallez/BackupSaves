# BackupSaves — план разработки

## Цели MVP
1. Профили бэкапа: имя, список источников (файлы/папки), папка назначения, расписание, retention.
2. Ручной бэкап → локальный ZIP или 7z.
3. Расписание через Task Scheduler → тот же код без UI (`--backup <profileId>`).
4. Открытый UI видит новые архивы **в реальном времени** (в т.ч. от Scheduler).
5. Restore «в один клик» по всем путям профиля из выбранного архива.
6. История запусков + понятные ошибки.
7. Трей (опционально toast).

## Вне MVP
Cloud/NAS, VSS, инкременты, автодетект игр, шифрование, миграция на WinUI/Avalonia.

## Слои
| Слой | Ответственность |
|------|-----------------|
| Core | Сбор файлов, ZIP/7z, restore, retention, модели |
| Scheduler | CRUD задач Task Scheduler, CLI-запуск |
| App (WPF) | UI, tray, live-список архивов |

## Данные
```
%AppData%/<AppName>/settings.json
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
0. Каркас слоёв + settings  
1. ZIP/7z + manifest + CLI `--backup`  
2. CRUD профилей + ручной бэкап  
3. One-click restore  
4. Task Scheduler  
5. Live FSW + tray  
6. Полировка  

## DoD
- [ ] Несколько путей → ZIP/7z с манифестом  
- [ ] Scheduler создаёт архив при закрытом UI  
- [ ] Открытый UI показывает архив без F5  
- [ ] One-click restore во все исходные папки  
- [ ] Retention после успеха  
- [ ] Понятная ошибка «файл занят»  
- [ ] Headless exit codes корректны  
