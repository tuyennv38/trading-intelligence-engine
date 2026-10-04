using System.Threading;
using System.Threading.Tasks;

namespace TradingIntelligenceEngine.Domain.AI;

public interface IAiDecisionEngine
{
    Task<AiDecision> DecideAsync(
        MarketDecisionContext context,
        CancellationToken cancellationToken);
}