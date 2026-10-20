using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Api.Jobs;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;

namespace TradingIntelligenceEngine.Api.Services;

public interface ITradingPlanRefreshService
{
    Task<(bool Queued, string Message)> EnqueueRefreshAsync(string symbol, string triggerReason);
}

public class TradingPlanRefreshService : ITradingPlanRefreshService
{
    private readonly IMarketDataStore _marketDataStore;
    private readonly IMarketAnalyzer _marketAnalyzer;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TradingPlanRefreshService> _logger;

    public TradingPlanRefreshService(
        IMarketDataStore marketDataStore,
        IMarketAnalyzer marketAnalyzer,
        IBackgroundJobClient backgroundJobClient,
        INotificationService notificationService,
        ILogger<TradingPlanRefreshService> logger)
    {
        _marketDataStore = marketDataStore;
        _marketAnalyzer = marketAnalyzer;
        _backgroundJobClient = backgroundJobClient;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<(bool Queued, string Message)> EnqueueRefreshAsync(string symbol, string triggerReason)
    {
        var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
        if (session == null || session.Timeframes == null || session.Timeframes.Count == 0)
        {
            return (false, "Chưa có session initialize. Hãy gọi /initialize trước.");
        }

        if (session.PlanRefreshInProgress)
            return (false, "AI đang tìm kịch bản. EA tiếp tục update nến, poll lại sau.");

        session.PlanRefreshInProgress = true;
        await _marketDataStore.SetSymbolSessionAsync(symbol, session);

        var useAiStructure = session.UseAiStructure;
        var candlesByTimeframe = new Dictionary<string, List<AiCandleInput>>();
        var analysisResults = new Dictionary<string, MarketState>();

        foreach (var tf in session.Timeframes)
        {
            var cached = await _marketDataStore.GetCandlesAsync(symbol, tf);
            if (cached == null || cached.Count == 0)
                continue;

            candlesByTimeframe[tf] = cached.Select(c => new AiCandleInput(
                c.Time, c.Open, c.High, c.Low, c.Close, c.TickVolume,
                c.EmaFast, c.EmaSlow, c.Rsi, c.Atr, c.Adx)).ToList();

            if (!useAiStructure && Enum.TryParse<Timeframe>(tf, true, out var timeframeEnum))
            {
                var domainCandles = cached.Select(c => new Candle(
                    time: DateTimeOffset.FromUnixTimeSeconds(c.Time),
                    open: c.Open,
                    high: c.High,
                    low: c.Low,
                    close: c.Close,
                    volume: c.TickVolume,
                    emaFast: c.EmaFast,
                    emaSlow: c.EmaSlow,
                    rsi: c.Rsi,
                    adx: c.Adx,
                    atr: c.Atr
                )).ToList();

                analysisResults[tf] = _marketAnalyzer.Analyze(
                    new MarketAnalysisRequest(symbol, timeframeEnum, domainCandles));
            }
        }

        if (candlesByTimeframe.Count == 0)
        {
            session.PlanRefreshInProgress = false;
            await _marketDataStore.SetSymbolSessionAsync(symbol, session);
            return (false, "Không có nến trong store để refresh plan.");
        }

        _logger.LogInformation(
            "Queue plan refresh for {Symbol}. useAiStructure={UseAi}. Reason={Reason}",
            symbol, useAiStructure, triggerReason);

        await _notificationService.SendMessageAsync(
            $"🔄 *{symbol}* xin kịch bản mới\nMode cấu trúc: {(useAiStructure ? "AI" : "Quant")}\n⏳ Đang xếp hàng Hangfire...");

        if (useAiStructure)
        {
            _backgroundJobClient.Enqueue<IAiNotificationJob>(job =>
                job.GenerateAndNotifyPlanAsync(symbol, null, triggerReason, true, candlesByTimeframe));
        }
        else
        {
            _backgroundJobClient.Enqueue<IAiNotificationJob>(job =>
                job.GenerateAndNotifyPlanAsync(symbol, analysisResults, triggerReason, false, null));
        }

        return (true, $"Đã xếp hàng tạo plan mới (useAiStructure={useAiStructure}).");
    }
}
