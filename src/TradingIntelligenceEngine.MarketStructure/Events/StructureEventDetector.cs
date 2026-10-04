using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;

using TradingIntelligenceEngine.Domain.Configuration;

namespace TradingIntelligenceEngine.MarketStructure.Events;

public class StructureEventDetector : IStructureEventDetector
{
    public IReadOnlyList<StructureEvent> Detect(IReadOnlyList<Candle> candles, IReadOnlyList<StructurePoint> structure, BreakOptions options)
    {
        var events = new List<StructureEvent>();

        // Find the overall trend from structure to know whether we are looking for BOS or CHOCH
        // A simple approach is scanning candles and maintaining the active Higher High / Higher Low / Lower High / Lower Low.

        StructurePoint? activeHigh = null;
        StructurePoint? activeLow = null;
        
        // Track the current structure direction: Bullish or Bearish
        EventDirection? currentTrend = null;

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];

            // 1. Update active structure points that are confirmed at this candle
            // A swing is confirmed at Index + Strength
            var confirmedPoints = structure.Where(sp => sp.Swing.Index + sp.Swing.Strength == i).ToList();

            foreach (var sp in confirmedPoints)
            {
                if (sp.Swing.Type == SwingType.High)
                {
                    activeHigh = sp;
                    if (sp.Label == StructureLabel.HH)
                    {
                        currentTrend = EventDirection.Bullish;
                    }
                    else if (sp.Label == StructureLabel.LH)
                    {
                        currentTrend = EventDirection.Bearish;
                    }
                }
                else
                {
                    activeLow = sp;
                    if (sp.Label == StructureLabel.LL)
                    {
                        currentTrend = EventDirection.Bearish;
                    }
                    else if (sp.Label == StructureLabel.HL)
                    {
                        currentTrend = EventDirection.Bullish;
                    }
                }
            }

            // 2. Check for breaks
            decimal currentBullishBreakPrice = options.Confirmation == BreakConfirmationType.Close ? candle.Close : candle.High;
            decimal currentBearishBreakPrice = options.Confirmation == BreakConfirmationType.Close ? candle.Close : candle.Low;

            if (currentTrend == EventDirection.Bullish)
            {
                // In bullish trend, we look for Bullish BOS (break of activeHigh) or Bearish CHOCH (break of activeLow)
                
                // Check BOS Bullish
                if (activeHigh != null && currentBullishBreakPrice > activeHigh.Swing.Price)
                {
                    // Add event, then clear activeHigh so we don't trigger it again on the next candle
                    events.Add(new StructureEvent(
                        StructureEventType.BOS,
                        EventDirection.Bullish,
                        activeHigh.Swing.Price,
                        i,
                        candle.Time,
                        "Confirmed"
                    ));
                    activeHigh = null; 
                }
                
                // Check CHOCH Bearish
                if (activeLow != null && currentBearishBreakPrice < activeLow.Swing.Price)
                {
                    events.Add(new StructureEvent(
                        StructureEventType.CHOCH,
                        EventDirection.Bearish,
                        activeLow.Swing.Price,
                        i,
                        candle.Time,
                        "Confirmed"
                    ));
                    activeLow = null;
                    currentTrend = EventDirection.Bearish; // Shift trend
                }
            }
            else if (currentTrend == EventDirection.Bearish)
            {
                // In bearish trend, we look for Bearish BOS (break of activeLow) or Bullish CHOCH (break of activeHigh)

                // Check BOS Bearish
                if (activeLow != null && currentBearishBreakPrice < activeLow.Swing.Price)
                {
                    events.Add(new StructureEvent(
                        StructureEventType.BOS,
                        EventDirection.Bearish,
                        activeLow.Swing.Price,
                        i,
                        candle.Time,
                        "Confirmed"
                    ));
                    activeLow = null;
                }

                // Check CHOCH Bullish
                if (activeHigh != null && currentBullishBreakPrice > activeHigh.Swing.Price)
                {
                    events.Add(new StructureEvent(
                        StructureEventType.CHOCH,
                        EventDirection.Bullish,
                        activeHigh.Swing.Price,
                        i,
                        candle.Time,
                        "Confirmed"
                    ));
                    activeHigh = null;
                    currentTrend = EventDirection.Bullish; // Shift trend
                }
            }
        }

        return events;
    }
}