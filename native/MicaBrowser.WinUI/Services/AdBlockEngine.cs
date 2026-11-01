using System.Text;

namespace MicaBrowser.Services;

public sealed class AdBlockStats
{
    public int DomainRules { get; init; }
    public int PathRules { get; init; }
    public int ExceptionRules { get; init; }
    public int Lines { get; init; }
}

public sealed class AdBlockEngine
{
    private readonly HashSet<string> _blockedDomains;
    private readonly HashSet<string> _allowedDomains;
    private readonly Dictionary<string, List<PathRule>> _blockedPathRules;
    private readonly Dictionary<string, List<PathRule>> _allowedPathRules;

    private AdBlockEngine(
        HashSet<string> blockedDomains,
        HashSet<string> allowedDomains,
        Dictionary<string, List<PathRule>> blockedPathRules,
        Dictionary<string, List<PathRule>> allowedPathRules,
        AdBlockStats stats)
    {
        _blockedDomains = blockedDomains;
        _allowedDomains = allowedDomains;
        _blockedPathRules = blockedPathRules;
        _allowedPathRules = allowedPathRules;
        Stats = stats;
    }

    public AdBlockStats Stats { get; }

    public static AdBlockEngine Load(IEnumerable<string> files)
    {
        var blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockedPathRules = new Dictionary<string, List<PathRule>>(StringComparer.OrdinalIgnoreCase);
        var allowedPathRules = new Dictionary<string, List<PathRule>>(StringComparer.OrdinalIgnoreCase);
        var lines = 0;
        var exceptionCount = 0;

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            foreach (var rawLine in File.ReadLines(file, Encoding.UTF8))
            {
                lines++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '!' || line[0] == '[')
                {
                    continue;
                }

                // Cosmetic / scriptlet rules are handled separately by the web engine.
                if (line.Contains("##", StringComparison.Ordinal) ||
                    line.Contains("#@#", StringComparison.Ordinal) ||
                    line.Contains("#$#", StringComparison.Ordinal) ||
                    line.Contains("#%#", StringComparison.Ordinal) ||
                    line.Contains("#?#", StringComparison.Ordinal))
                {
                    continue;
                }

                var isException = line.StartsWith("@@", StringComparison.Ordinal);
                if (isException)
                {
                    line = line[2..];
                    exceptionCount++;
                }

                // Rules with page-specific domain options are too contextual for the
                // lightweight engine; skipping them avoids false positives.
                if (line.Contains("$domain=", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("$denyallow=", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("$removeparam", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("$redirect", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var networkRule = StripOptions(line);
                if (networkRule.Length < 3)
                {
                    continue;
                }

                if (networkRule.StartsWith("||", StringComparison.Ordinal))
                {
                    var body = networkRule[2..];
                    var hostEnd = body.IndexOfAny(new[] { '/', '^', '*', '|' });
                    if (hostEnd < 0)
                    {
                        hostEnd = body.Length;
                    }

                    var host = body[..hostEnd].Trim().TrimEnd('.');
                    if (!IsValidHost(host))
                    {
                        continue;
                    }

                    var remainder = body[hostEnd..];
                    if (remainder.Length == 0 || remainder == "^" || remainder == "^|")
                    {
                        if (isException)
                        {
                            allowedDomains.Add(host);
                        }
                        else
                        {
                            blockedDomains.Add(host);
                        }

                        continue;
                    }

                    if (remainder[0] == '^')
                    {
                        remainder = remainder[1..];
                    }

                    if (remainder.Length == 0 || remainder == "|")
                    {
                        if (isException)
                        {
                            allowedDomains.Add(host);
                        }
                        else
                        {
                            blockedDomains.Add(host);
                        }
                        continue;
                    }

                    var pathRule = new PathRule(host, CleanPattern(remainder));
                    if (!pathRule.IsValid)
                    {
                        continue;
                    }

                    var target = isException ? allowedPathRules : blockedPathRules;
                    if (!target.TryGetValue(host, out var list))
                    {
                        list = new List<PathRule>();
                        target[host] = list;
                    }
                    list.Add(pathRule);
                }
            }
        }

        var stats = new AdBlockStats
        {
            DomainRules = blockedDomains.Count,
            PathRules = blockedPathRules.Sum(pair => pair.Value.Count),
            ExceptionRules = exceptionCount,
            Lines = lines
        };

        return new AdBlockEngine(
            blockedDomains,
            allowedDomains,
            blockedPathRules,
            allowedPathRules,
            stats);
    }

    public bool IsBlocked(Uri requestUri, Uri? pageUri, out string reason)
    {
        reason = string.Empty;
        if (requestUri.Scheme != Uri.UriSchemeHttp && requestUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = requestUri.Host.TrimEnd('.');
        if (host.Length == 0)
        {
            return false;
        }

        var pathAndQuery = requestUri.PathAndQuery;

        // Exceptions always win.
        foreach (var suffix in HostSuffixes(host))
        {
            if (_allowedDomains.Contains(suffix))
            {
                return false;
            }

            if (_allowedPathRules.TryGetValue(suffix, out var allowedRules) &&
                allowedRules.Any(rule => rule.Matches(pathAndQuery)))
            {
                return false;
            }
        }

        foreach (var suffix in HostSuffixes(host))
        {
            if (_blockedDomains.Contains(suffix))
            {
                reason = suffix;
                return true;
            }

            if (_blockedPathRules.TryGetValue(suffix, out var rules))
            {
                foreach (var rule in rules)
                {
                    if (rule.Matches(pathAndQuery))
                    {
                        reason = rule.Display;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> HostSuffixes(string host)
    {
        yield return host;
        var index = host.IndexOf('.');
        while (index >= 0 && index + 1 < host.Length)
        {
            host = host[(index + 1)..];
            if (host.Contains('.'))
            {
                yield return host;
            }
            index = host.IndexOf('.');
        }
    }

    private static string StripOptions(string rule)
    {
        var index = rule.IndexOf('$');
        return index >= 0 ? rule[..index] : rule;
    }

    private static string CleanPattern(string pattern)
    {
        if (pattern.EndsWith('|'))
        {
            pattern = pattern[..^1];
        }
        return pattern;
    }

    private static bool IsValidHost(string host)
    {
        return host.Length is > 2 and < 255 &&
               !host.Contains(' ') &&
               !host.Contains('/') &&
               !host.Contains('*') &&
               host.Contains('.');
    }

    private sealed class PathRule
    {
        public PathRule(string host, string pattern)
        {
            Host = host;
            Pattern = pattern;
            Display = "||" + host + pattern;
        }

        public string Host { get; }
        public string Pattern { get; }
        public string Display { get; }
        public bool IsValid => Pattern.Length > 1 && Pattern.Length < 400;

        public bool Matches(string pathAndQuery)
        {
            if (string.IsNullOrEmpty(pathAndQuery))
            {
                return false;
            }

            return GlobMatch(pathAndQuery, Pattern);
        }

        private static bool GlobMatch(string input, string pattern)
        {
            var i = 0;
            var p = 0;
            var star = -1;
            var match = 0;

            while (i < input.Length)
            {
                if (p < pattern.Length)
                {
                    var pc = pattern[p];
                    if (pc == '^')
                    {
                        var c = input[i];
                        if (c is '/' or '?' or '&' or '=' or ':' or '#')
                        {
                            p++;
                            i++;
                            continue;
                        }
                        if (i == input.Length - 1)
                        {
                            p++;
                            continue;
                        }
                    }
                    else if (pc == '*')
                    {
                        star = p++;
                        match = i;
                        continue;
                    }
                    else if (pc == '|' && p == pattern.Length - 1)
                    {
                        return i == input.Length;
                    }
                    else if (char.ToLowerInvariant(pc) == char.ToLowerInvariant(input[i]))
                    {
                        p++;
                        i++;
                        continue;
                    }
                }

                if (star >= 0)
                {
                    p = star + 1;
                    i = ++match;
                    continue;
                }
                return false;
            }

            while (p < pattern.Length && pattern[p] == '*')
            {
                p++;
            }

            if (p == pattern.Length - 1 && pattern[p] == '^')
            {
                p++;
            }

            return p == pattern.Length;
        }
    }
}
