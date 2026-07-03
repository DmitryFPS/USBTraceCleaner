namespace USBTraceCleaner.Models;

public enum NetworkAuditKind
{
    WiFiProfile,
    WiFiEvent,
    EthernetAdapter,
    EthernetEvent,
    VpnProfile,
    VpnEvent,
    DnsCache,
    DnsEvent,
    NetworkProfile,
    RegistryTrace,
    EventLogChannel,
    EventLogEntry,
    RouterGateway,
    RouterDevice,
    RouterDhcp,
    RouterSnmp,
    ArpEntry,
    FirewallRule,
    NlaCache,
    HostsFile,
    BluetoothNetwork,
    UsbNetwork,
    SruDatabase,
    NetbiosCache,
    WlanRegistry,
    DhcpEvent,
    FirewallEvent,
    NcsiEvent,
    WinInetTrace,
    Other
}

public enum NetworkAuditFilterGroup
{
    All,
    WiFi,
    Ethernet,
    Vpn,
    Router,
    Dns,
    EventLogs,
    Registry,
    Cache,
    UsbBluetooth,
    Other
}

public enum NetworkAuthorizationStatus
{
    Unknown,
    Allowed
}

public sealed class NetworkAuditWhitelist
{
    public List<string> AllowedIps { get; init; } = [];
    public List<string> AllowedWiFi { get; init; } = [];
    public List<string> AllowedVpn { get; init; } = [];

    public static NetworkAuditWhitelist DefaultExample() => new()
    {
        AllowedIps = ["20.0.0.116", "20.20.20.76"],
        AllowedWiFi = ["doZOR"],
        AllowedVpn = ["HAPP", "happ", "happ-tun"]
    };

    public static NetworkAuditWhitelist Parse(string? ips, string? wifi, string? vpn) =>
        new()
        {
            AllowedIps = SplitList(ips),
            AllowedWiFi = SplitList(wifi),
            AllowedVpn = SplitList(vpn)
        };

    private static List<string> SplitList(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToList();
}

public sealed class NetworkAuditItem
{
    public bool Selected { get; set; }
    public DateTime? EventTime { get; init; }
    public NetworkAuditKind Kind { get; init; }
    public NetworkAuditFilterGroup FilterGroup { get; init; }
    public required string Source { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string? Secret { get; init; }
    public bool CanClean { get; init; }
    public required string Location { get; init; }
    public NetworkAuthorizationStatus AuthorizationStatus { get; set; } = NetworkAuthorizationStatus.Unknown;

    public string DisplayTime => EventTime?.ToString("dd.MM.yyyy HH:mm:ss") ?? "—";
    public string DisplayGroup => FilterGroup switch
    {
        NetworkAuditFilterGroup.WiFi => "Wi‑Fi",
        NetworkAuditFilterGroup.Ethernet => "Ethernet",
        NetworkAuditFilterGroup.Vpn => "VPN",
        NetworkAuditFilterGroup.Router => "Роутер",
        NetworkAuditFilterGroup.Dns => "DNS",
        NetworkAuditFilterGroup.EventLogs => "Журналы",
        NetworkAuditFilterGroup.Registry => "Реестр",
        NetworkAuditFilterGroup.Cache => "Кэши",
        NetworkAuditFilterGroup.UsbBluetooth => "USB / Bluetooth",
        _ => "Прочее"
    };

    public string DisplayAuthorization => AuthorizationStatus switch
    {
        NetworkAuthorizationStatus.Allowed => "Разрешено",
        _ => "Неизвестно"
    };

    public string DisplayDetail
    {
        get
        {
            var text = string.IsNullOrEmpty(Secret)
                ? Detail
                : MaskSecrets
                    ? $"{Detail} | Пароль: ••••••••"
                    : $"{Detail} | Пароль: {Secret}";
            return NetworkAuditDisplay.SanitizeForDisplay(text);
        }
    }

    public bool MaskSecrets { get; set; }

    public string ActionLabel => NetworkAuditDisplay.GetActionLabel(CanClean);

    public string CleanEffect => CanClean
        ? NetworkAuditDisplay.GetCleanEffect(Kind, Title)
        : "Только информация (вне ПК или текущее железо)";
}

public sealed class NetworkAuditOptions
{
    public DateTime DateFrom { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime DateTo { get; set; } = DateTime.Today.AddDays(1).AddSeconds(-1);
    public bool ShowSecrets { get; set; } = true;
    public bool SimulationMode { get; set; }
    public string? RouterIp { get; set; }
    public string? RouterLogin { get; set; }
    public string? RouterPassword { get; set; }
    public bool ScanWiFi { get; set; } = true;
    public bool ScanEthernet { get; set; } = true;
    public bool ScanVpn { get; set; } = true;
    public bool ScanRouter { get; set; } = true;
    public bool ScanDns { get; set; } = true;
    public bool ScanEventLogs { get; set; } = true;
    public bool ScanRegistry { get; set; } = true;
    public bool ScanCaches { get; set; } = true;
    public bool ScanUsbBluetooth { get; set; } = true;
    public NetworkAuditWhitelist Whitelist { get; set; } = NetworkAuditWhitelist.DefaultExample();
    public bool FullCleanMode { get; set; }
    public bool CleanHostsFile { get; set; }
    public bool DisconnectNetwork { get; set; } = true;
    public bool RebootAfterClean { get; set; } = true;
    public bool ShowUnknownOnly { get; set; }
}

public sealed class NetworkAuditProgress
{
    public string Phase { get; init; } = string.Empty;
    public int ItemsFound { get; init; }
}

public sealed class NetworkVerifyResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> RemainingIssues { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}
