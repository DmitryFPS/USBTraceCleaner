using USBTraceCleaner.Models;



namespace USBTraceCleaner.Services;



/// <summary>USBTraceCleaner.exe --clean — headless real cleanup.</summary>

public static class HeadlessCleaner

{

    public static int Run()

    {

        if (!AdminHelper.IsAdministrator())

        {

            Console.Error.WriteLine("Требуются права администратора.");

            return 1;

        }



        var options = new CleanupOptions

        {

            SimulationMode = false,

            SaveBackup = true,

            BackupPath = AppPaths.GetExeDirectory(),

            CreateRestorePoint = false,

            CloseExplorer = false,

            RebootAfterClean = false,

            CleanAllUsbDevices = false,

            CleanMtpDevices = true,

            ExportFullUsbEnum = false,

            CleanEventLogs = false,
            ScrubLogFiles = true,
            PreserveLogFileTimestamps = true
        };



        var before = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);

        Console.WriteLine($"USB-накопители до очистки: {before}");



        var cleaner = new ArtifactCleaner();

        var result = cleaner.ExecuteUsboOblivionAsync(options).GetAwaiter().GetResult();



        Console.WriteLine(result.Log);



        var after = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);

        Console.WriteLine($"USB-накопители после очистки: {after}");



        if (!string.IsNullOrEmpty(result.ErrorMessage))

        {

            Console.Error.WriteLine(result.ErrorMessage);

            return 1;

        }



        return result.Success ? 0 : 1;

    }

}


