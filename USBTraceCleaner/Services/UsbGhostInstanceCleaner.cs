namespace USBTraceCleaner.Services;

/// <summary>Обратная совместимость CLI — делегирует в <see cref="PnPGhostScanner"/>.</summary>
public static class UsbGhostInstanceCleaner
{
    public sealed class GhostCleanupResult
    {
        public int GroupsFound { get; set; }
        public int Removed { get; set; }
        public int Failed { get; set; }
    }

    public static GhostCleanupResult CleanDuplicateInstances(Action<string>? log = null)
    {
        var result = PnPGhostScanner.RemoveAll(log);
        return new GhostCleanupResult
        {
            GroupsFound = result.GroupsFound,
            Removed = result.Removed,
            Failed = result.Failed
        };
    }
}
