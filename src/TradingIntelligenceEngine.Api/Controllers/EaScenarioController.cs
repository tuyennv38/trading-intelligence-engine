using System;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/scenarios")]
public class EaScenarioController : ControllerBase
{
    // Giả lập lưu trữ kịch bản trong bộ nhớ (In-memory store)
    // Thực tế bạn có thể lưu vào Redis hoặc Database để không bị mất khi restart app
    private static readonly ConcurrentDictionary<string, JsonElement> _scenarios = new();
    private static string _latestSignalId = string.Empty;

    /// <summary>
    /// API 1: Lấy ID của kịch bản mới nhất (EA gọi liên tục hàm này để check)
    /// GET /api/v1/scenarios/latest-id
    /// </summary>
    [HttpGet("latest-id")]
    [ProducesResponseType(200)]
    public IActionResult GetLatestScenarioId([FromQuery] string symbol = "SPX500")
    {
        // Bạn có thể mở rộng thêm logic lọc theo Symbol
        if (string.IsNullOrEmpty(_latestSignalId))
        {
            return Ok(new { signal_id = "", has_new = false });
        }

        return Ok(new { signal_id = _latestSignalId, has_new = true });
    }

    /// <summary>
    /// API 2: Lấy chi tiết JSON kịch bản dựa vào signal_id (EA gọi khi thấy ID mới)
    /// GET /api/v1/scenarios/{signalId}
    /// </summary>
    [HttpGet("{signalId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult GetScenarioDetails(string signalId)
    {
        if (_scenarios.TryGetValue(signalId, out var scenario))
        {
            return Ok(scenario);
        }

        return NotFound(new { error = $"Scenario with ID '{signalId}' not found" });
    }

    /// <summary>
    /// API 3 (Dành cho AI): Post kịch bản mới lên hệ thống sau khi phân tích xong
    /// POST /api/v1/scenarios
    /// </summary>
    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public IActionResult SaveScenario([FromBody] JsonElement scenarioPayload)
    {
        try
        {
            // Trích xuất signal_id từ cục JSON AI trả về
            if (scenarioPayload.TryGetProperty("signal_id", out var idProperty))
            {
                var signalId = idProperty.GetString();
                
                if (!string.IsNullOrEmpty(signalId))
                {
                    // Lưu vào Dictionary
                    _scenarios[signalId] = scenarioPayload;
                    _latestSignalId = signalId;

                    return Ok(new 
                    { 
                        message = "Scenario saved successfully", 
                        signal_id = signalId 
                    });
                }
            }

            return BadRequest(new { error = "Invalid JSON format. Missing 'signal_id' property." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Internal server error: " + ex.Message });
        }
    }
}
