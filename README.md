# USB Trace Cleaner

Утилита для поиска и удаления следов USB-устройств в **Windows 10** и **Windows 11**.

Аналог USBOblivion с четырьмя вкладками, режимом симуляции, аудитом сети и встроенной базой VID/PID.

**Версия:** 1.7.1

## Возможности

### Вкладка «USB»
- Сканирование реестра, логов, журналов событий и файловой системы
- Колонки **VID**, **PID**, **производитель**, **модель**, **первое подключение**, **источник**
- Распознавание путей `Enum\USB`, `USBSTOR`, `usbflags`, `DeviceMigration` и др.
- Встроенная база **USBVendors.txt** (~3400 производителей, ~20000 продуктов)
- Фильтр по группам: USB-накопители, реестр, логи, призраки PnP
- **Режим симуляции** (галочка «Симуляция (без удаления)»), резервная копия `.reg`, точка восстановления (по умолчанию выкл.)
- Выборочная очистка с галочками на каждый элемент
- Расширенная forensic-очистка: Prefetch, Amcache, Shimcache, Jump Lists, Explorer MRU, Recycle Bin, VSS, System log, self-trace
- Критерий успеха очистки: счётчик следов **USBSTOR** = 0 (встроенные `Enum\USB` хабы намеренно остаются)

### Вкладка «Другие USB-следы»
- Дополнительные артефакты вне основного сканера (не-storage usbflags, DeviceMigration, Setup\Upgrade)
- Подстановка производителя/модели по VID/PID из базы

### Вкладка «Аудит сети»
- Wi‑Fi профили, DNS-кэш, NLA, VPN, hosts, SRU, журналы сетевых событий
- Белый список IP / Wi‑Fi / VPN: элементы **не удаляются** при выборочной и максимальной очистке
- Режим симуляции, очистка выбранных следов с описанием последствий
- Экспорт отчёта в PDF

### Вкладка «Журналы событий»
- Просмотр каналов Event Viewer и очистка выбранных (`wevtutil cl`)
- Журнал «Безопасность» — только по отдельной галочке
- Опциональный purge `System.evtx` (Event ID 104)

### Общее
- Экспорт PDF-отчётов по USB и сетевому аудиту
- Запуск от имени администратора (обязательно для очистки)
- CLI: `--clean` — полный forensic-путь (скан + очистка, включая журналы)
- Инженерное руководство PDF рядом с EXE после сборки

## Что очищается (USB)

### Реестр (SYSTEM)
- `Enum\USB`, `Enum\USBSTOR`, `Enum\SWD\WPDBUSENUM`
- `Control\DeviceContainers`, `Control\DeviceClasses`
- `Control\usbflags` (включая orphan / IgnoreHWSerNum / полный wipe)
- `Control\usbstor`, `Control\DeviceMigration`
- `Services\USBSTOR\Enum`, ReadyBoost
- `MountedDevices` (тома USB)
- `Windows Search\VolumeInfoCache`
- `Windows Portable Devices\Devices`
- BAM/DAM (съёмные буквы + пути утилит очистки)
- AppCompatCache / Shimcache (при наличии USB/self-trace)

### Реестр (пользователи)
- `MountPoints2`, `CPC\Volume`
- AutoplayHandlers, Portable Devices
- ShellBags, MuiCache
- UserAssist (только USB/self-trace значения, не весь ключ)
- RecentDocs, TypedPaths, OpenSavePidlMRU, LastVisitedPidlMRU

### Файлы
- `setupapi.dev.log`, `setupapi.ev*`
- `INFCACHE.1`, PCA-логи Win11
- Recent `.lnk` (USB + self-trace)
- Jump Lists: `AutomaticDestinations`, `CustomDestinations` (только с USB/self-содержимым)
- Prefetch (`*.pf` по USB/утилитам)
- Amcache.hve (scrub Inventory)
- `$Recycle.Bin` ($I/$R с USB-путями)
- Volume Shadow Copies (vssadmin delete shadows)

### Журналы событий
- DeviceSetupManager, Kernel-PnP, DriverFrameworks-UserMode
- Storage-ClassPnP, WPD-MTPClassDriver, Partition/Diagnostic
- UserPnp/DeviceInstall, UserPnp/Operational
- **System** (UserPnp USBSTOR + повторная очистка Event ID 104)

### Self-trace
- Prefetch/BAM/Recent/Jump Lists для USBTraceCleaner, USBOblivion, USBDeview, USBDetector, UsbForensicAudit
- Временные `USBTC_*` в Temp

## Документация

| Файл | Назначение |
|------|------------|
| `docs/USBTraceCleaner_Инженерное_руководство.html` | Исходник инженерного руководства |
| `docs/USBTraceCleaner_Инженерное_руководство.pdf` | PDF для распространения рядом с EXE |
| `СБОРКА.txt` | Краткая инструкция по сборке |
| `build-exe.ps1` | Portable publish + генерация/копирование руководства |

## Сборка

Требуется [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Команды ниже выполняйте из **корня репозитория** (где `USBTraceCleaner.sln`).

### Рекомендуемый способ (portable + руководство)

```powershell
.\build-exe.ps1
```

Результат в `USBTraceCleaner\bin\Release\net8.0-windows\win-x64\publish\`:
- `USBTraceCleaner.exe`
- `USBTraceCleaner_Инженерное_руководство.pdf`

### Портативный exe вручную

```powershell
dotnet publish USBTraceCleaner\USBTraceCleaner.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

Цель MSBuild `CopyEngineeringGuide` копирует PDF из `docs\` рядом с EXE при `Build`/`Publish`.

Запускайте **от имени администратора**.

> Готовый exe в репозиторий не включается — соберите локально командами выше.

При очистке резервная копия сохраняется **рядом с exe**:
- `USBTraceCleaner_backup_ГГГГ-ММ-ДД_ЧЧ-мм-сс.reg`
- `USBTraceCleaner_manifest_ГГГГ-ММ-ДД_ЧЧ-мм-сс.txt` (список удалённого)

### Headless

```powershell
.\USBTraceCleaner.exe --clean
```

Полный скан + очистка (журналы, VSS, Prefetch, MRU, self-trace). Без авто-перезагрузки.

Другие CLI:
- `--restore-logs` — восстановить `setupapi.dev.log`
- `--fix-duplicates` — удаление PnP ghosts/duplicates
- `--self-test` — встроенная диагностика

## Тесты

```powershell
dotnet test USBTraceCleaner.Tests/USBTraceCleaner.Tests.csproj -c Release `
  /p:CollectCoverage=true --settings coverlet.runsettings
```

Покрытие строк — **≥90%** для тестируемой логики (модели, классификаторы, база VID/PID, отчёты, ForensicTracePatterns).
Код, требующий прав администратора, UI WPF и прямого доступа к ОС, помечен `[ExcludeFromCodeCoverage]`.

## Важно

1. Отключите все USB-накопители перед очисткой
2. После реальной очистки **перезагрузите** Windows
3. Не используйте на Windows, установленной на USB-диске
4. Удаление VSS уничтожает все теневые копии тома
5. Очистка журнала System удаляет и несвязанные события; повторный `wevtutil cl` снова может создать Event ID 104
6. Встроенные USB (хабы, камера, BT) останутся в `Enum\USB` — это не следы флешек
7. Перед юридически значимым исследованием сначала снимите forensic-образ / прогоните UsbForensicAudit

## Лицензия

См. файл [LICENSE](LICENSE).
