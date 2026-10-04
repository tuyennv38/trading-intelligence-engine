using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using TradingIntelligenceEngine.Api.Models;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.Signal;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/ai")]
public class AiDecisionController : ControllerBase
{
    private readonly IMarketAnalyzer _analyzer;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IAiDecisionEngine _aiEngine;

    public AiDecisionController(
        IMarketAnalyzer analyzer,
        IStrategyEngine strategyEngine,
        IAiDecisionEngine aiEngine)
    {
        _analyzer = analyzer;
        _strategyEngine = strategyEngine;
        _aiEngine = aiEngine;
    }

    [HttpPost("decision")]
    [ProducesResponseType(typeof(AiDecision), 200)]
    [ProducesResponseType(typeof(string), 400)]
    public async Task<IActionResult> GetAiDecision([FromBody] MarketAnalysisRequestDto requestDto, CancellationToken cancellationToken)
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
                c.Time, c.Open, c.High, c.Low, c.Close, c.Volume,
                c.EmaFast, c.EmaSlow, c.Rsi, c.Adx, c.Atr
            )).ToList();

            var request = new MarketAnalysisRequest(requestDto.Symbol, timeframe, domainCandles);
            
            // 1. Analyze Market State
            var state = _analyzer.Analyze(request);

            // 2. Evaluate Strategy Signals
            var signals = _strategyEngine.EvaluateAll(state);

            // 3. Ask AI
            var context = new MarketDecisionContext(state, signals);
            var decision = await _aiEngine.DecideAsync(context, cancellationToken);

            return Ok(decision);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Internal server error: " + ex.Message + "\n" + ex.StackTrace);
        }
    }
}