using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.Configuration;

namespace TradingIntelligenceEngine.Domain.MarketStructure;

public interface ISwingDetector
{
    IReadOnlyList<SwingPoint> Detect(
        IReadOnlyList<Candle> candles,
        SwingOptions options);
}

public interface IStructureAnalyzer
{
    IReadOnlyList<StructurePoint> Analyze(
        IReadOnlyList<SwingPoint> swings);
}

public interface IStructureEventDetector
{
    IReadOnlyList<StructureEvent> Detect(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<StructurePoint> structure,
        BreakOptions options);
}