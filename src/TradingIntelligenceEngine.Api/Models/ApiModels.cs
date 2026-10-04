using System;
using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Api.Models;

public record CandleDto(
    DateTimeOffset Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume
);

public record MarketAnalysisRequestDto(
    string Symbol,
    string Timeframe,
    List<CandleDto> Candles
);