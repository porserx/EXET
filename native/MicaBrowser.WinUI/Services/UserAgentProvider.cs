using MicaBrowser.Models;

namespace MicaBrowser.Services;

public static class UserAgentProvider
{
    private const string Desktop =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private const string Mobile =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36";
    private const string Tablet =
        "Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private const string Edge =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";
    private const string Firefox =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0";
    private const string Safari =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15";

    public static string? Resolve(AppSettings settings) => settings.UserAgentMode switch
    {
        "Desktop" => Desktop,
        "Mobile" => Mobile,
        "Tablet" => Tablet,
        "Chrome" => Desktop,
        "Edge" => Edge,
        "Firefox" => Firefox,
        "Safari" => Safari,
        "Custom" when !string.IsNullOrWhiteSpace(settings.UserAgentCustom) => settings.UserAgentCustom.Trim(),
        _ => null
    };
}

