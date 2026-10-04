using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using TradingIntelligenceEngine.Api.Models;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.Signal;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/signals")]
public class SignalController : ControllerBase
{
    private readonly IMarketAnalyzer _analyzer;
    private readonly IStrategyEngine _strategyEngine;

    public SignalController(IMarketAnalyzer analyzer, IStrategyEngine strategyEngine)
    {
        _analyzer = analyzer;
        _strategyEngine = strategyEngine;
    }

    [HttpPost("analyze")]
    [ProducesResponseType(typeof(System.Collections.Generic.IReadOnlyList<TradingSignal>), 200)]
    [ProducesResponseType(typeof(string), 400)]
    public IActionResult AnalyzeSignals([FromBody] MarketAnalysisRequestDto requestDto)
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
            
            // First analyze the market state
            var state = _analyzer.Analyze(request);

            // Then pass to strategy engine
            var signals = _strategyEngine.EvaluateAll(state);

            return Ok(signals);
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