# USB Trace Cleaner

Утилита для поиска и удаления следов USB-устройств в **Windows 10** и **Windows 11**.

Аналог USBOblivion с тремя вкладками, режимом симуляции, аудитом сети и встроенной базой VID/PID.

**Версия:** 1.6.0

## Возможности

### Вкладка «USB-следы»
- Сканирование реестра, логов, журналов событий и файловой системы
- Колонки **VID**, **PID**, **производитель**, **модель**, **первое подключение**, **источник**
- Распознавание путей `Enum\USB`, `USBSTOR`, `usbflags`, `DeviceMigration` и др.
- Встроенная база **USBVendors.txt** (~3400 производителей, ~20000 продуктов)
- Фильтр по группам: USB-накопители, реестр, логи, призраки PnP
- Режим симуляции, резервная копия `.reg`, точка восстановления
- Выборочная очистка с галочками на каждый элемент

### Вкладка «Другие USB-следы»
- Дополнительные артефакты вне основного сканера (BAM, ShellBags, SetupAPI и т.д.)
- Подстановка производителя/модели по VID/PID из базы

### Вкладка «Аудит сети»
- Wi‑Fi профили, DNS-кэш, NLA, VPN, hosts, SRU, журналы сетевых событий
- Очистка выбранных следов с описанием последствий
- Экспорт отчёта в PDF

### Общее
- Экспорт PDF-отчётов по USB и сетевому аудиту
- Запуск от имени администратора (обязательно для очистки)

## Что очищается (USB)

### Реестр (SYSTEM)
- `Enum\USB`, `Enum\USBSTOR`, `Enum\SWD\WPDBUSENUM`
- `Control\DeviceContainers`, `Control\DeviceClasses`
- `Control\usbflags`, `Control\usbstor`, `Control\DeviceMigration`
- `Services\USBSTOR\Enum`, ReadyBoost
- `MountedDevices` (тома USB)
- `Windows Search\VolumeInfoCache`
- `Windows Portable Devices\Devices`
- BAM/DAM (запуск программ с съёмных дисков)

### Реестр (пользователи)
- `MountPoints2`, `CPC\Volume`
- AutoplayHandlers, Portable Devices
- ShellBags, MuiCache, UserAssist

### Файлы
- `setupapi.dev.log`, `setupapi.ev*`
- `INFCACHE.1`, PCA-логи Win11
- Recent `.lnk`, Jump Lists

### Журналы событий
- DeviceSetupManager, Kernel-PnP, DriverFrameworks-UserMode
- Storage-ClassPnP, WPD-MTPClassDriver, Partition/Diagnostic

## Сборка

Требуется [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Команды ниже выполняйте из **корня репозитория** (где `USBTraceCleaner.sln`).

### Обычная сборка (нужен установленный .NET 8 на целевом ПК)

```powershell
dotnet publish USBTraceCleaner\USBTraceCleaner.csproj -c Release -r win-x64 --self-contained false
```

Результат: `USBTraceCleaner\bin\Release\net8.0-windows\win-x64\publish\USBTraceCleaner.exe`

### Портативный exe (один файл, .NET на целевом ПК не нужен)

```powershell
dotnet publish USBTraceCleaner\USBTraceCleaner.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

Результат: `USBTraceCleaner\bin\Release\net8.0-windows\win-x64\publish\USBTraceCleaner.exe` (~80 МБ).

Запускайте **от имени администратора**.

> Готовый exe в репозиторий не включается — соберите локально командами выше.

При очистке резервная копия сохраняется **рядом с exe**:
- `USBTraceCleaner_backup_ГГГГ-ММ-ДД_ЧЧ-мм-сс.reg`
- `USBTraceCleaner_manifest_ГГГГ-ММ-ДД_ЧЧ-мм-сс.txt` (список удалённого)

## Тесты

```powershell
dotnet test USBTraceCleaner.Tests/USBTraceCleaner.Tests.csproj -c Release `
  /p:CollectCoverage=true --settings coverlet.runsettings
```

Покрытие строк — **≥90%** для тестируемой логики (модели, классификаторы, база VID/PID, отчёты).
Код, требующий прав администратора, UI WPF и прямого доступа к ОС, помечен `[ExcludeFromCodeCoverage]`.

## Важно

1. Отключите все USB-накопители перед очисткой
2. После реальной очистки **перезагрузите** Windows
3. Не используйте на Windows, установленной на USB-диске

## Лицензия

См. файл [LICENSE](LICENSE).
