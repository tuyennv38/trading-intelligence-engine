using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;
using TradingIntelligenceEngine.MarketStructure.Utils;

namespace TradingIntelligenceEngine.MarketStructure.Swings;

public class SwingDetector : ISwingDetector
{
    public IReadOnlyList<SwingPoint> Detect(IReadOnlyList<Candle> candles, SwingOptions options)
    {
        var swings = new List<SwingPoint>();
        int n = options.FractalStrength;

        for (int i = n; i < candles.Count; i++)
        {
            // We need n candles after i to confirm a swing.
            // If i + n >= candles.Count, it's a potential swing.
            bool isPotential = i + n >= candles.Count;
            
            // Check Swing High
            bool isHigh = true;
            for (int j = 1; j <= n; j++)
            {
                if (candles[i].High <= candles[i - j].High)
                {
                    isHigh = false;
                    break;
                }
                
                // For candles after i, only check if they exist
                if (i + j < candles.Count && candles[i].High < candles[i + j].High)
                {
                    isHigh = false;
                    break;
                }
            }

            if (isHigh)
            {
                var atr = AtrCalculator.Calculate(candles, i);
                var minDistance = atr * options.MinAtrMultiplier;
                
                bool isValid = true;
                if (swings.Count > 0)
                {
                    var lastSwing = swings.Last();
                    if (lastSwing.Type == SwingType.Low)
                    {
                        if (candles[i].High - lastSwing.Price < minDistance)
                        {
                            isValid = false; // Filtered by adaptive ATR
                        }
                    }
                    else // Last swing was also a High
                    {
                        // If it's higher than the last high, it replaces it (if last was invalidated or we merge them)
                        // For simplicity in V1, we just accept it if distance is ok, or we mark the previous as Invalidated.
                        // The spec says: "Một confirmed swing không được âm thầm thay đổi... Nếu cần thay đổi phải có trạng thái Invalidated"
                    }
                }

                if (isValid)
                {
                    swings.Add(new SwingPoint(
                        Index: i,
                        Time: candles[i].Time,
                        Type: SwingType.High,
                        Price: candles[i].High,
                        Strength: n,
                        Status: isPotential ? SwingStatus.Potential : SwingStatus.Confirmed
                    ));
                }
            }

            // Check Swing Low
            bool isLow = true;
            for (int j = 1; j <= n; j++)
            {
                if (candles[i].Low >= candles[i - j].Low)
                {
                    isLow = false;
                    break;
                }
                
                if (i + j < candles.Count && candles[i].Low > candles[i + j].Low)
                {
                    isLow = false;
                    break;
                }
            }

            if (isLow)
            {
                var atr = AtrCalculator.Calculate(candles, i);
                var minDistance = atr * options.MinAtrMultiplier;
                
                bool isValid = true;
                if (swings.Count > 0)
                {
                    var lastSwing = swings.Last();
                    if (lastSwing.Type == SwingType.High)
                    {
                        if (lastSwing.Price - candles[i].Low < minDistance)
                        {
                            isValid = false;
                        }
                    }
                }

                if (isValid)
                {
                    swings.Add(new SwingPoint(
                        Index: i,
                        Time: candles[i].Time,
                        Type: SwingType.Low,
                        Price: candles[i].Low,
                        Strength: n,
                        Status: isPotential ? SwingStatus.Potential : SwingStatus.Confirmed
                    ));
                }
            }
        }

        // Clean up consecutive same-type swings (ZigZag behavior)
        // We ensure strict alternation between High and Low.
        var filteredSwings = new List<SwingPoint>();
        SwingPoint? lastValid = null;

        foreach (var current in swings)
        {
            if (lastValid == null)
            {
                filteredSwings.Add(current);
                lastValid = current;
                continue;
            }

            if (current.Type == lastValid.Type)
            {
                // Consecutive same type. Keep the more extreme one.
                bool shouldReplace = (current.Type == SwingType.High && current.Price >= lastValid.Price) ||
                                     (current.Type == SwingType.Low && current.Price <= lastValid.Price);

                if (shouldReplace)
                {
                    // Invalidate the previous one
                    int lastIndex = filteredSwings.FindLastIndex(s => s == lastValid);
                    if (lastIndex >= 0)
                    {
                        filteredSwings[lastIndex] = filteredSwings[lastIndex] with { Status = SwingStatus.Invalidated };
                    }
                    filteredSwings.Add(current);
                    lastValid = current;
                }
                else
                {
                    // Current is invalidated
                    filteredSwings.Add(current with { Status = SwingStatus.Invalidated });
                }
            }
            else
            {
                // Different type, valid alternation
                filteredSwings.Add(current);
                lastValid = current;
            }
        }

        return filteredSwings;
    }
}