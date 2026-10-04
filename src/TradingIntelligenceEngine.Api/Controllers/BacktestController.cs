using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using TradingIntelligenceEngine.Api.Models;
using TradingIntelligenceEngine.Domain.Backtesting;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/backtest")]
public class BacktestController : ControllerBase
{
    private readonly IBacktestEngine _engine;

    public BacktestController(IBacktestEngine engine)
    {
        _engine = engine;
    }

    [HttpPost("run")]
    [ProducesResponseType(typeof(BacktestReport), 200)]
    [ProducesResponseType(typeof(string), 400)]
    public IActionResult RunBacktest([FromBody] BacktestRequestDto requestDto)
    {
        if (requestDto == null || requestDto.HistoricalCandles == null || requestDto.WindowSize <= 0)
        {
            return BadRequest("Invalid backtest request.");
        }

        if (!Enum.TryParse<Timeframe>(requestDto.Timeframe, true, out var timeframe))
        {
            return BadRequest($"Invalid timeframe: {requestDto.Timeframe}");
        }

        try
        {
            var domainCandles = requestDto.HistoricalCandles.Select(c => new Candle(
                c.Time, c.Open, c.High, c.Low, c.Close, c.Volume,
                c.EmaFast, c.EmaSlow, c.Rsi, c.Adx, c.Atr
            )).ToList();

            var request = new BacktestRequest
            {
                Symbol = requestDto.Symbol ?? "UNKNOWN",
                Timeframe = timeframe,
                WindowSize = requestDto.WindowSize,
                HistoricalCandles = domainCandles
            };

            var report = _engine.Run(request);

            return Ok(report);
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