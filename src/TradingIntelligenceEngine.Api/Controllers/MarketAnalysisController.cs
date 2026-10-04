using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using TradingIntelligenceEngine.Api.Models;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/market-analysis")]
public class MarketAnalysisController : ControllerBase
{
    private readonly IMarketAnalyzer _analyzer;

    public MarketAnalysisController(IMarketAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    [HttpPost("analyze")]
    [ProducesResponseType(typeof(MarketState), 200)]
    [ProducesResponseType(typeof(string), 400)]
    public IActionResult Analyze([FromBody] MarketAnalysisRequestDto requestDto)
    {
        if (requestDto == null || string.IsNullOrWhiteSpace(requestDto.Symbol) || requestDto.Candles == null)
        {
            return BadRequest("Invalid request.");
        }

        if (!Enum.TryParse<Timeframe>(requestDto.Timeframe, true, out var timeframe))
        {
            return BadRequest($"Invalid timeframe: {requestDto.Timeframe}");
        }

        try
        {
            var domainCandles = requestDto.Candles.Select(c => new Candle(
                c.Time, c.Open, c.High, c.Low, c.Close, c.Volume
            )).ToList();

            var request = new MarketAnalysisRequest(requestDto.Symbol, timeframe, domainCandles);
            var state = _analyzer.Analyze(request);

            return Ok(state);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Internal server error: " + ex.Message);
        }
    }
}