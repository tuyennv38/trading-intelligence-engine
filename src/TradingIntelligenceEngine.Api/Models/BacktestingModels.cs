using System;
using System.Collections.Generic;

namespace TradingIntelligenceEngine.Api.Models;

public record BacktestRequestDto(
    string Symbol,
    string Timeframe,
    int WindowSize,
    List<CandleDto> HistoricalCandles
);