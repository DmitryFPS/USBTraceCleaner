using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
internal sealed partial class RouterAuditScanner
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public IEnumerable<NetworkAuditItem> Scan(NetworkAuditOptions options)
    {
        var gateway = string.IsNullOrWhiteSpace(options.RouterIp)
            ? NetshHelper.GetDefaultGateway()
            : options.RouterIp.Trim();

        if (string.IsNullOrWhiteSpace(gateway))
        {
            yield return new NetworkAuditItem
            {
                Kind = NetworkAuditKind.RouterGateway,
                FilterGroup = NetworkAuditFilterGroup.Router,
                Source = "маршрутизация",
                Title = "Шлюз по умолчанию не найден",
                Detail = "ПК не подключён к сети или шлюз недоступен",
                Location = "—",
                CanClean = false
            };
            yield break;
        }

        yield return new NetworkAuditItem
        {
            Kind = NetworkAuditKind.RouterGateway,
            FilterGroup = NetworkAuditFilterGroup.Router,
            Source = "маршрутизация",
            Title = $"Шлюз / роутер: {gateway}",
            Detail = $"Ping: {(PingHost(gateway) ? "доступен" : "не отвечает")}",
            Location = gateway,
            CanClean = false
        };

        foreach (var item in ScanArp(gateway))
            yield return item;

        foreach (var item in TrySnmp(gateway))
            yield return item;

        foreach (var item in TryRouterHttp(gateway, options.RouterLogin, options.RouterPassword))
            yield return item;

        foreach (var item in ScanLocalNetwork(gateway))
            yield return item;
    }

    private static IEnumerable<NetworkAuditItem> ScanArp(string gateway)
    {
        var output = ProcessRunner.Run("arp", "-a");
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains(gateway[..Math.Min(6, gateway.Length)], StringComparison.Ordinal))
            {
                var m = ArpLineRegex().Match(line);
                if (!m.Success) continue;
                var ip = m.Groups[1].Value;
                var mac = m.Groups[2].Value;
                if (ip.StartsWith("224.") || ip.StartsWith("239.") || ip.StartsWith("255.")) continue;

                yield return new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.ArpEntry,
                    FilterGroup = NetworkAuditFilterGroup.Router,
                    Source = "arp -a",
                    Title = $"Устройство в LAN: {ip}",
                    Detail = $"MAC: {mac}",
                    Location = ip,
                    CanClean = false
                };
            }
        }
    }

    private static IEnumerable<NetworkAuditItem> ScanLocalNetwork(string gateway)
    {
        var items = new List<NetworkAuditItem>();
        try
        {
            var neighbors = ProcessRunner.Run("powershell", "-NoProfile -Command \"Get-NetNeighbor -AddressFamily IPv4 | Select-Object IPAddress,LinkLayerAddress,State | Format-Table -HideTableHeaders\"");
            foreach (var line in neighbors.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                items.Add(new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.RouterDevice,
                    FilterGroup = NetworkAuditFilterGroup.Router,
                    Source = "Get-NetNeighbor",
                    Title = $"Сосед в сети: {parts[0]}",
                    Detail = $"MAC/состояние: {string.Join(' ', parts.Skip(1))}",
                    Location = parts[0],
                    CanClean = false
                });
            }
        }
        catch { /* ignore */ }

        items.Add(new NetworkAuditItem
        {
            Kind = NetworkAuditKind.RouterDevice,
            FilterGroup = NetworkAuditFilterGroup.Router,
            Source = "net view",
            Title = "Компьютеры в рабочей группе",
            Detail = Truncate(ProcessRunner.Run("net", "view"), 800),
            Location = gateway,
            CanClean = false
        });
        return items;
    }

    private static IEnumerable<NetworkAuditItem> TrySnmp(string gateway)
    {
        foreach (var community in new[] { "public", "private" })
        {
            var descr = SnmpGetString(gateway, community, "1.3.6.1.2.1.1.1.0");
            if (descr == null) continue;

            yield return new NetworkAuditItem
            {
                Kind = NetworkAuditKind.RouterSnmp,
                FilterGroup = NetworkAuditFilterGroup.Router,
                Source = $"SNMP ({community})",
                Title = $"SNMP: {gateway}",
                Detail = descr,
                Location = gateway,
                CanClean = false
            };

            var name = SnmpGetString(gateway, community, "1.3.6.1.2.1.1.5.0");
            if (name != null)
            {
                yield return new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.RouterSnmp,
                    FilterGroup = NetworkAuditFilterGroup.Router,
                    Source = $"SNMP ({community})",
                    Title = $"Имя устройства: {name}",
                    Detail = "sysName",
                    Location = gateway,
                    CanClean = false
                };
            }

            break;
        }
    }

    private static IEnumerable<NetworkAuditItem> TryRouterHttp(string gateway, string? login, string? password)
    {
        var urls = new[]
        {
            $"http://{gateway}/",
            $"http://{gateway}/status.asp",
            $"http://{gateway}/dhcp_clients.asp",
            $"http://{gateway}/lan.htm",
            $"https://{gateway}/"
        };

        foreach (var url in urls)
        {
            var batch = TryFetchRouterPage(url, gateway, login, password);
            if (batch.Count == 0) continue;
            foreach (var item in batch)
                yield return item;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(login))
        {
            yield return new NetworkAuditItem
            {
                Kind = NetworkAuditKind.RouterDhcp,
                FilterGroup = NetworkAuditFilterGroup.Router,
                Source = "HTTP",
                Title = "Не удалось прочитать веб-интерфейс роутера",
                Detail = "Проверьте IP, логин и пароль админки роутера",
                Location = gateway,
                CanClean = false
            };
        }
    }

    private static List<NetworkAuditItem> TryFetchRouterPage(string url, string gateway, string? login, string? password)
    {
        var items = new List<NetworkAuditItem>();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(login))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
            }

            using var response = Http.Send(request);
            if (!response.IsSuccessStatusCode) return items;

            var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (html.Length < 50) return items;

            items.Add(new NetworkAuditItem
            {
                Kind = NetworkAuditKind.RouterDhcp,
                FilterGroup = NetworkAuditFilterGroup.Router,
                Source = url,
                Title = $"Веб-интерфейс роутера: {url}",
                Detail = ExtractRouterSummary(html),
                Location = gateway,
                CanClean = false
            });
            items.AddRange(ExtractDhcpLeases(html));
        }
        catch { /* ignore */ }

        return items;
    }

    private static IEnumerable<NetworkAuditItem> ExtractDhcpLeases(string html)
    {
        foreach (Match m in DhcpRowRegex().Matches(html))
        {
            yield return new NetworkAuditItem
            {
                Kind = NetworkAuditKind.RouterDhcp,
                FilterGroup = NetworkAuditFilterGroup.Router,
                Source = "роутер DHCP",
                Title = $"DHCP клиент: {m.Groups[1].Value}",
                Detail = $"MAC: {m.Groups[2].Value}",
                Location = m.Groups[1].Value,
                CanClean = false
            };
        }
    }

    private static string ExtractRouterSummary(string html)
    {
        var text = HtmlTagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return Truncate(text, 600);
    }

    private static string? SnmpGetString(string host, string community, string oid)
    {
        try
        {
            var payload = BuildSnmpGet(community, oid);
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 3000;
            client.Client.SendTimeout = 3000;
            var endpoint = new IPEndPoint(IPAddress.Parse(host), 161);
            client.Send(payload, payload.Length, endpoint);
            var remote = new IPEndPoint(IPAddress.Any, 0);
            var response = client.Receive(ref remote);
            return ParseSnmpOctetString(response);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildSnmpGet(string community, string oid)
    {
        var oidBytes = EncodeOid(oid);
        var communityBytes = Encoding.ASCII.GetBytes(community);
        var varBind = Concat(EncodeTLV(0x30, Concat(
            EncodeTLV(0x06, oidBytes),
            EncodeTLV(0x05, []))));

        var pdu = EncodeTLV(0xA0, Concat(
            EncodeTLV(0x02, [0x01, 0]),
            EncodeTLV(0x02, [0x01, 0]),
            EncodeTLV(0x02, [0x01, 0]),
            EncodeTLV(0x30, varBind)));

        return EncodeTLV(0x30, Concat(
            EncodeTLV(0x02, [0x01, 0]),
            EncodeTLV(0x04, communityBytes),
            pdu));
    }

    private static string? ParseSnmpOctetString(byte[] data)
    {
        for (var i = 0; i < data.Length - 2; i++)
        {
            if (data[i] != 0x04) continue;
            var len = data[i + 1];
            if (len <= 0 || i + 2 + len > data.Length) continue;
            return Encoding.UTF8.GetString(data, i + 2, len);
        }
        return null;
    }

    private static byte[] EncodeOid(string oid)
    {
        var parts = oid.Split('.').Select(int.Parse).ToArray();
        var bytes = new List<byte> { (byte)(parts[0] * 40 + parts[1]) };
        for (var i = 2; i < parts.Length; i++)
            bytes.AddRange(EncodeSubOid(parts[i]));
        return bytes.ToArray();
    }

    private static IEnumerable<byte> EncodeSubOid(int value)
    {
        if (value < 128) return [ (byte)value ];
        var stack = new Stack<byte>();
        stack.Push((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            stack.Push((byte)(0x80 | (value & 0x7F)));
            value >>= 7;
        }
        return stack;
    }

    private static byte[] EncodeTLV(byte tag, byte[] value)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(tag);
        ms.Write(EncodeLength(value.Length));
        ms.Write(value);
        return ms.ToArray();
    }

    private static byte[] EncodeLength(int length)
    {
        if (length < 0x80) return [(byte)length];
        var bytes = BitConverter.GetBytes(length).Where(b => b > 0).Reverse().ToArray();
        return Concat([(byte)(0x80 | bytes.Length)], bytes);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var len = parts.Sum(p => p.Length);
        var result = new byte[len];
        var offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }

    private static bool PingHost(string host)
    {
        try
        {
            using var ping = new Ping();
            return ping.Send(host, 1500).Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fa-f\-]{11,17})", RegexOptions.IgnoreCase)]
    private static partial Regex ArpLineRegex();

    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3}).{0,40}?([0-9A-Fa-f]{2}(?:[:-][0-9A-Fa-f]{2}){5})")]
    private static partial Regex DhcpRowRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
