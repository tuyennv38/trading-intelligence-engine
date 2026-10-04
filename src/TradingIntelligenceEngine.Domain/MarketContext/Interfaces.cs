using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;
using TradingIntelligenceEngine.Domain.Configuration;

namespace TradingIntelligenceEngine.Domain.MarketContext;

public interface ILiquidityAnalyzer
{
    LiquidityState Analyze(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<SwingPoint> swings,
        LiquidityOptions options);
}

public interface ISupportResistanceAnalyzer
{
    IReadOnlyList<PriceZone> Analyze(
        IReadOnlyList<SwingPoint> swings,
        decimal atrTolerance);
}

public interface ITechnicalAnalyzer
{
    TechnicalState Analyze(
        IReadOnlyList<Candle> candles,
        IndicatorOptions options);
}

public interface IRegimeAnalyzer
{
    RegimeState Analyze(
        IReadOnlyList<StructureEvent> events,
        TechnicalState technical,
        StructurePoint lastStructure,
        IndicatorOptions options);
}