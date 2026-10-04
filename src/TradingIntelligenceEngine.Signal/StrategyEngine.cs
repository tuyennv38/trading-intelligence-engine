using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Signal;
using TradingIntelligenceEngine.Domain.MarketState;

namespace TradingIntelligenceEngine.Signal;

public class StrategyEngine : IStrategyEngine
{
    private readonly IEnumerable<ITradingStrategy> _strategies;

    public StrategyEngine(IEnumerable<ITradingStrategy> strategies)
    {
        _strategies = strategies;
    }

    public IReadOnlyList<TradingSignal> EvaluateAll(MarketState state)
    {
        var signals = new List<TradingSignal>();

        foreach (var strategy in _strategies)
        {
            var signal = strategy.Evaluate(state);
            if (signal != null && signal.Direction != SignalDirection.Neutral)
            {
                signals.Add(signal);
            }
        }

        return signals;
    }
}