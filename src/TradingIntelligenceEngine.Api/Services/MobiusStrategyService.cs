using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.DTOs.Mobius;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.Configuration;

namespace TradingIntelligenceEngine.Api.Services;

public class MobiusStrategyService : IMobiusStrategyService
{
    private readonly HttpClient _httpClient;
    private readonly MobiusOptions _options;
    private readonly IMarketDataStore _marketDataStore;
    private readonly INotificationService? _notifier;
    private readonly ILogger<MobiusStrategyService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MobiusStrategyService(
        HttpClient httpClient,
        MobiusOptions options,
        IMarketDataStore marketDataStore,
        ILogger<MobiusStrategyService> logger,
        INotificationService? notifier = null)
    {
        _httpClient = httpClient;
        _options = options;
        _marketDataStore = marketDataStore;
        _logger = logger;
        _notifier = notifier;

        if (_options.TimeoutSeconds > 0)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }
    }

    public async Task<MobiusScenarioResponse> FetchAndApplyScenariosAsync(
        MobiusScenarioRequest request,
        bool? notifyOverride = null,
        CancellationToken cancellationToken = default)
    {
        var symbol = string.IsNullOrWhiteSpace(request.Symbol) ? "XAUUSD" : request.Symbol.Trim().ToUpperInvariant();
        request.Symbol = symbol;
        if (string.IsNullOrWhiteSpace(request.Interval))
            request.Interval = "1h";
        if (request.Schools == null || request.Schools.Count == 0)
            request.Schools = new List<string> { "ICT", "SMC" };
        if (request.TopKCards <= 0)
            request.TopKCards = 3;
        if (request.MaxRiskUsd <= 0)
            request.MaxRiskUsd = 100m;
        if (string.IsNullOrWhiteSpace(request.Language))
            request.Language = "vi";

        var url = $"{_options.BaseUrl.TrimEnd('/')}/{_options.Endpoint.TrimStart('/')}";
        var jsonPayload = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Cầu nối Mobius: Gửi POST tới {Url} cho {Symbol} (interval: {Interval}, risk: ${Risk})",
            url, symbol, request.Interval, request.MaxRiskUsd);

        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Mobius API trả về lỗi HTTP {StatusCode}: {Response}",
                (int)response.StatusCode, responseString);
            throw new HttpRequestException(
                $"Mobius API trả về lỗi HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {responseString}");
        }

        var mobiusResponse = JsonSerializer.Deserialize<MobiusScenarioResponse>(responseString, JsonOptions)
            ?? throw new InvalidOperationException("Không thể giải mã (deserialize) JSON từ Mobius API.");

        var signalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol}";
        mobiusResponse.SignalId = signalId;
        mobiusResponse.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (string.IsNullOrWhiteSpace(mobiusResponse.Symbol))
            mobiusResponse.Symbol = symbol;

        // Giới hạn mô hình tối đa 2 TP theo quy chuẩn hệ thống
        if (mobiusResponse.Scenarios != null)
        {
            foreach (var sc in mobiusResponse.Scenarios)
            {
                if (sc.TakeProfits != null && sc.TakeProfits.Count > 2)
                {
                    sc.TakeProfits = sc.TakeProfits.Take(2).ToList();
                }
            }
        }

        // Chuyển đổi và lưu trữ vào MarketDataStore của hệ thống
        var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
        var cap = TradingPlanResponse.NormalizeMaxRiskUsd(
            request.MaxRiskUsd > 0
                ? request.MaxRiskUsd
                : session?.MaxRiskUsd ?? TradingPlanResponse.DefaultMaxRiskUsd);

        var plan = new TradingPlanResponse
        {
            SignalId = signalId,
            Symbol = symbol,
            Bias = mobiusResponse.Bias ?? string.Empty,
            Context = !string.IsNullOrWhiteSpace(mobiusResponse.Context)
                ? mobiusResponse.Context
                : (mobiusResponse.AnalysisSummary ?? string.Empty),
            Scenarios = mobiusResponse.Scenarios ?? new List<EaTradeScenario>(),
            Source = "mobius",
            CreatedAt = mobiusResponse.CreatedAt,
            MaxRiskUsd = mobiusResponse.MaxRiskUsd > 0 ? mobiusResponse.MaxRiskUsd : cap
        };

        plan.EnsureDefaults(symbol, signalId, cap);
        TakeProfitPlanner.PullEarlier(plan, session?.Contract);

        bool hasScenarios = plan.Scenarios != null && plan.Scenarios.Count > 0;

        // Nếu có kịch bản, nạp ngay vào plan hiện tại để EA lấy được và thực thi
        if (hasScenarios)
        {
            await _marketDataStore.SetLatestPlanAsync(symbol, plan);
            _logger.LogInformation(
                "Cầu nối Mobius: Đã nạp thành công {Count} kịch bản vào bộ nhớ hiện tại cho {Symbol} (SignalId: {SignalId})",
                plan.Scenarios.Count, symbol, signalId);
        }
        else
        {
            _logger.LogWarning("Cầu nối Mobius: Phản hồi từ Mobius không có kịch bản (0 scenarios) cho {Symbol}.", symbol);
        }

        if (session == null)
        {
            session = new TradingIntelligenceEngine.Application.DTOs.MarketData.SymbolTradingSession
            {
                Symbol = symbol,
                MaxRiskUsd = cap,
                PlanRefreshInProgress = false
            };
        }
        else
        {
            session.PlanRefreshInProgress = false;
        }
        await _marketDataStore.SetSymbolSessionAsync(symbol, session);

        // Cập nhật lại scenarios và plan chuẩn hóa vào response trả về
        mobiusResponse.Scenarios = plan.Scenarios;
        mobiusResponse.Plan = plan;

        // Gửi thông báo Google Chat nếu có cấu hình notify
        bool shouldNotify = notifyOverride ?? mobiusResponse.Notify;
        if (shouldNotify && _notifier != null)
        {
            try
            {
                var chatMsg = TradingPlanChatFormatter.BuildPlanMessage(
                    symbol,
                    plan,
                    "MOBIUS RAG STRATEGY (ICT/SMC)",
                    mobiusResponse.AnalysisSummary);
                await _notifier.SendMessageAsync(chatMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi thông báo Mobius plan qua Google Chat.");
            }
        }

        _logger.LogInformation(
            "Cầu nối Mobius: Hoàn tất nạp {Count} kịch bản cho {Symbol} (SignalId: {SignalId})",
            plan.Scenarios.Count, symbol, signalId);

        return mobiusResponse;
    }
}
