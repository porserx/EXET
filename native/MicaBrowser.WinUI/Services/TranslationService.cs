using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MicaBrowser.Models;

namespace MicaBrowser.Services;

public static class TranslationService
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        CookieContainer = new CookieContainer(),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        Timeout = TimeSpan.FromSeconds(35)
    };

    public static async Task<IReadOnlyList<string>> TranslateBatchAsync(
        AppSettings settings,
        IReadOnlyList<string> texts,
        string target,
        string source = "auto")
    {
        if (texts.Count == 0) return Array.Empty<string>();
        if (string.Equals(settings.TranslationProvider, "Bing", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.DeepSeekApiKey))
        {
            try
            {
                return await TranslateBingAsync(texts, target, source);
            }
            catch
            {
                return await TranslateMyMemoryAsync(texts, target, source);
            }
        }
        return await TranslateDeepSeekAsync(settings, texts, target, source);
    }

    private static async Task<IReadOnlyList<string>> TranslateDeepSeekAsync(
        AppSettings settings, IReadOnlyList<string> texts, string target, string source)
    {
        var key = settings.DeepSeekApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("请先在设置中填写 DeepSeek API Key");
        }

        var language = LanguageName(target);
        var sourceName = source == "auto" ? "the detected source language" : LanguageName(source);
        var prompt = "Translate each string in the JSON array from " + sourceName + " to " + language +
                     ". Keep placeholders, numbers, URLs and formatting. Return ONLY a JSON array of the same length, no markdown.";
        var payload = JsonSerializer.Serialize(new
        {
            model = "deepseek-chat",
            temperature = 0,
            messages = new object[]
            {
                new { role = "system", content = prompt },
                new { role = "user", content = JsonSerializer.Serialize(texts) }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DeepSeek 翻译失败：{(int)response.StatusCode} {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";
        content = StripCodeFence(content);
        var translated = JsonSerializer.Deserialize<List<string>>(content);
        if (translated is null || translated.Count != texts.Count)
        {
            throw new InvalidOperationException("DeepSeek 返回的翻译数量不匹配");
        }
        return translated;
    }

    private static async Task<IReadOnlyList<string>> TranslateBingAsync(
        IReadOnlyList<string> texts, string target, string source)
    {
        var to = BingLanguage(target);
        var from = BingSourceLanguage(source);
        using var page = new HttpRequestMessage(HttpMethod.Get, "https://www.bing.com/translator?from=" + from + "&to=" + to);
        page.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36");
        using var pageResponse = await Client.SendAsync(page);
        var html = await pageResponse.Content.ReadAsStringAsync();

        var ig = Regex.Match(html, "IG:\"([^\"]+)\"").Groups[1].Value;
        var tokenMatch = Regex.Match(html, "params_AbusePreventionHelper\\s*=\\s*\\[(\\d+),\"([^\"]+)\"");
        if (string.IsNullOrWhiteSpace(ig) || !tokenMatch.Success)
        {
            throw new InvalidOperationException("Bing 翻译页面无法初始化");
        }
        var key = tokenMatch.Groups[1].Value;
        var token = tokenMatch.Groups[2].Value;
        var joined = string.Join("\n", texts);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fromLang"] = from,
            ["text"] = joined,
            ["to"] = to,
            ["token"] = token,
            ["key"] = key
        });
        using var response = await Client.PostAsync(
            $"https://www.bing.com/ttranslatev3?isVertical=1&&IG={ig}&IID=translator.5024.1", form);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Bing 翻译失败：{(int)response.StatusCode}");
        }
        using var doc = JsonDocument.Parse(body);
        var value = doc.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? string.Empty;
        var parts = value.Split('\n');
        if (parts.Length != texts.Count)
        {
            // Bing may collapse line breaks; translate one by one as a fallback.
            var result = new List<string>();
            foreach (var text in texts)
            {
                result.Add(await TranslateBingSingleAsync(text, to, from, ig, key, token));
            }
            return result;
        }
        return parts;
    }

    private static async Task<string> TranslateBingSingleAsync(string text, string to, string from, string ig, string key, string token)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fromLang"] = from,
            ["text"] = text,
            ["to"] = to,
            ["token"] = token,
            ["key"] = key
        });
        using var response = await Client.PostAsync(
            $"https://www.bing.com/ttranslatev3?isVertical=1&&IG={ig}&IID=translator.5024.1", form);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? text;
    }

    private static async Task<IReadOnlyList<string>> TranslateMyMemoryAsync(
        IReadOnlyList<string> texts, string target, string source)
    {
        var from = source;
        if (string.IsNullOrWhiteSpace(from) || from == "auto")
        {
            from = DetectSource(texts);
        }
        var to = MyMemoryLanguage(target);
        from = MyMemoryLanguage(from);
        var joined = string.Join("\n", texts);
        var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(joined) +
                  "&langpair=" + Uri.EscapeDataString(from + "|" + to);
        using var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"MyMemory 翻译失败：{(int)response.StatusCode}");
        }
        using var doc = JsonDocument.Parse(body);
        var value = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString() ?? string.Empty;
        value = WebUtility.HtmlDecode(value);
        var parts = value.Split('\n');
        if (parts.Length == texts.Count)
        {
            return parts;
        }

        var result = new List<string>(texts.Count);
        foreach (var text in texts)
        {
            result.Add(await TranslateMyMemorySingleAsync(text, from, to));
        }
        return result;
    }

    private static async Task<string> TranslateMyMemorySingleAsync(string text, string from, string to)
    {
        var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) +
                  "&langpair=" + Uri.EscapeDataString(from + "|" + to);
        using var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return text;
        using var doc = JsonDocument.Parse(body);
        var value = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString() ?? text;
        return WebUtility.HtmlDecode(value);
    }

    private static string DetectSource(IReadOnlyList<string> texts)
    {
        var sample = string.Concat(texts.Take(8));
        if (sample.Any(ch => ch >= '\u3040' && ch <= '\u30ff')) return "ja";
        if (sample.Any(ch => ch >= '\uac00' && ch <= '\ud7af')) return "ko";
        if (sample.Any(ch => ch >= '\u0600' && ch <= '\u06ff')) return "ar";
        if (sample.Any(ch => ch >= '\u0400' && ch <= '\u04ff')) return "ru";
        if (sample.Any(ch => ch >= '\u4e00' && ch <= '\u9fff')) return "zh-CN";
        return "en";
    }

    private static string MyMemoryLanguage(string code) => code switch
    {
        "zh-CN" or "zh-Hans" => "zh-CN",
        "zh-TW" or "zh-Hant" => "zh-TW",
        "auto" or "" => "en",
        _ => code
    };

    private static string LanguageName(string code) => code switch
    {
        "zh-CN" or "zh-Hans" => "Simplified Chinese",
        "zh-TW" or "zh-Hant" => "Traditional Chinese",
        "en" => "English",
        "ja" => "Japanese",
        "ko" => "Korean",
        "fr" => "French",
        "de" => "German",
        "es" => "Spanish",
        "ru" => "Russian",
        "ar" => "Arabic",
        "pt" => "Portuguese",
        "it" => "Italian",
        _ => code
    };

    private static string BingLanguage(string code) => code switch
    {
        "zh-CN" or "zh-Hans" => "zh-Hans",
        "zh-TW" or "zh-Hant" => "zh-Hant",
        "en" => "en",
        "ja" => "ja",
        "ko" => "ko",
        "fr" => "fr",
        "de" => "de",
        "es" => "es",
        "ru" => "ru",
        "ar" => "ar",
        "pt" => "pt",
        "it" => "it",
        _ => "en"
    };

    private static string BingSourceLanguage(string code) => code switch
    {
        "zh-CN" or "zh-Hans" => "zh-Hans",
        "zh-TW" or "zh-Hant" => "zh-Hant",
        "auto" or "" => "auto-detect",
        _ => BingLanguage(code)
    };

    private static string StripCodeFence(string content)
    {
        content = content.Trim();
        if (!content.StartsWith("```", StringComparison.Ordinal)) return content;
        var firstNewline = content.IndexOf('\n');
        var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline < 0 || lastFence <= firstNewline) return content;
        return content[(firstNewline + 1)..lastFence].Trim();
    }
}
