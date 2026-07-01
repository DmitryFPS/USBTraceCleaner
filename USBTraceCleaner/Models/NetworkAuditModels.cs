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

public sealed class NetworkAuditItem
{
    public bool Selected { get; set; } = true;
    public DateTime? EventTime { get; init; }
    public NetworkAuditKind Kind { get; init; }
    public NetworkAuditFilterGroup FilterGroup { get; init; }
    public required string Source { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string? Secret { get; init; }
    public bool CanClean { get; init; }
    public required string Location { get; init; }

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

    public string DisplayDetail => string.IsNullOrEmpty(Secret)
        ? Detail
        : MaskSecrets
            ? $"{Detail} | Пароль: ••••••••"
            : $"{Detail} | Пароль: {Secret}";

    public bool MaskSecrets { get; set; }
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
}

public sealed class NetworkAuditProgress
{
    public string Phase { get; init; } = string.Empty;
    public int ItemsFound { get; init; }
}
