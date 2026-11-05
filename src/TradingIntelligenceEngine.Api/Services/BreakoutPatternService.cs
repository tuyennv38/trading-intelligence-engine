using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;
using TradingIntelligenceEngine.TechnicalAnalysis.Indicators;

namespace TradingIntelligenceEngine.Api.Services;

public class BreakoutPatternService : IBreakoutPatternService
{
    private readonly IMobiusKlinesClient _klinesClient;
    private readonly IMarketDataStore _marketDataStore;
    private readonly INotificationService? _notifier;
    private readonly BreakoutOptions _options;
    private readonly ISwingDetector _swingDetector;
    private readonly SwingOptions _swingOptions;
    private readonly IAiBreakoutEngine? _aiBreakoutEngine;
    private readonly AiProviderOptions? _aiProviderOptions;
    private readonly ILogger<BreakoutPatternService> _logger;

    public BreakoutPatternService(
        IMobiusKlinesClient klinesClient,
        IMarketDataStore marketDataStore,
        BreakoutOptions options,
        ISwingDetector swingDetector,
        SwingOptions swingOptions,
        ILogger<BreakoutPatternService> logger,
        INotificationService? notifier = null,
        IAiBreakoutEngine? aiBreakoutEngine = null,
        AiProviderOptions? aiProviderOptions = null)
    {
        _klinesClient = klinesClient;
        _marketDataStore = marketDataStore;
        _options = options;
        _swingDetector = swingDetector;
        _swingOptions = swingOptions;
        _logger = logger;
        _notifier = notifier;
        _aiBreakoutEngine = aiBreakoutEngine;
        _aiProviderOptions = aiProviderOptions;
    }


    // Mức dao động tối đa (tính theo ATR) để coi một chuỗi swing là "đi ngang"
    private const decimal FlatSpanAtr = 0.8m;
    private const int SwingWindow = 3;

    private enum SideTrend { Flat, Up, Down, Mixed }

    private sealed class PatternCandidate
    {
        public required PatternDetail Detail { get; init; }
        public int Priority { get; init; }
        public BoundaryZone? ResistanceOverride { get; init; }
        public BoundaryZone? SupportOverride { get; init; }
    }

