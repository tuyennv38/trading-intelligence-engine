using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Regime;

public class RegimeAnalyzer : IRegimeAnalyzer
{
    public RegimeState Analyze(
        IReadOnlyList<StructureEvent> events, 
        TechnicalState technical, 
        StructurePoint? lastStructure, 
        IndicatorOptions options)
    {
        // Default base confidence
        decimal confidence = 0.5m;
        
        bool isEmaBullish = technical.EmaFast > technical.EmaSlow;
        bool isEmaBearish = technical.EmaFast < technical.EmaSlow;
        bool isTrendingStrength = technical.Adx >= options.TrendingAdxThreshold;
        
        var lastEvent = events.LastOrDefault();

        // Check Transition (CHOCH just happened)
        if (lastEvent != null && lastEvent.Type == StructureEventType.CHOCH)
        {
            return new RegimeState(MarketRegimeType.Transition, 0.7m);
        }

        // Check TrendingUp
        // Bullish structure (lastStructure HH or HL) + EMA aligned + ADX strong
        bool isStructureBullish = lastStructure?.Label == StructureLabel.HH || lastStructure?.Label == StructureLabel.HL;
        if (isStructureBullish && isEmaBullish && isTrendingStrength)
        {
            confidence = 0.6m + (technical.Adx > options.TrendingAdxThreshold + 10 ? 0.2m : 0m); // simple boost
            return new RegimeState(MarketRegimeType.TrendingUp, confidence);
        }

        // Check TrendingDown
        bool isStructureBearish = lastStructure?.Label == StructureLabel.LH || lastStructure?.Label == StructureLabel.LL;
        if (isStructureBearish && isEmaBearish && isTrendingStrength)
        {
            confidence = 0.6m + (technical.Adx > options.TrendingAdxThreshold + 10 ? 0.2m : 0m);
            return new RegimeState(MarketRegimeType.TrendingDown, confidence);
        }

        // If ADX is low and structure is unclear or conflicting
        if (!isTrendingStrength)
        {
            return new RegimeState(MarketRegimeType.Ranging, 0.8m); // Confident it's ranging if ADX < threshold
        }

        // If signals conflict (e.g. ADX high but structure vs EMA oppose)
        return new RegimeState(MarketRegimeType.Transition, 0.5m);
    }
}