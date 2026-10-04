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
    private readonly Services.IClickhouseLogger _clickhouseLogger;

    public AiDecisionController(
        IMarketAnalyzer analyzer,
        IStrategyEngine strategyEngine,
        IAiDecisionEngine aiEngine,
        Services.IClickhouseLogger clickhouseLogger)
    {
        _analyzer = analyzer;
        _strategyEngine = strategyEngine;
        _aiEngine = aiEngine;
        _clickhouseLogger = clickhouseLogger;
    }

    [HttpPost("decision")]
    [ProducesResponseType(typeof(AiDecisionResult), 200)]
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
            var decisionResult = await _aiEngine.DecideAsync(context, cancellationToken);

            var sessionId = requestDto.SessionId ?? Guid.NewGuid();
            var requestJson = System.Text.Json.JsonSerializer.Serialize(requestDto);
            await _clickhouseLogger.LogDecisionAsync(sessionId, requestDto.Symbol, requestDto.Timeframe, requestJson, state, decisionResult);

            return Ok(decisionResult);
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

    [HttpPost("feedback")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(string), 400)]
    public async Task<IActionResult> LogFeedback([FromBody] AiFeedbackRequestDto feedbackDto)
    {
        if (feedbackDto == null || feedbackDto.SessionId == Guid.Empty)
        {
            return BadRequest("Invalid feedback request.");
        }

        try
        {
            await _clickhouseLogger.LogOutcomeAsync(
                feedbackDto.SessionId,
                feedbackDto.Outcome,
                feedbackDto.PnlPips,
                feedbackDto.ExitReason
            );

            return Ok(new { message = "Feedback logged successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Internal server error: " + ex.Message);
        }
    }
}