    public async Task<PatternRecognitionResult> RecognizePatternAsync(
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> candles,
        CancellationToken cancellationToken = default)
    {
        if (candles == null || candles.Count == 0)
        {
            return new PatternRecognitionResult
            {
                Symbol = symbol,
                Timeframe = timeframe,
                PrimaryPattern = new PatternDetail
                {
                    Group = "A",
                    Name = "NO_CLEAR_PATTERN",
                    Status = "NOT_FOUND",
                    Confidence = "LOW"
                }
            };
        }

        var currentPrice = candles.Last().Close;
        var lastIndex = candles.Count - 1;
        var atr = IndicatorMath.CalculateAtr(candles, 14);
        if (atr <= 0) atr = FallbackAtr(candles);

        // 1. Điểm xoay: chỉ dùng swing đã CONFIRMED (loại Invalidated và Potential) trong 60 nến gần nhất.
        // Swing Invalidated là bản trùng/bị thay thế, đếm vào sẽ biến một lần tiếp xúc thành nhiều lần chạm.
        var recentCutoffIndex = Math.Max(0, candles.Count - 60);
        var swings = _swingDetector.Detect(candles, _swingOptions);
        var recentSwings = swings
            .Where(s => s.Index >= recentCutoffIndex && s.Status == SwingStatus.Confirmed)
            .ToList();
        var swingHighs = recentSwings.Where(s => s.Type == SwingType.High).OrderBy(s => s.Index).ToList();
        var swingLows = recentSwings.Where(s => s.Type == SwingType.Low).OrderBy(s => s.Index).ToList();

        var result = new PatternRecognitionResult
        {
            Symbol = symbol,
            Timeframe = timeframe,
            CurrentPrice = currentPrice,
            Atr = atr,
            CandleCount = candles.Count
        };

        // 2. Vùng biên (Support & Resistance) quanh giá hiện tại
        var tolerance = atr * 0.4m;
        var flatSpan = atr * FlatSpanAtr;
        var resZone = ClusterSwings(swingHighs.Select(s => s.Price).ToList(), currentPrice, isResistance: true, tolerance, atr);
        var supZone = ClusterSwings(swingLows.Select(s => s.Price).ToList(), currentPrice, isResistance: false, tolerance, atr);

        // 3. Phân loại cấu trúc theo 4 nhóm trong Skill (A, B, C, D)
        var candidates = new List<PatternCandidate>();
        var evidence = new List<string>();
        var missing = new List<string>();

        var lastHighs = swingHighs.TakeLast(SwingWindow).ToList();
        var lastLows = swingLows.TakeLast(SwingWindow).ToList();
        var highsTrend = GetTrend(lastHighs, flatSpan);
        var lowsTrend = GetTrend(lastLows, flatSpan);

        // Nhóm A: Horizontal (Rectangle / Double-Multiple Top/Bottom)
        bool isRectangle = false;
        if (resZone.TouchCount >= 2 && supZone.TouchCount >= 2 &&
            resZone.Low.HasValue && supZone.High.HasValue && resZone.Low.Value > supZone.High.Value &&
            IsNotSloped(highsTrend) && IsNotSloped(lowsTrend))
        {
            // Biên độ phải hợp lý so với biến động hiện tại (không quá hẹp / quá rộng)
            var height = resZone.Center - supZone.Center;
            if (height >= atr * 1.5m && height <= atr * 12m)
            {
                isRectangle = true;
                candidates.Add(new PatternCandidate
                {
                    Priority = 3,
                    Detail = new PatternDetail
                    {
                        Group = "A",
                        Name = "RECTANGLE",
                        Status = (resZone.TouchCount >= 3 || supZone.TouchCount >= 3) ? "CONFIRMED" : "POTENTIAL",
                        Confidence = (resZone.TouchCount >= 3 && supZone.TouchCount >= 3) ? "HIGH" : "MEDIUM"
                    }
                });
                evidence.Add($"Giá dao động giữa Kháng cự ({resZone.Low:F2} - {resZone.High:F2}, {resZone.TouchCount} lần chạm) và Hỗ trợ ({supZone.Low:F2} - {supZone.High:F2}, {supZone.TouchCount} lần chạm).");
            }
        }

        if (!isRectangle)
        {
            if (resZone.TouchCount >= 2 && IsNotSloped(highsTrend))
            {
                candidates.Add(new PatternCandidate
                {
                    Priority = 4,
                    Detail = new PatternDetail
                    {
                        Group = "A",
                        Name = resZone.TouchCount >= 3 ? "MULTIPLE_TOP" : "DOUBLE_TOP",
                        Status = "POTENTIAL",
                        Confidence = "MEDIUM"
                    }
                });
                evidence.Add($"Xuất hiện {resZone.TouchCount} đỉnh tiệm cận vùng kháng cự {resZone.Low:F2} - {resZone.High:F2}.");
            }

            if (supZone.TouchCount >= 2 && IsNotSloped(lowsTrend))
            {
                candidates.Add(new PatternCandidate
                {
                    Priority = 4,
                    Detail = new PatternDetail
                    {
                        Group = "A",
                        Name = supZone.TouchCount >= 3 ? "MULTIPLE_BOTTOM" : "DOUBLE_BOTTOM",
                        Status = "POTENTIAL",
                        Confidence = "MEDIUM"
                    }
                });
                evidence.Add($"Xuất hiện {supZone.TouchCount} đáy tiệm cận vùng hỗ trợ {supZone.Low:F2} - {supZone.High:F2}.");
            }
        }

        // Nhóm B (Triangles) & C (Wedges / Channels): cần >= 2 swing mỗi cạnh; CONFIRMED khi có >= 3 swing ở ít nhất một cạnh
        if (lastHighs.Count >= 2 && lastLows.Count >= 2)
        {
            var status = (lastHighs.Count >= 3 || lastLows.Count >= 3) ? "CONFIRMED" : "POTENTIAL";
            var confidence = (lastHighs.Count >= 3 && lastLows.Count >= 3) ? "HIGH" : "MEDIUM";
            var ratio = ConvergenceRatio(lastHighs, lastLows, out var widthEnd);
            var slopedUpper = BuildSlopedZone(lastHighs, lastIndex, tolerance);
            var slopedLower = BuildSlopedZone(lastLows, lastIndex, tolerance);

            if (highsTrend == SideTrend.Flat && lowsTrend == SideTrend.Up && widthEnd > 0 && ratio < 1m)
            {
                candidates.Add(new PatternCandidate
                {
                    Priority = 1,
                    SupportOverride = slopedLower,
                    Detail = new PatternDetail { Group = "B", Name = "ASCENDING_TRIANGLE", Status = status, Confidence = confidence }
                });
                evidence.Add($"Kháng cự phẳng quanh {lastHighs.Average(h => h.Price):F2} kết hợp đáy nâng cao dần (Higher Lows). Thiên hướng Breakout Tăng.");
            }
            else if (lowsTrend == SideTrend.Flat && highsTrend == SideTrend.Down && widthEnd > 0 && ratio < 1m)
            {
                candidates.Add(new PatternCandidate
                {
                    Priority = 1,
                    ResistanceOverride = slopedUpper,
                    Detail = new PatternDetail { Group = "B", Name = "DESCENDING_TRIANGLE", Status = status, Confidence = confidence }
                });
                evidence.Add($"Hỗ trợ phẳng quanh {lastLows.Average(l => l.Price):F2} kết hợp đỉnh hạ thấp dần (Lower Highs). Thiên hướng Breakdown Giảm.");
            }
            else if (highsTrend == SideTrend.Down && lowsTrend == SideTrend.Up && widthEnd > 0 && ratio < 1m)
            {
                candidates.Add(new PatternCandidate
                {
                    Priority = 1,
                    ResistanceOverride = slopedUpper,
                    SupportOverride = slopedLower,
                    Detail = new PatternDetail { Group = "B", Name = "SYMMETRICAL_TRIANGLE", Status = status, Confidence = confidence }
                });
                evidence.Add("Đỉnh hạ dần (Lower Highs) và Đáy nâng dần (Higher Lows) tạo tam giác đối xứng co hẹp biên độ.");
            }
            else if (highsTrend == SideTrend.Up && lowsTrend == SideTrend.Up && widthEnd > 0)
            {
                if (ratio < 0.75m)
                {
                    candidates.Add(new PatternCandidate
                    {
                        Priority = 1,
                        ResistanceOverride = slopedUpper,
                        SupportOverride = slopedLower,
                        Detail = new PatternDetail { Group = "C", Name = "RISING_WEDGE", Status = status, Confidence = confidence }
                    });
                    evidence.Add("Đỉnh và Đáy cùng dốc lên nhưng biên độ hội tụ co hẹp dần (Nêm tăng). Cảnh báo đảo chiều giảm.");
                }
                else if (ratio >= 0.85m && ratio <= 1.15m)
                {
                    candidates.Add(new PatternCandidate
                    {
                        Priority = 1,
                        ResistanceOverride = slopedUpper,
                        SupportOverride = slopedLower,
                        Detail = new PatternDetail { Group = "C", Name = "ASCENDING_CHANNEL", Status = status, Confidence = confidence }
                    });
                    evidence.Add("Đỉnh cao dần (HH) và Đáy cao dần (HL) di chuyển trong kênh giá tăng song song.");
                }
            }
            else if (highsTrend == SideTrend.Down && lowsTrend == SideTrend.Down && widthEnd > 0)
            {
                if (ratio < 0.75m)
                {
                    candidates.Add(new PatternCandidate
                    {
                        Priority = 1,
                        ResistanceOverride = slopedUpper,
                        SupportOverride = slopedLower,
                        Detail = new PatternDetail { Group = "C", Name = "FALLING_WEDGE", Status = status, Confidence = confidence }
                    });
                    evidence.Add("Đỉnh và Đáy cùng dốc xuống nhưng biên độ hội tụ co hẹp dần (Nêm giảm). Cảnh báo đảo chiều tăng.");
                }
                else if (ratio >= 0.85m && ratio <= 1.15m)
                {
                    candidates.Add(new PatternCandidate
                    {
                        Priority = 1,
                        ResistanceOverride = slopedUpper,
                        SupportOverride = slopedLower,
                        Detail = new PatternDetail { Group = "C", Name = "DESCENDING_CHANNEL", Status = status, Confidence = confidence }
                    });
                    evidence.Add("Đỉnh thấp dần (LH) và Đáy thấp dần (LL) di chuyển trong kênh giá giảm song song.");
                }
            }
        }

        // Nhóm D: Continuation (Bull Flag / Bear Flag) - cần impulse leg rõ + vùng điều chỉnh hẹp phía sau
        var flag = DetectFlag(candles, atr, tolerance, swingHighs, swingLows);
        if (flag != null)
        {
            candidates.Add(flag.Value.Candidate);
            evidence.Add(flag.Value.Evidence);
        }

        // 4. Chọn Primary Pattern theo trạng thái -> độ tin cậy -> độ đặc trưng của mô hình
        if (candidates.Count > 0)
        {
            var ordered = candidates
                .OrderByDescending(c => StatusRank(c.Detail.Status))
                .ThenByDescending(c => ConfidenceRank(c.Detail.Confidence))
                .ThenBy(c => c.Priority)
                .ToList();
            result.PrimaryPattern = ordered[0].Detail;
            result.OtherCandidates = ordered.Skip(1).Select(c => c.Detail).ToList();

            if (ordered[0].ResistanceOverride != null) resZone = ordered[0].ResistanceOverride!;
            if (ordered[0].SupportOverride != null) supZone = ordered[0].SupportOverride!;

            if (result.PrimaryPattern.Status != "CONFIRMED")
                missing.Add($"Mô hình {result.PrimaryPattern.Name} mới ở mức {result.PrimaryPattern.Status}: cần thêm swing/lần chạm độc lập để xác nhận.");
        }
        else
        {
            result.PrimaryPattern = new PatternDetail
            {
                Group = "A",
                Name = "HORIZONTAL_LEVELS",
                Status = "DEVELOPING",
                Confidence = "LOW"
            };
            missing.Add("Chưa đủ 2 lần chạm độc lập ở cả 2 biên để xác nhận mô hình hoàn chỉnh.");
        }

        result.Zones.Resistance = resZone;
        result.Zones.Support = supZone;

        // Đánh giá Market State
        if (result.PrimaryPattern.Name.Contains("ASCENDING") || result.PrimaryPattern.Name == "BULL_FLAG")
            result.MarketState = "TREND_UP";
        else if (result.PrimaryPattern.Name.Contains("DESCENDING") || result.PrimaryPattern.Name == "BEAR_FLAG")
            result.MarketState = "TREND_DOWN";
        else
            result.MarketState = "RANGE";

        result.Evidence = evidence;
        result.MissingEvidence = missing;

        return await Task.FromResult(result);
    }

