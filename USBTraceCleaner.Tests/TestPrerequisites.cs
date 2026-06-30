namespace USBTraceCleaner.Tests;

internal static class TestPrerequisites
{
    public static bool IsAdmin => USBTraceCleaner.Services.AdminHelper.IsAdministrator();

    public static bool HasUsbStor =>
        USBTraceCleaner.Services.RegistryHelper.CountUsbStorDevicesAllControlSets() > 0;

    /// <summary>
    /// Интеграционный тест реальной очистки: права админа + записи USBSTOR в реестре.
    /// </summary>
    public static bool CanRunDestructiveUsbStorTest => IsAdmin && HasUsbStor;
}
