using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Application.Services;

public class MarketAnalyzerService : IMarketAnalyzer
{
    private readonly ISwingDetector _swingDetector;
    private readonly IStructureAnalyzer _structureAnalyzer;
    private readonly IStructureEventDetector _eventDetector;
    private readonly ITechnicalAnalyzer _technicalAnalyzer;
    private readonly ILiquidityAnalyzer _liquidityAnalyzer;
    private readonly IRegimeAnalyzer _regimeAnalyzer;
    
    private readonly SwingOptions _swingOptions;
    private readonly BreakOptions _breakOptions;
    private readonly LiquidityOptions _liquidityOptions;
    private readonly IndicatorOptions _indicatorOptions;

    public MarketAnalyzerService(
        ISwingDetector swingDetector,
        IStructureAnalyzer structureAnalyzer,
        IStructureEventDetector eventDetector,
        ITechnicalAnalyzer technicalAnalyzer,
        ILiquidityAnalyzer liquidityAnalyzer,
        IRegimeAnalyzer regimeAnalyzer,
        SwingOptions swingOptions,
        BreakOptions breakOptions,
        LiquidityOptions liquidityOptions,
        IndicatorOptions indicatorOptions)
    {
        _swingDetector = swingDetector;
        _structureAnalyzer = structureAnalyzer;
        _eventDetector = eventDetector;
        _technicalAnalyzer = technicalAnalyzer;
        _liquidityAnalyzer = liquidityAnalyzer;
        _regimeAnalyzer = regimeAnalyzer;
        
        _swingOptions = swingOptions;
        _breakOptions = breakOptions;
        _liquidityOptions = liquidityOptions;
        _indicatorOptions = indicatorOptions;
    }

    public MarketState Analyze(MarketAnalysisRequest request)
    {
        if (request.Candles == null || request.Candles.Count == 0)
        {
            throw new ArgumentException("Candles cannot be empty.");
        }

        // 1. Structure Detection Pipeline
        var swings = _swingDetector.Detect(request.Candles, _swingOptions);
        var structurePoints = _structureAnalyzer.Analyze(swings);
        var events = _eventDetector.Detect(request.Candles, structurePoints, _breakOptions);

        // 2. Context Detection Pipeline
        var technical = _technicalAnalyzer.Analyze(request.Candles, _indicatorOptions);
        var liquidity = _liquidityAnalyzer.Analyze(request.Candles, swings, _liquidityOptions);
        
        var lastStructure = structurePoints.LastOrDefault(s => s.Swing.Status != SwingStatus.Invalidated);
        var regime = _regimeAnalyzer.Analyze(events, technical, lastStructure, _indicatorOptions);

        // 3. Assemble MarketState
        EventDirection trendDirection = EventDirection.Bullish;
        if (regime.Type == MarketRegimeType.TrendingDown) trendDirection = EventDirection.Bearish;
        else if (regime.Type == MarketRegimeType.TrendingUp) trendDirection = EventDirection.Bullish;
        else 
        {
            // If ranging/transition, fallback to the last structure label or event
            if (lastStructure?.Label == StructureLabel.LH || lastStructure?.Label == StructureLabel.LL)
                trendDirection = EventDirection.Bearish;
            else if (lastStructure?.Label == StructureLabel.HH || lastStructure?.Label == StructureLabel.HL)
                trendDirection = EventDirection.Bullish;
        }

        var trendState = new TrendState(trendDirection, regime.Confidence);
        
        var structureState = new StructureState(
            LastLabel: lastStructure?.Label ?? StructureLabel.HH,
            Sequence: structurePoints
                .Where(s => s.Swing.Status != SwingStatus.Invalidated)
                .TakeLast(5) // Just keep the recent sequence for context
                .Select(s => s.Label.ToString())
                .ToList()
        );

        return new MarketState
        {
            Symbol = request.Symbol,
            Timeframe = request.Timeframe,
            CurrentPrice = request.Candles.Last().Close,
            Trend = trendState,
            Structure = structureState,
            Events = events,
            Liquidity = liquidity,
            Technical = technical,
            Regime = regime
        };
    }
}