using System.Net;
using Microsoft.AspNetCore.Http;

namespace Webbstatistik.SDK;

public static class TrackingRequestClientIpResolver
{
    private static readonly string[] DirectIpHeaderNames =
    [
        "CF-Connecting-IP",
        "True-Client-IP",
        "Fly-Client-IP",
        "X-Real-IP",
        "X-Client-IP",
        "Fastly-Client-IP"
    ];

    public static IPAddress? ResolveClientIp(HttpContext context)
    {
        foreach (var headerName in DirectIpHeaderNames)
        {
            if (TryParseIpList(context.Request.Headers[headerName].ToString(), out var directIp))
            {
                return directIp;
            }
        }

        if (TryParseIpList(context.Request.Headers["X-Forwarded-For"].ToString(), out var forwardedIp))
        {
            return forwardedIp;
        }

        if (TryParseForwardedHeader(context.Request.Headers["Forwarded"].ToString(), out var standardizedForwardedIp))
        {
            return standardizedForwardedIp;
        }

        return Normalize(context.Connection.RemoteIpAddress);
    }

    private static bool TryParseIpList(string rawValue, out IPAddress? ipAddress)
    {
        ipAddress = null;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        foreach (var candidate in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseIp(candidate, out ipAddress))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseForwardedHeader(string rawValue, out IPAddress? ipAddress)
    {
        ipAddress = null;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var forwardedEntries = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in forwardedEntries)
        {
            var segments = entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                const string prefix = "for=";
                if (!segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = segment[prefix.Length..].Trim().Trim('"');
                if (candidate.StartsWith('[') && candidate.Contains(']'))
                {
                    candidate = candidate[1..candidate.IndexOf(']')];
                }
                else if (candidate.Count(x => x == ':') == 1)
                {
                    candidate = candidate[..candidate.IndexOf(':')];
                }

                if (TryParseIp(candidate, out ipAddress))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseIp(string candidate, out IPAddress? ipAddress)
    {
        ipAddress = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.Trim().Trim('"');
        if (!IPAddress.TryParse(normalized, out var parsedIp) || parsedIp is null)
        {
            return false;
        }

        ipAddress = Normalize(parsedIp);
        return ipAddress is not null;
    }

    private static IPAddress? Normalize(IPAddress? ipAddress)
    {
        if (ipAddress is null)
        {
            return null;
        }

        return ipAddress.IsIPv4MappedToIPv6 ? ipAddress.MapToIPv4() : ipAddress;
    }
}