    public async Task<BreakoutScenarioResult> PlanBreakoutScenarioAsync(
        PatternRecognitionResult patternResult,
        IReadOnlyList<Candle> candles,
        decimal maxRiskUsd = 100m,
        CancellationToken cancellationToken = default)
    {
        var symbol = patternResult.Symbol;
        var tf = patternResult.Timeframe;
        var currentPrice = patternResult.CurrentPrice;
        var atr = patternResult.Atr > 0
            ? patternResult.Atr
            : (candles != null && candles.Count > 0 ? FallbackAtr(candles) : Math.Max(currentPrice * 0.001m, 0.0001m));
        var signalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol}_BO";
        var digits = DigitsFor(currentPrice);
        decimal R(decimal v) => Math.Round(v, digits);

        var resZone = patternResult.Zones.Resistance;
        var supZone = patternResult.Zones.Support;
        var buffer = R(atr * 0.25m);

        var plan = new TradingPlanResponse
        {
            SignalId = signalId,
            Symbol = symbol,
            Bias = patternResult.MarketState == "TREND_UP" ? "BULLISH" : (patternResult.MarketState == "TREND_DOWN" ? "BEARISH" : "NEUTRAL_RANGE"),
            Context = $"Nhận diện mô hình [{patternResult.PrimaryPattern.Name}] ({patternResult.PrimaryPattern.Group}) trạng thái {patternResult.PrimaryPattern.Status}. Kháng cự: {resZone.High:F2}, Hỗ trợ: {supZone.Low:F2}.",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Source = TradingPlanSource.Ai,
            MaxRiskUsd = maxRiskUsd
        };

        // Swing đã xác nhận làm cột mốc tham chiếu cho TP
        var confirmedSwings = candles != null && candles.Count > 0
            ? _swingDetector.Detect(candles, _swingOptions).Where(s => s.Status == SwingStatus.Confirmed).ToList()
            : new List<SwingPoint>();
        decimal? rangeHeight = (resZone.High.HasValue && supZone.Low.HasValue && resZone.High.Value > supZone.Low.Value)
            ? resZone.High.Value - supZone.Low.Value
            : null;

        EaTradeScenario? buyScenario = null;
        EaTradeScenario? sellScenario = null;
        var notes = new List<string>();

        // Chỉ lập kịch bản live khi mô hình đã CONFIRMED (hoặc cấu hình cho phép mô hình yếu hơn)
        bool patternOk = !_options.RequireConfirmedPattern || patternResult.PrimaryPattern.Status == "CONFIRMED";
        if (!patternOk)
        {
            notes.Add($"Mô hình {patternResult.PrimaryPattern.Name} mới ở trạng thái {patternResult.PrimaryPattern.Status} - chỉ theo dõi, chưa lập kịch bản breakout.");
        }

        // CHỈ LẬP KỊCH BẢN KHI VÙNG BIÊN ĐÃ CÓ TỐI THIỂU 2 ĐIỂM CHẠM ĐỘC LẬP
        bool hasRes = patternOk && resZone.High.HasValue && resZone.TouchCount >= 2;
        bool hasSup = patternOk && supZone.Low.HasValue && supZone.TouchCount >= 2;
        if (patternOk && !hasRes && !hasSup)
            notes.Add($"Kháng cự ({resZone.TouchCount} lần chạm) và Hỗ trợ ({supZone.TouchCount} lần chạm) chưa đạt tối thiểu 2 điểm chạm độc lập. Chờ lần test thứ 2 trước khi lập kịch bản.");

        var noChaseDistance = atr * _options.NoChaseAtr;

        if (hasRes)
        {
            var trigger = R(resZone.High!.Value + buffer);
            if (currentPrice > trigger + noChaseDistance)
            {
                notes.Add($"NO_CHASE: giá {currentPrice:F2} đã chạy quá xa trigger mua {trigger:F2} (> {_options.NoChaseAtr:0.##} ATR), bỏ kịch bản Breakout Mua.");
            }
            else
            {
                var (sl, tp1, tp2) = PlanRisk(true, trigger, resZone, buffer, atr, symbol, rangeHeight, confirmedSwings, R);
                buyScenario = new EaTradeScenario
                {
                    Id = "scenario_1_breakout_buy",
                    Type = EaScenarioType.BreakoutBuy,
                    Name = $"Breakout Mua khi vượt {trigger:F2}",
                    Logic = $"Chờ nến {tf} đóng cửa dứt khoát trên mốc {trigger:F2} (vượt cản {resZone.High:F2} + buffer {buffer:F2}). Sau đó chờ nhịp Retest kiểm tra lại vùng cản làm hỗ trợ mới kích hoạt Buy theo đà tiếp diễn.",
                    TriggerPrice = trigger,
                    StopLoss = sl,
                    MaxRiskUsd = maxRiskUsd,
                    TakeProfits = new List<decimal> { tp1, tp2 },
                    Confirmation = new ScenarioConfirmation
                    {
                        Condition = "CLOSE_ABOVE_TRIGGER",
                        Timeframe = tf,
                        Patterns = new List<string> { "Bullish Marubozu", "Strong Expansion Candle" }
                    },
                    Retest = new ScenarioRetest
                    {
                        Timeframe = ScenarioRetest.OneStepSmaller(tf),
                        Condition = EaRetestCondition.RetestThenContinue,
                        Patterns = new List<string> { "Pinbar", "Bullish Engulfing" }
                    },
                    Status = EaScenarioStatus.Pending
                };
            }
        }

