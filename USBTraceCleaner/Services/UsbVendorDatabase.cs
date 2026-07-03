using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace USBTraceCleaner.Services;

// База VID/PID (usb.ids), вшита в exe. Реестр в приоритете — сюда смотрим, если имён нет.
public static class UsbVendorDatabase
{
    private static readonly Lazy<Data> _db = new(Load, isThreadSafe: true);

    private static readonly Regex VendorLine = new(
        @"^([0-9a-fA-F]{4})  (.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProductLine = new(
        @"^\t([0-9a-fA-F]{4})  (.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed class Data
    {
        public Dictionary<string, string> Vendors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Products { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static int VendorCount => _db.Value.Vendors.Count;
    public static int ProductCount => _db.Value.Products.Count;

    public static string? LookupVendor(string? vid)
    {
        if (string.IsNullOrWhiteSpace(vid)) return null;
        return _db.Value.Vendors.TryGetValue(vid.Trim(), out var name) ? name : null;
    }

    public static string? LookupProduct(string? vid, string? pid)
    {
        if (string.IsNullOrWhiteSpace(vid) || string.IsNullOrWhiteSpace(pid)) return null;
        var key = $"{vid.Trim()}{pid.Trim()}";
        return _db.Value.Products.TryGetValue(key, out var name) ? name : null;
    }

    private static Data Load()
    {
        var data = new Data();
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("USBVendors.txt", StringComparison.OrdinalIgnoreCase));
            if (resName == null) return data;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return data;

            using var reader = new StreamReader(stream);
            string? currentVid = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;

                var pm = ProductLine.Match(line);
                if (pm.Success && currentVid != null)
                {
                    var key = currentVid + pm.Groups[1].Value.ToLowerInvariant();
                    data.Products[key] = pm.Groups[2].Value.Trim();
                    continue;
                }

                var vm = VendorLine.Match(line);
                if (vm.Success)
                {
                    currentVid = vm.Groups[1].Value.ToLowerInvariant();
                    data.Vendors[currentVid] = vm.Groups[2].Value.Trim();
                }
            }
        }
        catch
        {
            // необязательный источник — при сбое просто пусто
        }

        return data;
    }
}
