# Maintainer notes (не для README)

Внутренние заметки по сборке, релизам и архитектуре. Публичный обзор — в корневом [README.md](../README.md).

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

### Выложить проверенный Release на GitHub (без пересборки)

1. В Visual Studio: **Release** → собрать → проверить руками.
2. Двойной клик по `upload-release.bat` в корне репо (или из cmd):

```bat
upload-release.bat
```

Скрипт возьмёт уже собранное из  
`WpfApp_BackupSaves\WpfApp_BackupSaves\bin\Release\net9.0-windows*\`,  
прочитает версию из `BackupSaves.exe`, сделает `releases\BackupSaves-<ver>.zip`  
и опубликует Release в [digitallez/BackupSaves](https://github.com/digitallez/BackupSaves/releases)  
(`gh auth login` один раз).

Только zip без upload: `upload-release.bat -NoUpload`

Альтернатива со сборкой через `dotnet publish`: `.\publish-release.ps1 -UploadToReleasesRepo`.

При старте GUI приложение само проверяет latest release и предлагает обновиться.

## Запуск

- GUI: `BackupSaves.exe`
- Планировщик / CLI: `BackupSaves.exe --backup <guid-профиля>`

Настройки и логи:

- `%LocalAppData%\BackupSaves\logs\yyyy-MM-dd.log` (кнопка **Логи** в статус-баре)
- `%LocalAppData%\BackupSaves\logs\update-apply.log` — лог автообновления
- `%AppData%\BackupSaves\settings.json`

Архивы: `{BackupRoot}/{slug}/{slug}_yyyy-MM-dd_HH-mm-ss.7z|.zip`

## Архитектура

```
BackupSaves.Core        — ZIP/7z, restore, retention, settings, лог
BackupSaves.Scheduler   — CRUD задач Windows Task Scheduler
WpfApp_BackupSaves      — UI, tray, live-список, CLI entry
```

UI и Scheduler не пишут архивы напрямую — только через Core.

Подробности: [DEV-PLAN.md](DEV-PLAN.md), [TZ-SHORT.md](TZ-SHORT.md).

## Статус MVP

По [плану](DEV-PLAN.md) этапы 0–6 и DoD закрыты. Рекомендуемая установка: `%LocalAppData%\BackupSaves` (без Program Files — автообновление без UAC).

## Вне scope (намеренно)

Cloud/NAS, VSS, инкрементальные бэкапы, автодетект игр, шифрование.
