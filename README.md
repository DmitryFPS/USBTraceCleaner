# USB Trace Cleaner

Утилита для удаления следов USB-устройств в **Windows 10** и **Windows 11**.

Аналог USBOblivion с современным интерфейсом, режимом симуляции и расширенным покрытием артефактов Win10/11.

## Возможности

- **Режим симуляции** — показывает все найденные следы без изменений
- **Резервная копия** — экспорт `.reg` перед очисткой
- **Точка восстановления** — опционально перед очисткой
- **Выборочная очистка** — галочки на каждый элемент
- **Фильтр по категориям** — реестр, логи, файлы, события

## Что очищается

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

```powershell
cd USBTraceCleaner
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false
```

Запускайте **от имени администратора**.

Портативная версия — один файл `USBTraceCleaner.exe` (~68 МБ). .NET на целевом ПК **не нужен**.

При очистке резервная копия сохраняется **рядом с exe**:
- `USBTraceCleaner_backup_ГГГГ-ММ-ДД_ЧЧ-мм-сс.reg`
- `USBTraceCleaner_manifest_ГГГГ-ММ-ДД_ЧЧ-мм-сс.txt` (список удалённого)

## Важно

1. Отключите все USB-накопители перед очисткой
2. После реальной очистки **перезагрузите** Windows
3. Не используйте на Windows, установленной на USB-диске
