# BackupSaves

Локальный бэкап сейвов игр на **Windows 10+**.

Архивы **ZIP / 7z** на диск, профили с несколькими источниками, расписание через **Task Scheduler**, restore в один клик, трей.

## Возможности

- Профили: источники (файлы/папки), папка бэкапов, retention, расписание, формат ZIP или 7z
- Ручной бэкап и headless: `BackupSaves.exe --backup <profileId>`
- Task Scheduler или автобэкап из GUI (пока приложение открыто / в трее)
- Восстановление
- Трей, история, логи, автообновление из GitHub Releases

## Установка

Готовые сборки: [Releases](https://github.com/digitallez/BackupSaves/releases).

Рекомендуемый путь: `%LocalAppData%\BackupSaves` (без Program Files — обновления без UAC).

Требования: Windows 10 (17763+) / 11. Для сборки из исходников — [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
cd WpfApp_BackupSaves
dotnet build WpfApp_BackupSaves.sln -c Release
```

## Использование

| Режим | Команда |
|---|---|
| GUI | `BackupSaves.exe` |
| Планировщик / CLI | `BackupSaves.exe --backup <profileId>` |

Настройки: `%AppData%\BackupSaves\settings.json`  
Логи: `%LocalAppData%\BackupSaves\logs\`  
Архивы: `{BackupRoot}/{slug}/{slug}_yyyy-MM-dd_HH-mm-ss.7z|.zip`

## Лицензия

[MIT](LICENSE)
