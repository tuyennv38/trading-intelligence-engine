using System.Collections.Generic;

namespace TradingIntelligenceEngine.Domain.Signal;

public interface ITradingStrategy
{
    string Name { get; }
    TradingSignal Evaluate(MarketState.MarketState state);
}

public interface IStrategyEngine
{
    IReadOnlyList<TradingSignal> EvaluateAll(MarketState.MarketState state);
}