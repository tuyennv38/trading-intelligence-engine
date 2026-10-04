using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.TechnicalAnalysis.Indicators;

namespace TradingIntelligenceEngine.TechnicalAnalysis;

public class TechnicalAnalyzer : ITechnicalAnalyzer
{
    public TechnicalState Analyze(IReadOnlyList<Candle> candles, IndicatorOptions options)
    {
        var closes = candles.Select(c => c.Close).ToList();

        decimal emaFast = IndicatorMath.CalculateEma(closes, options.EmaFast);
        decimal emaSlow = IndicatorMath.CalculateEma(closes, options.EmaSlow);
        decimal rsi = IndicatorMath.CalculateRsi(closes, options.RsiPeriod);
        decimal atr = IndicatorMath.CalculateAtr(candles, options.AtrPeriod);
        decimal adx = IndicatorMath.CalculateAdx(candles, options.AdxPeriod);
        
        // ATR Percentile can be calculated historically. 
        // For V1, we return 0 or calculate a simple ratio if needed, but it's optional.
        decimal atrPercentile = 0m; 

        return new TechnicalState(
            EmaFast: emaFast,
            EmaSlow: emaSlow,
            Rsi: rsi,
            Atr: atr,
            Adx: adx,
            AtrPercentile: atrPercentile
        );
    }
}