        if (hasSup)
        {
            var trigger = R(supZone.Low!.Value - buffer);
            if (currentPrice < trigger - noChaseDistance)
            {
                notes.Add($"NO_CHASE: giá {currentPrice:F2} đã chạy quá xa trigger bán {trigger:F2} (> {_options.NoChaseAtr:0.##} ATR), bỏ kịch bản Breakdown Bán.");
            }
            else
            {
                var (sl, tp1, tp2) = PlanRisk(false, trigger, supZone, buffer, atr, symbol, rangeHeight, confirmedSwings, R);
                sellScenario = new EaTradeScenario
                {
                    Id = "scenario_2_breakout_sell",
                    Type = EaScenarioType.BreakoutSell,
                    Name = $"Breakdown Bán khi thủng {trigger:F2}",
                    Logic = $"Chờ nến {tf} đóng cửa dứt khoát dưới mốc {trigger:F2} (thủng hỗ trợ {supZone.Low:F2} - buffer {buffer:F2}). Sau đó chờ nhịp Retest kiểm tra lại vùng hỗ trợ cũ đóng vai trò kháng cự mới kích hoạt Sell.",
                    TriggerPrice = trigger,
                    StopLoss = sl,
                    MaxRiskUsd = maxRiskUsd,
                    TakeProfits = new List<decimal> { tp1, tp2 },
                    Confirmation = new ScenarioConfirmation
                    {
                        Condition = "CLOSE_BELOW_TRIGGER",
                        Timeframe = tf,
                        Patterns = new List<string> { "Bearish Marubozu", "Strong Expansion Candle" }
                    },
                    Retest = new ScenarioRetest
                    {
                        Timeframe = ScenarioRetest.OneStepSmaller(tf),
                        Condition = EaRetestCondition.RetestThenContinue,
                        Patterns = new List<string> { "Shooting Star", "Bearish Engulfing" }
                    },
                    Status = EaScenarioStatus.Pending
                };
            }
        }

        // Ưu tiên kịch bản cùng hướng bias của mô hình
        var scenarios = new List<EaTradeScenario>();
        var first = patternResult.MarketState == "TREND_DOWN" ? sellScenario : buyScenario;
        var second = patternResult.MarketState == "TREND_DOWN" ? buyScenario : sellScenario;
        if (first != null) scenarios.Add(first);
        if (second != null) scenarios.Add(second);

        if (scenarios.Count == 0 && notes.Count > 0)
        {
            plan.Context = $"Chưa lập kịch bản Breakout. {string.Join(" ", notes)}";
        }
        else if (notes.Count > 0)
        {
            plan.Context += " " + string.Join(" ", notes);
        }

        plan.Scenarios = scenarios;
        plan.EnsureDefaults(symbol, signalId, maxRiskUsd);

        var currentStatus = scenarios.Count > 0
            ? "SCENARIO_CONFIRMED"
            : (patternResult.PrimaryPattern.Status == "POTENTIAL" ? "MONITOR" : "WAIT");

        var result = new BreakoutScenarioResult
        {
            PatternRecognition = patternResult,
            TradingPlan = plan,
            CurrentStatus = currentStatus,
            SummaryReport = BuildSummaryReport(patternResult, buyScenario, sellScenario, notes, currentStatus)
        };

