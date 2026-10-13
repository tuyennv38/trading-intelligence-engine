using Microsoft.AspNetCore.Mvc;
using TradingIntelligenceEngine.Api.Models;
using TradingIntelligenceEngine.Domain.Configuration;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/ai/config")]
public class AiConfigController : ControllerBase
{
    private readonly AiProviderOptions _options;

    public AiConfigController(AiProviderOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Lấy cấu hình AI hiện tại (API key được mask).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AiProviderConfigResponseDto), 200)]
    public IActionResult GetConfig()
    {
        var (baseUrl, apiKey, model) = _options.Snapshot();

        return Ok(new AiProviderConfigResponseDto
        {
            BaseUrl = baseUrl,
            Model = model,
            HasApiKey = !string.IsNullOrWhiteSpace(apiKey),
            ApiKeyMasked = MaskApiKey(apiKey)
        });
    }

    /// <summary>
    /// Cập nhật BaseUrl / ApiKey / Model dùng cho mọi request AI tiếp theo.
    /// Để trống ApiKey nếu muốn giữ key hiện tại.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(AiProviderConfigResponseDto), 200)]
    [ProducesResponseType(typeof(string), 400)]
    public IActionResult UpdateConfig([FromBody] AiProviderConfigDto request)
    {
        if (request == null)
            return BadRequest("Invalid request.");

        if (string.IsNullOrWhiteSpace(request.BaseUrl) && request.ApiKey is null && string.IsNullOrWhiteSpace(request.Model))
            return BadRequest("At least one of BaseUrl, ApiKey, or Model must be provided.");

        _options.Update(
            string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim(),
            request.ApiKey,
            string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim());

        var (baseUrl, apiKey, model) = _options.Snapshot();

        return Ok(new AiProviderConfigResponseDto
        {
            BaseUrl = baseUrl,
            Model = model,
            HasApiKey = !string.IsNullOrWhiteSpace(apiKey),
            ApiKeyMasked = MaskApiKey(apiKey)
        });
    }

    private static string? MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        if (apiKey.Length <= 8)
            return "****";

        return apiKey[..4] + "****" + apiKey[^4..];
    }
}
