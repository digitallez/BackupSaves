# BackupSaves — короткое ТЗ (копировать в чат)

```
Решение: WpfApp_BackupSaves (WPF). Правила: .cursor/rules/ + docs/DEV-PLAN.md

Сделать утилиту бэкапа сейвов игр на Windows 10+.

Стек (фиксирован):
- UI: WPF (Fluent/WPF-UI ок)
- Архив: локальный ZIP или 7z
- Расписание: Windows Task Scheduler + headless CLI
  App.exe --backup <profileId>  (exit 0 / ≠0)

Функции MVP:
1) Профили: имя, несколько источников (файлы/папки), BackupRoot, retention N, schedule, формат ZIP|7z
2) Ручной бэкап → архив с manifest.json (sourcePath + archivePath); atomic *.tmp → rename
3) Task Scheduler создаёт архивы при закрытом UI
4) Если UI открыт — новые архивы видны в реальном времени (FileSystemWatcher *.zip/*.7z + polling fallback)
5) Restore «в один клик»: все пути из манифеста в исходные папки, confirm overwrite, отчёт ошибок
6) Трей, история success/fail

Вне scope: cloud/NAS, VSS, инкременты, автодетект игр, смена UI-фреймворка.

Архитектура: Core (ZIP/7z + restore) | Scheduler | App(WPF). UI не пишет архивы напрямую.

Порядок: каркас → ZIP/7z+CLI → профили UI → restore → Scheduler → live+tray → полировка.
Сначала предложи структуру папок/проектов и схемы settings.json / manifest.json, затем код.
```
