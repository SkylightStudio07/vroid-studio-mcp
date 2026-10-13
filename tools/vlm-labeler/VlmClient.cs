using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VroidVlmLabeler;

/// <summary>Minimal OpenAI-compatible vision chat client. One image per request, no max_tokens.</summary>
public sealed class VlmClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;

    public VlmClient(string apiBase, string model, int timeoutSeconds)
    {
        _endpoint = apiBase.TrimEnd('/') + "/chat/completions";
        _model = model;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }

    public string Label(string systemPrompt, string taskPrompt, string imageBase64)
    {
        var payload = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = taskPrompt },
                        new { type = "image_url", image_url = new { url = "data:image/png;base64," + imageBase64 } },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        using var response = _http.Send(request);
        var body = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
        if (!response.IsSuccessStatusCode)
            throw new Exception($"VLM HTTP {(int)response.StatusCode}: {Truncate(body, 300)}");

        using var doc = JsonDocument.Parse(body);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        return Sanitize(content);
    }

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        foreach (var line in text.Split('\n'))
        {
            var candidate = line.Trim();
            if (candidate.Length > 0) { text = candidate; break; }
        }
        text = text.Trim().Trim('"', '\'', '`', '*', '.', ',', ':', ';', '-', ' ');
        text = text.Replace("  ", " ");
        if (text.Length > 80) text = text.Substring(0, 80).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "...";

    public void Dispose() => _http.Dispose();
}
