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

        var lastCandle = candles.LastOrDefault();
        
        // If the client provided pre-calculated indicators on the last candle, use them directly
        // This is highly recommended for production bots taking feeds from MT4/Binance to save CPU and ensure perfect sync.
        decimal emaFast = lastCandle?.EmaFast ?? IndicatorMath.CalculateEma(closes, options.EmaFast);
        decimal emaSlow = lastCandle?.EmaSlow ?? IndicatorMath.CalculateEma(closes, options.EmaSlow);
        decimal rsi = lastCandle?.Rsi ?? IndicatorMath.CalculateRsi(closes, options.RsiPeriod);
        decimal atr = lastCandle?.Atr ?? IndicatorMath.CalculateAtr(candles, options.AtrPeriod);
        decimal adx = lastCandle?.Adx ?? IndicatorMath.CalculateAdx(candles, options.AdxPeriod);
        
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