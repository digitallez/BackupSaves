# BackupSaves

Утилита для бэкапа сейвов игр на **Windows 10+**.

Локальные архивы **ZIP / 7z**, профили с несколькими источниками, расписание через **Task Scheduler**, restore «в один клик», трей и тёмная тема.

## Возможности

- Профили: имя, источники (файлы/папки), `BackupRoot`, retention N, расписание, формат ZIP|7z (по умолчанию 7z)
- Ручной и headless-бэкап: `BackupSaves.exe --backup <profileId>` (exit `0` / `≠0`)
- `manifest.json` в архиве + atomic запись `*.tmp` → rename
- Live-список архивов (FileSystemWatcher + polling)
- Restore по манифесту с подтверждением перезаписи
- Трей, история запусков, логи

## Требования

- Windows 10 (17763+) / Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Сборка

```powershell
cd WpfApp_BackupSaves
dotnet build WpfApp_BackupSaves.sln -c Release
```

Исполняемый файл:

`WpfApp_BackupSaves\WpfApp_BackupSaves\bin\Release\net9.0-windows10.0.17763.0\BackupSaves.exe`

### Версии

| Конфигурация | Поведение |
|---|---|
| **Debug** | номер не увеличивается, в UI: `1.0.N-debug` |
| **Release** | patch +1 (`1.0.1` → `1.0.2` …), пишется в `Version.props` |

Без инкремента: `dotnet build -c Release -p:NoVersionIncrement=true`

## Запуск

- GUI: `BackupSaves.exe`
- Планировщик / CLI: `BackupSaves.exe --backup <guid-профиля>`

Настройки и логи:

- `%AppData%\BackupSaves\settings.json`
- `%AppData%\BackupSaves\logs\yyyy-MM-dd.log`

Архивы: `{BackupRoot}/{slug}/{slug}_yyyy-MM-dd_HH-mm-ss.7z|.zip`

## Архитектура

```
BackupSaves.Core        — ZIP/7z, restore, retention, settings, лог
BackupSaves.Scheduler   — CRUD задач Windows Task Scheduler
WpfApp_BackupSaves      — UI, tray, live-список, CLI entry
```

UI и Scheduler не пишут архивы напрямую — только через Core.

Подробности: [docs/DEV-PLAN.md](docs/DEV-PLAN.md), [docs/TZ-SHORT.md](docs/TZ-SHORT.md).

## Вне scope (намеренно)

Cloud/NAS, VSS, инкрементальные бэкапы, автодетект игр, шифрование.

## Лицензия

[MIT](LICENSE)
