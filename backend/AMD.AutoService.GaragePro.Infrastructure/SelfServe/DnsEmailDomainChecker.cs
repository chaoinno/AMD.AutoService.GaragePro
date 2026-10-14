using System.Globalization;
using AMD.AutoService.GaragePro.Application.Abstractions;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ตรวจว่าโดเมนของอีเมลรับอีเมลได้จริง (docs/12 §7 ชั้น 3 — กันพิมพ์มั่ว/สะกดผิด) ด้วย MX แล้วค่อย A/AAAA (implicit MX ตาม RFC 5321)
/// · NXDOMAIN · null MX (<c>MX 0 .</c> RFC 7505) · ไม่มีทั้ง MX และ A/AAAA → <see cref="EmailDomainVerdict.NoMailServer"/>
/// · resolver ล่ม/timeout/SERVFAIL → <see cref="EmailDomainVerdict.Unknown"/> (ผู้เรียกปล่อยผ่าน — DNS ของเราล่มต้องไม่ปิดการสมัคร)
/// · cache ผลที่แน่นอน 24 ชม. · Unknown cache แค่ 1 นาที (กันยิง DNS รัวตอน resolver ล่ม แต่หายเร็วเมื่อกลับมา)
/// · ใช้ DnsClient เพราะ BCL ถามได้แค่ A/AAAA (Dns.GetHostAddressesAsync) ซึ่งตัดสินโดเมนที่มีแต่ MX ผิด
/// </summary>
public sealed class DnsEmailDomainChecker(IMemoryCache cache, ILogger<DnsEmailDomainChecker> logger, ILookupClient? lookup = null)
    : IEmailDomainChecker
{
    private static readonly TimeSpan DefiniteTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan UnknownTtl = TimeSpan.FromMinutes(1);
    private static readonly IdnMapping Idn = new();

    private static readonly Lazy<ILookupClient> SharedLookup = new(() => new LookupClient(new LookupClientOptions
    {
        UseCache = true,
        Timeout = TimeSpan.FromSeconds(3),
        Retries = 1,
        ThrowDnsErrors = false,
        ContinueOnDnsError = false,
    }));

    private ILookupClient Lookup => lookup ?? SharedLookup.Value;

    public async Task<EmailDomainVerdict> CheckAsync(string domain, CancellationToken ct = default)
    {
        var ascii = ToAscii(domain);
        if (ascii is null) return EmailDomainVerdict.NoMailServer;

        var key = "sp:dns:" + ascii;
        if (cache.TryGetValue(key, out EmailDomainVerdict cached)) return cached;

        var verdict = await ResolveAsync(ascii, ct);
        cache.Set(key, verdict, verdict == EmailDomainVerdict.Unknown ? UnknownTtl : DefiniteTtl);
        return verdict;
    }

    private async Task<EmailDomainVerdict> ResolveAsync(string domain, CancellationToken ct)
    {
        try
        {
            var mx = await Lookup.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
            if (mx.HasError)
                return mx.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain
                    ? EmailDomainVerdict.NoMailServer
                    : Unknown(domain, mx.ErrorMessage);

            var exchanges = mx.Answers.MxRecords().ToList();
            if (exchanges.Count > 0)
                // null MX "." = โดเมนประกาศว่าไม่รับอีเมล (RFC 7505)
                return exchanges.Any(r => r.Exchange.Value.Trim('.').Length > 0)
                    ? EmailDomainVerdict.Ok
                    : EmailDomainVerdict.NoMailServer;

            foreach (var type in new[] { QueryType.A, QueryType.AAAA })
            {
                var address = await Lookup.QueryAsync(domain, type, cancellationToken: ct);
                if (address.HasError)
                {
                    if (address.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain)
                        return EmailDomainVerdict.NoMailServer;
                    return Unknown(domain, address.ErrorMessage);
                }

                if (address.Answers.Any(r => r is ARecord or AaaaRecord)) return EmailDomainVerdict.Ok;
            }

            return EmailDomainVerdict.NoMailServer;
        }
        catch (Exception ex) when (ex is DnsResponseException or System.Net.Sockets.SocketException
                                       or TimeoutException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return Unknown(domain, ex.GetType().Name);
        }
    }

    private EmailDomainVerdict Unknown(string domain, string? reason)
    {
        logger.LogWarning("ตรวจ DNS ของโดเมน {Domain} ไม่ได้ ({Reason}) ปล่อยผ่าน", domain, reason);
        return EmailDomainVerdict.Unknown;
    }

    /// <summary>โดเมน → รูป ASCII (punycode) ตัวพิมพ์เล็ก — null เมื่อไม่ใช่ชื่อโดเมนที่ถามได้</summary>
    internal static string? ToAscii(string? domain)
    {
        var value = domain?.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(value) || value.Length > 253 || !value.Contains('.')) return null;
        try
        {
            return Idn.GetAscii(value).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
