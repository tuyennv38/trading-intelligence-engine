using System;
using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.MarketStructure.Utils;

public static class AtrCalculator
{
    public static decimal Calculate(IReadOnlyList<Candle> candles, int index, int period = 14)
    {
        if (index < period)
        {
            // Fallback for early candles: average of High-Low
            decimal sum = 0;
            for (int i = 0; i <= index; i++)
            {
                sum += candles[i].High - candles[i].Low;
            }
            return sum / (index + 1);
        }

        decimal trSum = 0;
        for (int i = index - period + 1; i <= index; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;

            var tr1 = high - low;
            var tr2 = Math.Abs(high - prevClose);
            var tr3 = Math.Abs(low - prevClose);

            var tr = Math.Max(tr1, Math.Max(tr2, tr3));
            trSum += tr;
        }

        return trSum / period;
    }
}