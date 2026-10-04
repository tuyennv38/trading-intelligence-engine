using System.Linq;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.MarketStructure;
using TradingIntelligenceEngine.Domain.Signal;

namespace TradingIntelligenceEngine.Signal.Strategies;

public class TrendFollowingPullbackStrategy : ITradingStrategy
{
    public string Name => "TrendFollowingPullback";

    public TradingSignal Evaluate(MarketState state)
    {
        // Require a clear trend
        if (state.Regime == null || state.Technical == null || state.Structure == null)
            return new TradingSignal(SignalDirection.Neutral, Name, 0, 0, null, null, "Missing context");

        var currentPrice = state.Technical.EmaFast; // Just an approximation of current price if we don't have the last candle directly in MarketState. 
        // Actually, we could extract current price from the latest event or liquidity sweep, but let's just use the last confirmed structure price for now.
        // Wait, MarketState doesn't have CurrentPrice directly. Let's add it to MarketState later or pass it. 
        // We'll use EMA Fast as a baseline or just logic over Structure.

        if (state.Regime.Type == MarketRegimeType.TrendingUp)
        {
            // Look for a recent pullback
            // e.g. Last structure is HL (Higher Low)
            if (state.Structure.LastLabel == StructureLabel.HL)
            {
                // Basic scoring
                int score = 60 + (int)(state.Regime.Confidence * 20); // up to 80
                
                return new TradingSignal(
                    Direction: SignalDirection.Long,
                    Setup: "BullishPullback (HL)",
                    Score: score,
                    EntryPrice: state.CurrentPrice, 
                    SuggestedStopLoss: state.CurrentPrice - (state.Technical.Atr * 2), // 2 ATR Stop
                    SuggestedTakeProfit: state.CurrentPrice + (state.Technical.Atr * 4), // 1:2 RR
                    Reasoning: "Market is Trending Up and formed a Higher Low."
                );
            }
        }
        else if (state.Regime.Type == MarketRegimeType.TrendingDown)
        {
            if (state.Structure.LastLabel == StructureLabel.LH)
            {
                int score = 60 + (int)(state.Regime.Confidence * 20);
                
                return new TradingSignal(
                    Direction: SignalDirection.Short,
                    Setup: "BearishPullback (LH)",
                    Score: score,
                    EntryPrice: state.CurrentPrice,
                    SuggestedStopLoss: state.CurrentPrice + (state.Technical.Atr * 2),
                    SuggestedTakeProfit: state.CurrentPrice - (state.Technical.Atr * 4),
                    Reasoning: "Market is Trending Down and formed a Lower High."
                );
            }
        }

        return new TradingSignal(SignalDirection.Neutral, Name, 0, 0, null, null, "No clear setup");
    }
}