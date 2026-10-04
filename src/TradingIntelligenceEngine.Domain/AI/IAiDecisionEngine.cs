using System.Threading;
using System.Threading.Tasks;

namespace TradingIntelligenceEngine.Domain.AI;

public sealed record AiDecisionResult(
    AiDecision Decision,
    string Prompt,
    string RawResponse,
    long LatencyMs
);

public interface IAiDecisionEngine
{
    Task<AiDecisionResult> DecideAsync(
        MarketDecisionContext context,
        CancellationToken cancellationToken);
}