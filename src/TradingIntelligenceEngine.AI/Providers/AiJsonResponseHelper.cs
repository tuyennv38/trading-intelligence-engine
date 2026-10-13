using System;
using System.Text;
using System.Text.Json;

namespace TradingIntelligenceEngine.AI.Providers;

internal static class AiJsonResponseHelper
{
    /// <summary>
    /// Lấy text content từ body chat/completions (JSON thường, SSE, hoặc content dạng array).
    /// </summary>
    public static string ExtractAssistantText(string rawHttpBody)
    {
        if (string.IsNullOrWhiteSpace(rawHttpBody))
            return string.Empty;

        var trimmed = rawHttpBody.TrimStart();

        // Proxy đôi khi trả SSE dù stream=false
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return ExtractFromSse(rawHttpBody);

        // Body không phải JSON (plain text error / prose)
        if (trimmed[0] != '{' && trimmed[0] != '[')
            return trimmed;

        try
        {
            using var document = JsonDocument.Parse(rawHttpBody);
            var root = document.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var message))
                {
                    var text = ReadContentProperty(message);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }

                if (choice.TryGetProperty("delta", out var delta))
                {
                    var text = ReadContentProperty(delta);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }

                if (choice.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                    return textEl.GetString() ?? string.Empty;
            }

            // Một số proxy trả thẳng object nghiệp vụ
            if (root.ValueKind == JsonValueKind.Object)
                return root.GetRawText();
        }
        catch (JsonException)
        {
            return trimmed;
        }

        return trimmed;
    }

    /// <summary>
    /// Tách object JSON đầu tiên trong text (bỏ prose/markdown quanh JSON).
    /// </summary>
    public static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = StripMarkdownFence(text.Trim());
        if (cleaned.StartsWith('{'))
            return ExtractBalancedJsonObject(cleaned);

        var start = cleaned.IndexOf('{');
        if (start < 0)
            return cleaned;

        return ExtractBalancedJsonObject(cleaned.Substring(start));
    }

    public static string Preview(string? text, int maxLen = 400)
    {
        if (string.IsNullOrEmpty(text))
            return "<empty>";

        var oneLine = text.Replace("\r", " ").Replace("\n", " ");
        return oneLine.Length <= maxLen ? oneLine : oneLine.Substring(0, maxLen) + "...";
    }

    private static string ReadContentProperty(JsonElement parent)
    {
        if (!parent.TryGetProperty("content", out var content))
            return string.Empty;

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        // Multimodal / array content parts: [{ "type":"text", "text":"..." }]
        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    sb.Append(part.GetString());
                    continue;
                }

                if (part.ValueKind == JsonValueKind.Object)
                {
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
                    else if (part.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                        sb.Append(c.GetString());
                }
            }

            return sb.ToString();
        }

        if (content.ValueKind == JsonValueKind.Object)
            return content.GetRawText();

        return string.Empty;
    }

    private static string ExtractFromSse(string raw)
    {
        var sb = new StringBuilder();
        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var payload = line.Substring(5).Trim();
            if (payload == "[DONE]")
                continue;

            if (!payload.StartsWith('{'))
                continue;

            try
            {
                using var chunkDoc = JsonDocument.Parse(payload);
                if (!chunkDoc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta))
                    sb.Append(ReadContentProperty(delta));
                else if (choice.TryGetProperty("message", out var message))
                    sb.Append(ReadContentProperty(message));
            }
            catch (JsonException)
            {
                // ignore bad chunk
            }
        }

        return sb.ToString();
    }

    private static string StripMarkdownFence(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            content = content.Substring(7);
        else if (content.StartsWith("```", StringComparison.Ordinal))
            content = content.Substring(3);

        content = content.Trim();
        if (content.EndsWith("```", StringComparison.Ordinal))
            content = content.Substring(0, content.Length - 3);

        return content.Trim();
    }

    private static string ExtractBalancedJsonObject(string text)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;
        int start = -1;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                    return text.Substring(start, i - start + 1);
            }
        }

        return text;
    }
}
