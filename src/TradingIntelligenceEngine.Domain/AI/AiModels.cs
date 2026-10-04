using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.Signal;

namespace TradingIntelligenceEngine.Domain.AI;

public enum AiAction
{
    BUY,
    SELL,
    WAIT
}

public sealed record AiDecision(
    AiAction Decision,
    decimal Confidence,
    IReadOnlyList<string> Reasoning,
    IReadOnlyList<string> Invalidations
);

public sealed record MarketDecisionContext(
    MarketState.MarketState MarketState,
    IReadOnlyList<TradingSignal> SuggestedSignals
);