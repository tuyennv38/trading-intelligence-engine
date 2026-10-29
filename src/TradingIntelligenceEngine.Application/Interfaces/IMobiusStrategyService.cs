using System.Threading;
using System.Threading.Tasks;
using TradingIntelligenceEngine.Application.DTOs.Mobius;

namespace TradingIntelligenceEngine.Application.Interfaces;

/// <summary>
/// Service cầu nối gọi sang Mobius Strategy Scenarios API (ICT/SMC).
/// </summary>
public interface IMobiusStrategyService
{
    Task<MobiusScenarioResponse> FetchAndApplyScenariosAsync(
        MobiusScenarioRequest request,
        bool? notifyOverride = null,
        CancellationToken cancellationToken = default);
}
