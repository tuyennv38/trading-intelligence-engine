using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.TechnicalAnalysis.Indicators;

public static class IndicatorMath
{
    public static decimal CalculateEma(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count == 0) return 0;
        if (values.Count <= period) return values.Average();

        decimal k = 2.0m / (period + 1);
        
        // Start with SMA for the first 'period' elements
        decimal ema = values.Take(period).Average();

        for (int i = period; i < values.Count; i++)
        {
            ema = (values[i] * k) + (ema * (1 - k));
        }

        return ema;
    }

    public static decimal CalculateRsi(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count <= period) return 50m; // Neutral

        decimal gainSum = 0;
        decimal lossSum = 0;

        for (int i = 1; i <= period; i++)
        {
            decimal diff = closes[i] - closes[i - 1];
            if (diff > 0) gainSum += diff;
            else lossSum -= diff;
        }

        decimal avgGain = gainSum / period;
        decimal avgLoss = lossSum / period;

        for (int i = period + 1; i < closes.Count; i++)
        {
            decimal diff = closes[i] - closes[i - 1];
            decimal gain = diff > 0 ? diff : 0;
            decimal loss = diff < 0 ? -diff : 0;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0) return 100m;
        
        decimal rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    public static decimal CalculateAtr(IReadOnlyList<Candle> candles, int period)
    {
        if (candles.Count <= period) return 0;

        decimal trSum = 0;
        // True Range for the first period
        for (int i = 1; i <= period; i++)
        {
            trSum += CalculateTrueRange(candles[i], candles[i - 1]);
        }
        
        decimal atr = trSum / period;

        // Smoothed moving average for the rest
        for (int i = period + 1; i < candles.Count; i++)
        {
            decimal tr = CalculateTrueRange(candles[i], candles[i - 1]);
            atr = ((atr * (period - 1)) + tr) / period;
        }

        return atr;
    }

    private static decimal CalculateTrueRange(Candle current, Candle previous)
    {
        var tr1 = current.High - current.Low;
        var tr2 = Math.Abs(current.High - previous.Close);
        var tr3 = Math.Abs(current.Low - previous.Close);

        return Math.Max(tr1, Math.Max(tr2, tr3));
    }

    public static decimal CalculateAdx(IReadOnlyList<Candle> candles, int period)
    {
        if (candles.Count <= period * 2) return 0;

        // ADX requires smoothed +DI and -DI
        // This is a simplified Wilder's ADX for V1
        decimal[] plusDM = new decimal[candles.Count];
        decimal[] minusDM = new decimal[candles.Count];
        decimal[] tr = new decimal[candles.Count];

        for (int i = 1; i < candles.Count; i++)
        {
            var highDiff = candles[i].High - candles[i - 1].High;
            var lowDiff = candles[i - 1].Low - candles[i].Low;

            if (highDiff > lowDiff && highDiff > 0) plusDM[i] = highDiff;
            if (lowDiff > highDiff && lowDiff > 0) minusDM[i] = lowDiff;

            tr[i] = CalculateTrueRange(candles[i], candles[i - 1]);
        }

        decimal smoothedTR = tr.Skip(1).Take(period).Sum();
        decimal smoothedPlusDM = plusDM.Skip(1).Take(period).Sum();
        decimal smoothedMinusDM = minusDM.Skip(1).Take(period).Sum();

        decimal[] dx = new decimal[candles.Count];

        for (int i = period + 1; i < candles.Count; i++)
        {
            smoothedTR = smoothedTR - (smoothedTR / period) + tr[i];
            smoothedPlusDM = smoothedPlusDM - (smoothedPlusDM / period) + plusDM[i];
            smoothedMinusDM = smoothedMinusDM - (smoothedMinusDM / period) + minusDM[i];

            decimal plusDI = smoothedTR == 0 ? 0 : 100 * smoothedPlusDM / smoothedTR;
            decimal minusDI = smoothedTR == 0 ? 0 : 100 * smoothedMinusDM / smoothedTR;

            decimal diDiff = Math.Abs(plusDI - minusDI);
            decimal diSum = plusDI + minusDI;
            
            dx[i] = diSum == 0 ? 0 : 100 * diDiff / diSum;
        }

        decimal adx = dx.Skip(period + 1).Take(period).Average();

        for (int i = period * 2 + 1; i < candles.Count; i++)
        {
            adx = ((adx * (period - 1)) + dx[i]) / period;
        }

        return adx;
    }
}