        return await Task.FromResult(result);
    }

    public async Task<BreakoutScenarioResult> AnalyzeAndExecuteBreakoutSyncAsync(
        string? symbol = null,
        string? interval = null,
        int? limit = null,
        decimal? maxRiskUsd = null,
        string? patternMode = null,
        string? scenarioMode = null,
        bool notify = true,
        CancellationToken cancellationToken = default)
    {
        symbol = string.IsNullOrWhiteSpace(symbol) ? _options.Symbol : symbol.Trim().ToUpperInvariant();
        interval = string.IsNullOrWhiteSpace(interval) ? _options.Interval : interval.Trim();
        var limitVal = limit.HasValue && limit.Value > 0 ? limit.Value : _options.Limit;
        if (maxRiskUsd.HasValue && maxRiskUsd.Value > 0)
        {
            _options.MaxRiskUsd = maxRiskUsd.Value; // Lưu lại mức rủi ro mới cho các chu kỳ quét tự động tiếp theo
        }
        var riskVal = _options.MaxRiskUsd;

        if (!string.IsNullOrWhiteSpace(patternMode))
            _options.PatternMode = patternMode.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(scenarioMode))
            _options.ScenarioMode = scenarioMode.Trim().ToUpperInvariant();

        var effPatternMode = _options.PatternMode;
        var effScenarioMode = _options.ScenarioMode;

        _logger.LogInformation("Khởi động quy trình Breakout Sync cho {Symbol} ({Interval}, limit={Limit}). Chế độ: [Pattern: {PMode}, Scenario: {SMode}]",
            symbol, interval, limitVal, effPatternMode, effScenarioMode);

        // 1. Lấy nến từ Mobius API
        var rawCandles = await _klinesClient.FetchRawCandlesAsync(symbol, interval, limitVal, _options.Exchange, _options.Market, cancellationToken);
        if (rawCandles.Count == 0)
        {
            throw new InvalidOperationException($"Không lấy được nến từ Mobius API cho {symbol} ({interval}).");
        }

        // 2. Lưu/Cập nhật nến vào IMarketDataStore để EA hoặc Controller có thể truy xuất
        await _marketDataStore.InitializeCandlesAsync(symbol, interval.ToUpperInvariant(), rawCandles);

        var domainCandles = rawCandles.Select(c => new Candle(
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

        // Thuật toán Quant chỉ dùng nến đã đóng: bỏ nến cuối nếu còn đang hình thành
        var analysisCandles = DropFormingCandle(domainCandles, interval);

        var aiCandles = rawCandles.Select(c => new AiCandleInput(
            c.Time, c.Open, c.High, c.Low, c.Close, c.TickVolume,
            c.EmaFast, c.EmaSlow, c.Rsi, c.Atr, c.Adx)).ToList();

        var hasAiConfig = !string.IsNullOrWhiteSpace(_aiProviderOptions?.Snapshot().BaseUrl);
        BreakoutScenarioResult? scenarioResult = null;
        string engineMode = (effPatternMode == "AI" && effScenarioMode == "AI") ? "AI" : $"{effPatternMode}+{effScenarioMode}";
        string? fallbackReason = null;

        // 3. Điều phối theo cấu hình PatternMode & ScenarioMode:
        // TRƯỜNG HỢP 1: Cả hai đều dùng AI ("AI" + "AI")
        if (effPatternMode == "AI" && effScenarioMode == "AI")
        {
            if (_aiBreakoutEngine != null && hasAiConfig)
            {
                try
                {
                    _logger.LogInformation("Đang dùng AI ({Model}) thực hiện cả 2 Kỹ năng: Nhận diện mô hình & Lập kịch bản Breakout...", _aiProviderOptions!.Snapshot().Model);
                    scenarioResult = await _aiBreakoutEngine.AnalyzeBreakoutWithSkillsAsync(symbol, interval.ToUpperInvariant(), aiCandles, riskVal, cancellationToken);
                }
                catch (Exception ex)
                {
                    fallbackReason = ex.Message;
                    _logger.LogWarning(ex, "Gọi AI thất bại ({Message}). Chuyển sang fallback Định lượng...", ex.Message);
                }
            }
        }
        // TRƯỜNG HỢP 2: Nhận diện bằng Thuật toán Quant, Lập kịch bản bằng AI ("QUANT" + "AI")
        else if (effPatternMode != "AI" && effScenarioMode == "AI")
        {
            var patternResult = await RecognizePatternAsync(symbol, interval.ToUpperInvariant(), analysisCandles, cancellationToken);
            if (_aiBreakoutEngine != null && hasAiConfig)
            {
                try
                {
                    _logger.LogInformation("Nhận diện mô hình bằng Thuật toán Quant xong ({Pattern}). Đang chuyển sang AI lập kịch bản...", patternResult.PrimaryPattern.Name);
                    scenarioResult = await _aiBreakoutEngine.PlanScenarioWithAiAsync(patternResult, aiCandles, riskVal, cancellationToken);
                }
                catch (Exception ex)
                {
                    fallbackReason = ex.Message;
                    _logger.LogWarning(ex, "AI lập kịch bản thất bại ({Message}). Chuyển sang fallback Thuật toán lập kịch bản...", ex.Message);
                }
            }

            if (scenarioResult == null)
            {
                scenarioResult = await PlanBreakoutScenarioAsync(patternResult, analysisCandles, riskVal, cancellationToken);
            }
        }
        // TRƯỜNG HỢP 3: Nhận diện bằng AI, Lập kịch bản bằng Thuật toán Quant ("AI" + "QUANT")
        else if (effPatternMode == "AI" && effScenarioMode != "AI")
        {
            if (_aiBreakoutEngine != null && hasAiConfig)
            {
                try
                {
                    _logger.LogInformation("Đang dùng AI nhận diện mô hình...");
                    var aiResult = await _aiBreakoutEngine.AnalyzeBreakoutWithSkillsAsync(symbol, interval.ToUpperInvariant(), aiCandles, riskVal, cancellationToken);
                    scenarioResult = await PlanBreakoutScenarioAsync(aiResult.PatternRecognition, analysisCandles, riskVal, cancellationToken);
                }
                catch (Exception ex)
                {
                    fallbackReason = ex.Message;
                    _logger.LogWarning(ex, "AI nhận diện thất bại ({Message}). Chuyển sang Thuật toán Quant hoàn toàn...", ex.Message);
                }
            }
        }

        // TRƯỜNG HỢP 4 hoặc Fallback: 100% Thuật toán Định lượng Quant ("QUANT" + "QUANT")
        if (scenarioResult == null)
        {
            if (!string.IsNullOrEmpty(fallbackReason))
            {
                engineMode = "QUANT (FALLBACK DO AI LỖI)";
            }
            else
            {
                engineMode = "QUANT";
            }
            _logger.LogInformation("Thực thi 100% bằng Thuật toán Định lượng C# (Geometry Quant Algorithm)...");
            var patternResult = await RecognizePatternAsync(symbol, interval.ToUpperInvariant(), analysisCandles, cancellationToken);
            scenarioResult = await PlanBreakoutScenarioAsync(patternResult, analysisCandles, riskVal, cancellationToken);
        }

        // 4. Lưu TradingPlanResponse vào Store
        await _marketDataStore.SetLatestPlanAsync(symbol, scenarioResult.TradingPlan);

        _logger.LogInformation("Hoàn tất lập kịch bản Breakout cho {Symbol}. Primary Pattern: {Pattern} ({Status}). Động cơ: {Mode}",
            symbol, scenarioResult.PatternRecognition.PrimaryPattern.Name, scenarioResult.PatternRecognition.PrimaryPattern.Status, engineMode);

        // 5. Gửi thông báo nếu có cấu hình
        if (notify && _notifier != null && _options.NotifyOnScan)
        {
            var message = FormatGoogleChatMessage(scenarioResult, engineMode, fallbackReason);
            try
            {
                await _notifier.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi thông báo Google Chat cho Breakout Sync.");
            }
        }

        return scenarioResult;
    }

    private static BoundaryZone ClusterSwings(
        List<decimal> prices,
        decimal currentPrice,
        bool isResistance,
        decimal tolerance,
        decimal atr)
    {
        if (prices.Count == 0) return new BoundaryZone();

        // Gom cụm theo thứ tự giá (không phụ thuộc thứ tự thời gian): một cụm có biên độ tối đa = 2 * tolerance
        var clusters = new List<List<decimal>>();
        foreach (var p in prices.OrderBy(x => x))
        {
            if (clusters.Count > 0 && p - clusters[^1][0] <= tolerance * 2m)
                clusters[^1].Add(p);
            else
                clusters.Add(new List<decimal> { p });
        }

        // Kháng cự phải nằm ở trên (hoặc quanh) giá hiện tại, hỗ trợ phải nằm ở dưới (hoặc quanh) giá hiện tại.
        // Không còn fallback sang vùng nằm sai phía: nếu giá đã vượt hết thì không có biên để breakout.
        var validClusters = isResistance
            ? clusters.Where(c => c.Max() >= currentPrice - tolerance).ToList()
            : clusters.Where(c => c.Min() <= currentPrice + tolerance).ToList();

        if (validClusters.Count == 0) return new BoundaryZone();

        // Chọn cụm mạnh nhất: nhiều lần chạm là chính, gần giá hiện tại là phụ (mỗi 1 ATR xa trừ 0.5 lần chạm)
        var best = validClusters
            .OrderByDescending(c => c.Count - 0.5m * Math.Abs(c.Average() - currentPrice) / atr)
            .ThenBy(c => Math.Abs(c.Average() - currentPrice))
            .First();

        return new BoundaryZone { Low = best.Min(), High = best.Max(), TouchCount = best.Count };
    }

    // Cụm swing đã phẳng theo cấu tạo; chỉ loại trường hợp các swing gần nhất dốc một chiều rõ rệt (dấu hiệu Triangle/Wedge/Channel)
    private static bool IsNotSloped(SideTrend trend) => trend == SideTrend.Flat || trend == SideTrend.Mixed;

    private static SideTrend GetTrend(IReadOnlyList<SwingPoint> swings, decimal flatSpan)
    {
        if (swings.Count < 2) return SideTrend.Mixed;

        var prices = swings.Select(s => s.Price).ToList();
        if (prices.Max() - prices.Min() <= flatSpan) return SideTrend.Flat;

        bool up = true, down = true;
        for (int i = 1; i < prices.Count; i++)
        {
            if (prices[i] <= prices[i - 1]) up = false;
            if (prices[i] >= prices[i - 1]) down = false;
        }

        return up ? SideTrend.Up : (down ? SideTrend.Down : SideTrend.Mixed);
    }

    // Đường thẳng đi qua swing đầu và swing cuối, nội suy/ngoại suy tại chỉ số nến bất kỳ
    private static decimal ProjectLine(IReadOnlyList<SwingPoint> swings, int atIndex)
    {
        var a = swings[0];
        var b = swings[^1];
        if (b.Index == a.Index) return b.Price;
        var slope = (b.Price - a.Price) / (b.Index - a.Index);
        return b.Price + slope * (atIndex - b.Index);
    }

    // Tỷ lệ độ rộng hai cạnh (cuối / đầu): < 1 là hội tụ, ~1 là song song, > 1 là mở rộng
    private static decimal ConvergenceRatio(IReadOnlyList<SwingPoint> highs, IReadOnlyList<SwingPoint> lows, out decimal widthEnd)
    {
        var x0 = Math.Min(highs[0].Index, lows[0].Index);
        var x1 = Math.Max(highs[^1].Index, lows[^1].Index);
        var widthStart = ProjectLine(highs, x0) - ProjectLine(lows, x0);
        widthEnd = ProjectLine(highs, x1) - ProjectLine(lows, x1);
        return widthStart > 0 ? widthEnd / widthStart : decimal.MaxValue;
    }

    private static BoundaryZone BuildSlopedZone(IReadOnlyList<SwingPoint> swings, int lastIndex, decimal tolerance)
    {
        var projected = ProjectLine(swings, lastIndex);
        return new BoundaryZone
        {
            Low = projected - tolerance / 2m,
            High = projected + tolerance / 2m,
            TouchCount = swings.Count
        };
    }

    /// <summary>
    /// Bull/Bear Flag: nhịp impulse (pole) >= 3 ATR, theo sau là vùng điều chỉnh hẹp 5-14 nến,
    /// độ sâu hồi &lt;= 50% pole và không tiếp tục đẩy xa khỏi đỉnh/đáy pole.
    /// </summary>
    private static (PatternCandidate Candidate, string Evidence)? DetectFlag(
        IReadOnlyList<Candle> candles,
        decimal atr,
        decimal tolerance,
        IReadOnlyList<SwingPoint> swingHighs,
        IReadOnlyList<SwingPoint> swingLows)
    {
        const int poleLength = 10;
        const int minConsolidation = 5;
        const int maxConsolidation = 14;

        var n = candles.Count;
        (int c, decimal move, decimal hi, decimal lo, decimal depth)? best = null;

        for (int c = minConsolidation; c <= maxConsolidation; c++)
        {
            var poleStart = n - c - poleLength;
            if (poleStart < 0) break;
            var poleEnd = n - c - 1;

            var move = candles[poleEnd].Close - candles[poleStart].Open;
            if (Math.Abs(move) < atr * 3.0m) continue;

            var poleHigh = decimal.MinValue;
            var poleLow = decimal.MaxValue;
            for (int i = poleStart; i <= poleEnd; i++)
            {
                poleHigh = Math.Max(poleHigh, candles[i].High);
                poleLow = Math.Min(poleLow, candles[i].Low);
            }

            var hi = decimal.MinValue;
            var lo = decimal.MaxValue;
            for (int i = n - c; i < n; i++)
            {
                hi = Math.Max(hi, candles[i].High);
                lo = Math.Min(lo, candles[i].Low);
            }

            var poleRange = poleHigh - poleLow;
            if (poleRange <= 0) continue;

            var bull = move > 0;
            if ((hi - lo) > Math.Abs(move) * 0.6m) continue; // vùng điều chỉnh phải hẹp so với pole
            var depth = bull ? (poleHigh - lo) / poleRange : (hi - poleLow) / poleRange;
            if (depth > 0.5m) continue; // hồi quá sâu thì đã xóa phần lớn impulse
            if (bull && hi > poleHigh + atr * 0.5m) continue; // vẫn đang đẩy tiếp -> chưa phải điều chỉnh
            if (!bull && lo < poleLow - atr * 0.5m) continue;

            if (best == null || Math.Abs(move) > Math.Abs(best.Value.move))
                best = (c, move, hi, lo, depth);
        }

        if (best == null) return null;

        var (len, impulse, flagHigh, flagLow, retrace) = best.Value;
        var isBull = impulse > 0;
        var from = n - len;

        var upperContacts = CountContacts(candles, from, flagHigh, tolerance, isHigh: true);
        var lowerContacts = CountContacts(candles, from, flagLow, tolerance, isHigh: false);
        var confirmed = upperContacts >= 2 && lowerContacts >= 2;

        var candidate = new PatternCandidate
        {
            Priority = 2,
            ResistanceOverride = new BoundaryZone { Low = flagHigh - tolerance, High = flagHigh, TouchCount = upperContacts },
            SupportOverride = new BoundaryZone { Low = flagLow, High = flagLow + tolerance, TouchCount = lowerContacts },
            Detail = new PatternDetail
            {
                Group = "D",
                Name = isBull ? "BULL_FLAG" : "BEAR_FLAG",
                Status = confirmed ? "CONFIRMED" : "POTENTIAL",
                Confidence = "MEDIUM"
            }
        };

        var evidence = $"Impulse {(isBull ? "tăng" : "giảm")} {impulse:F2} (>= 3 ATR) theo sau là vùng điều chỉnh hẹp {len} nến ({flagLow:F2} - {flagHigh:F2}), hồi {retrace:P0} impulse.";
        return (candidate, evidence);
    }

    // Một lần chạm = giá rời vùng rồi quay lại; nhiều nến liên tiếp trong vùng chỉ tính một lần
    private static int CountContacts(IReadOnlyList<Candle> candles, int fromIndex, decimal level, decimal tolerance, bool isHigh)
    {
        int count = 0;
        bool inZone = false;
        for (int i = fromIndex; i < candles.Count; i++)
        {
            bool touching = isHigh ? candles[i].High >= level - tolerance : candles[i].Low <= level + tolerance;
            if (touching && !inZone) count++;
            inZone = touching;
        }
        return count;
    }

    private static int StatusRank(string status) => status switch
    {
        "CONFIRMED" => 3,
        "POTENTIAL" => 2,
        "DEVELOPING" => 1,
        _ => 0
    };

    private static int ConfidenceRank(string confidence) => confidence switch
    {
        "HIGH" => 3,
        "MEDIUM" => 2,
        _ => 1
    };

    // ATR dự phòng khi không đủ nến để tính ATR(14): trung bình biên độ nến gần nhất
    private static decimal FallbackAtr(IReadOnlyList<Candle> candles)
    {
        var recent = candles.TakeLast(14).Select(c => c.High - c.Low).Where(r => r > 0).ToList();
        if (recent.Count > 0) return recent.Average();
        return Math.Max(candles.Last().Close * 0.001m, 0.0001m);
    }

    private static int DigitsFor(decimal price) => price >= 100m ? 2 : (price >= 10m ? 3 : 5);

    private static bool IsGold(string symbol) =>
        symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ||
        symbol.Contains("GOLD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SL đặt tại điểm vô hiệu hóa cấu trúc (quay lại bên trong vùng vừa phá, trừ buffer), giới hạn trong khoảng rủi ro cho phép
    /// (Vàng: 5.0 - 9.5 giá; symbol khác: 1 - 3 ATR). TP1 = swing tham chiếu kế tiếp có trong dữ liệu (1R - 3R) nếu có, ngược lại 1.2R;
    /// TP2 = measured move bằng chiều cao vùng tích lũy (giới hạn trong khoảng TP1+0.5R .. 3.5R), ngược lại 2.2R.
    /// </summary>
    private static (decimal Sl, decimal Tp1, decimal Tp2) PlanRisk(
        bool isBuy,
        decimal trigger,
        BoundaryZone brokenZone,
        decimal buffer,
        decimal atr,
        string symbol,
        decimal? rangeHeight,
        IReadOnlyList<SwingPoint> swings,
        Func<decimal, decimal> round)
    {
        var invalidation = isBuy ? brokenZone.Low!.Value - buffer : brokenZone.High!.Value + buffer;
        var structuralDistance = Math.Abs(trigger - invalidation);

        var gold = IsGold(symbol);
        var minDistance = gold ? 5.0m : atr * 1.0m;
        var maxDistance = gold ? 9.5m : atr * 3.0m;
        var slDistance = Math.Clamp(structuralDistance, minDistance, maxDistance);

        var sl = round(isBuy ? trigger - slDistance : trigger + slDistance);

        // TP1: swing tham chiếu kế tiếp theo hướng breakout, cách trigger từ 1R đến 3R
        var references = swings
            .Where(s => isBuy
                ? s.Type == SwingType.High && s.Price >= trigger + slDistance && s.Price <= trigger + slDistance * 3m
                : s.Type == SwingType.Low && s.Price <= trigger - slDistance && s.Price >= trigger - slDistance * 3m)
            .Select(s => s.Price)
            .ToList();

        decimal tp1Distance = references.Count > 0
            ? (isBuy ? references.Min() - trigger : trigger - references.Max())
            : slDistance * 1.2m;

        // TP2: measured move của vùng tích lũy
        decimal tp2Distance = rangeHeight.HasValue
            ? Math.Clamp(rangeHeight.Value, tp1Distance + slDistance * 0.5m, slDistance * 3.5m)
            : slDistance * 2.2m;
        if (tp2Distance <= tp1Distance) tp2Distance = tp1Distance + slDistance * 0.5m;

        var tp1 = round(isBuy ? trigger + tp1Distance : trigger - tp1Distance);
        var tp2 = round(isBuy ? trigger + tp2Distance : trigger - tp2Distance);
        return (sl, tp1, tp2);
    }

    // Mobius trả về thời gian mở nến: nến cuối chưa đủ thời lượng thì còn đang hình thành
    private static List<Candle> DropFormingCandle(List<Candle> candles, string interval)
    {
        if (candles.Count < 2 || !TryParseIntervalSeconds(interval, out var seconds))
            return candles;

        return candles[^1].Time.AddSeconds(seconds) > DateTimeOffset.UtcNow
            ? candles.Take(candles.Count - 1).ToList()
            : candles;
    }

    private static bool TryParseIntervalSeconds(string interval, out int seconds)
    {
        seconds = 0;
        var m = Regex.Match(interval.Trim(), @"^(?:(\d+)([mhdw])|([mhdw])(\d+))$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        var amount = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[4].Value, CultureInfo.InvariantCulture);
        var unit = char.ToLowerInvariant((m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)[0]);
        seconds = unit switch
        {
            'm' => amount * 60,
            'h' => amount * 3600,
            'd' => amount * 86400,
            _ => amount * 604800
        };
        return seconds > 0;
    }

    private static string BuildSummaryReport(
        PatternRecognitionResult pattern,
        EaTradeScenario? buyScenario,
        EaTradeScenario? sellScenario,
        IReadOnlyList<string> notes,
        string currentStatus)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SYMBOL: {pattern.Symbol}");
        sb.AppendLine($"TIMEFRAME: {pattern.Timeframe}");
        sb.AppendLine($"PATTERN: {pattern.PrimaryPattern.Name} (Group {pattern.PrimaryPattern.Group})");
        sb.AppendLine($"PATTERN STATUS: {pattern.PrimaryPattern.Status} [Confidence: {pattern.PrimaryPattern.Confidence}]");
        sb.AppendLine();
        sb.AppendLine("STRUCTURE:");
        sb.AppendLine($"- Resistance: {pattern.Zones.Resistance.Low:F2} - {pattern.Zones.Resistance.High:F2} ({pattern.Zones.Resistance.TouchCount} touches)");
        sb.AppendLine($"- Support: {pattern.Zones.Support.Low:F2} - {pattern.Zones.Support.High:F2} ({pattern.Zones.Support.TouchCount} touches)");
        sb.AppendLine($"- Current Price: {pattern.CurrentPrice:F2} | ATR: {pattern.Atr:F2}");
        sb.AppendLine($"- Evidence: {string.Join("; ", pattern.Evidence)}");
        if (pattern.MissingEvidence.Count > 0)
            sb.AppendLine($"- Missing: {string.Join("; ", pattern.MissingEvidence)}");
        sb.AppendLine();

        AppendScenario(sb, "SCENARIO A - BREAKOUT UP", buyScenario);
        AppendScenario(sb, "SCENARIO B - BREAKDOWN DOWN", sellScenario);

        if (notes.Count > 0)
            sb.AppendLine($"NOTES: {string.Join(" ", notes)}");
        sb.AppendLine($"CURRENT STATUS: {currentStatus}");

        return sb.ToString();
    }

    private static void AppendScenario(StringBuilder sb, string title, EaTradeScenario? scenario)
    {
        sb.AppendLine($"{title}:");
        if (scenario == null)
        {
            sb.AppendLine("- Không lập kịch bản (chưa đủ điều kiện hoặc NO_CHASE).");
        }
        else
        {
            sb.AppendLine($"- Trigger Price: {scenario.TriggerPrice:F2}");
            sb.AppendLine($"- Confirmation: Đóng nến {(scenario.IsBuy ? "trên" : "dưới")} Trigger + Retest {scenario.Retest?.Timeframe}");
            sb.AppendLine($"- Stop Loss: {scenario.StopLoss:F2} | TPs: {string.Join(", ", scenario.TakeProfits.Select(t => t.ToString("F2")))}");
        }
        sb.AppendLine();
    }

    private static string FormatGoogleChatMessage(BreakoutScenarioResult result, string mode, string? fallbackReason = null)
    {
        var p = result.PatternRecognition;
        var plan = result.TradingPlan;
        var now = DateTime.UtcNow.AddHours(7);

        var sb = new StringBuilder();
        sb.AppendLine("⚡ *[BREAKOUT PATTERN SCANNER]*");
        sb.AppendLine($"🤖 Động cơ: *{mode}*");
        if (!string.IsNullOrEmpty(fallbackReason))
        {
            sb.AppendLine($"⚠️ _Lưu ý: AI phản hồi chậm/lỗi ({fallbackReason}), hệ thống tạm chuyển sang Quant để không gián đoạn._");
        }
        sb.AppendLine($"⏰ {now:dd/MM/yyyy HH:mm:ss} (UTC+7)");
        var displayAtr = p.Atr > 0 ? p.Atr : 2.5m;
        sb.AppendLine($"📌 *{p.Symbol} ({p.Timeframe})* | Giá: *{p.CurrentPrice:F2}* | ATR: *{displayAtr:F2}*");
        sb.AppendLine($"📐 Mô hình: *{p.PrimaryPattern.Name}* (Nhóm {p.PrimaryPattern.Group})");
        sb.AppendLine($"📊 Trạng thái: *{p.PrimaryPattern.Status}* (Độ tin cậy: {p.PrimaryPattern.Confidence})");
        sb.AppendLine($"🛡️ Hỗ trợ: `{p.Zones.Support.Low:F2} - {p.Zones.Support.High:F2}` ({p.Zones.Support.TouchCount} lần chạm)");
        sb.AppendLine($"⚔️ Kháng cự: `{p.Zones.Resistance.Low:F2} - {p.Zones.Resistance.High:F2}` ({p.Zones.Resistance.TouchCount} lần chạm)");
        sb.AppendLine();

        if (plan.Scenarios != null && plan.Scenarios.Count > 0)
        {
            sb.AppendLine("🎯 *KỊCH BẢN BREAKOUT ĐÃ LẬP:*\n");
            foreach (var sc in plan.Scenarios)
            {
                var icon = sc.IsBuy ? "🟢" : "🔴";
                decimal refPrice = sc.IsBreakout
                    ? (sc.TriggerPrice ?? 0m)
                    : (sc.EntryZone != null ? (sc.EntryZone.Min + sc.EntryZone.Max) / 2m : 0m);

                decimal slPts = refPrice > 0 ? Math.Abs(refPrice - sc.StopLoss) : 0m;
                var riskUsd = sc.MaxRiskUsd > 0 ? sc.MaxRiskUsd : (plan.MaxRiskUsd > 0 ? plan.MaxRiskUsd : 100m);

                sb.AppendLine($"{icon} *{sc.Name}*");
                if (sc.IsBreakout)
                {
                    sb.AppendLine($"  📍 Trigger : `{sc.TriggerPrice:F2}`");
                }
                else if (sc.EntryZone != null)
                {
                    sb.AppendLine($"  🟢 Entry   : `{sc.EntryZone.Min:F2} - {sc.EntryZone.Max:F2}`");
                }

                sb.AppendLine($"  🛡 SL      : `{sc.StopLoss:F2}` (-{slPts:F2} pts)");
                sb.AppendLine($"  💰 Rủi ro  : ${riskUsd:0.##} (lot theo SL, không vượt mức này)");

                if (sc.TakeProfits != null && sc.TakeProfits.Count > 0)
                {
                    for (int i = 0; i < sc.TakeProfits.Count; i++)
                    {
                        var tp = sc.TakeProfits[i];
                        decimal tpPts = refPrice > 0 ? Math.Abs(tp - refPrice) : 0m;
                        decimal rr = slPts > 0 ? (tpPts / slPts) : 0m;
                        sb.AppendLine($"  🎯 TP{i + 1}    : `{tp:F2}` (+{tpPts:F2} pts) R:R 1:{rr:F1}");
                    }
                }

                sb.AppendLine($"  💡 Logic   : {sc.Logic}\n");
            }
        }
        else
        {
            sb.AppendLine("⏳ *ĐANG THEO DÕI:* Giá nằm trong vùng tích lũy, chưa kích hoạt phá vỡ.");
        }

        return sb.ToString();
    }
}
