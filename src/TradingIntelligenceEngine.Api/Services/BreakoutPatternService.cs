using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        var atr = IndicatorMath.CalculateAtr(candles, 14);
        if (atr <= 0) atr = 2.0m;

        // 1. Dò tìm điểm xoay (Swing Highs / Swing Lows) - giới hạn 60 nến gần nhất để bám sát vùng giá hiện tại
        var recentCutoffIndex = Math.Max(0, candles.Count - 60);
        var swings = _swingDetector.Detect(candles, _swingOptions);
        var recentSwings = swings.Where(s => s.Index >= recentCutoffIndex).ToList();
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

        // 2. Phân tích các vùng biên (Support & Resistance) bám sát theo giá hiện tại
        var tolerance = atr * 0.4m;
        var (resZone, resTouches) = ClusterSwings(swingHighs.Select(s => s.Price).ToList(), currentPrice, isResistance: true, tolerance: tolerance);
        var (supZone, supTouches) = ClusterSwings(swingLows.Select(s => s.Price).ToList(), currentPrice, isResistance: false, tolerance: tolerance);

        result.Zones.Resistance = new BoundaryZone
        {
            Low = resZone.Item1,
            High = resZone.Item2,
            TouchCount = resTouches
        };

        result.Zones.Support = new BoundaryZone
        {
            Low = supZone.Item1,
            High = supZone.Item2,
            TouchCount = supTouches
        };

        // 3. Phân loại cấu trúc theo 4 nhóm trong Skill (A, B, C, D)
        var candidates = new List<PatternDetail>();
        var evidence = new List<string>();
        var missing = new List<string>();

        // Nhóm A: Horizontal (Rectangle / Double Top/Bottom)
        bool isHorizontalRange = resTouches >= 2 && supTouches >= 2 &&
            resZone.Item1.HasValue && supZone.Item2.HasValue &&
            (resZone.Item1.Value > supZone.Item2.Value);

        if (isHorizontalRange)
        {
            candidates.Add(new PatternDetail
            {
                Group = "A",
                Name = "RECTANGLE",
                Status = (resTouches >= 3 || supTouches >= 3) ? "CONFIRMED" : "POTENTIAL",
                Confidence = (resTouches >= 3 && supTouches >= 3) ? "HIGH" : "MEDIUM"
            });
            evidence.Add($"Giá dao động giữa Kháng cự ({resZone.Item1:F2} - {resZone.Item2:F2}, {resTouches} lần chạm) và Hỗ trợ ({supZone.Item1:F2} - {supZone.Item2:F2}, {supTouches} lần chạm).");
        }
        else if (resTouches >= 2 && swingHighs.Count >= 2)
        {
            candidates.Add(new PatternDetail
            {
                Group = "A",
                Name = resTouches >= 3 ? "MULTIPLE_TOP" : "DOUBLE_TOP",
                Status = "POTENTIAL",
                Confidence = "MEDIUM"
            });
            evidence.Add($"Xuất hiện {resTouches} đỉnh tiệm cận vùng kháng cự {resZone.Item1:F2} - {resZone.Item2:F2}.");
        }
        else if (supTouches >= 2 && swingLows.Count >= 2)
        {
            candidates.Add(new PatternDetail
            {
                Group = "A",
                Name = supTouches >= 3 ? "MULTIPLE_BOTTOM" : "DOUBLE_BOTTOM",
                Status = "POTENTIAL",
                Confidence = "MEDIUM"
            });
            evidence.Add($"Xuất hiện {supTouches} đáy tiệm cận vùng hỗ trợ {supZone.Item1:F2} - {supZone.Item2:F2}.");
        }

        // Nhóm B: Triangles (Symmetrical, Ascending, Descending)
        if (swingHighs.Count >= 2 && swingLows.Count >= 2)
        {
            var lastHighs = swingHighs.TakeLast(3).ToList();
            var lastLows = swingLows.TakeLast(3).ToList();

            bool highsDescending = lastHighs.Zip(lastHighs.Skip(1), (prev, curr) => curr.Price < prev.Price).All(x => x);
            bool lowsAscending = lastLows.Zip(lastLows.Skip(1), (prev, curr) => curr.Price > prev.Price).All(x => x);
            bool highsAscending = lastHighs.Zip(lastHighs.Skip(1), (prev, curr) => curr.Price > prev.Price).All(x => x);
            bool lowsDescending = lastLows.Zip(lastLows.Skip(1), (prev, curr) => curr.Price < prev.Price).All(x => x);
            bool highsFlat = resTouches >= 2 && (resZone.Item2 - resZone.Item1) <= tolerance;
            bool lowsFlat = supTouches >= 2 && (supZone.Item2 - supZone.Item1) <= tolerance;

            if (highsFlat && lowsAscending)
            {
                candidates.Add(new PatternDetail
                {
                    Group = "B",
                    Name = "ASCENDING_TRIANGLE",
                    Status = "CONFIRMED",
                    Confidence = "HIGH"
                });
                evidence.Add($"Kháng cự phẳng tại {resZone.Item1:F2} kết hợp đáy nâng cao dần (Higher Lows). Thiên hướng Breakout Tăng.");
            }
            else if (lowsFlat && highsDescending)
            {
                candidates.Add(new PatternDetail
                {
                    Group = "B",
                    Name = "DESCENDING_TRIANGLE",
                    Status = "CONFIRMED",
                    Confidence = "HIGH"
                });
                evidence.Add($"Hỗ trợ phẳng tại {supZone.Item1:F2} kết hợp đỉnh hạ thấp dần (Lower Highs). Thiên hướng Breakdown Giảm.");
            }
            else if (highsDescending && lowsAscending)
            {
                candidates.Add(new PatternDetail
                {
                    Group = "B",
                    Name = "SYMMETRICAL_TRIANGLE",
                    Status = "CONFIRMED",
                    Confidence = "MEDIUM"
                });
                evidence.Add("Đỉnh hạ dần (Lower Highs) và Đáy nâng dần (Higher Lows) tạo tam giác đối xứng co hẹp biên độ.");
            }
            // Nhóm C: Trendlines & Channels (Wedges & Channels)
            else if (highsAscending && lowsAscending)
            {
                var upperSpread = lastHighs.Last().Price - lastLows.Last().Price;
                var initialSpread = lastHighs.First().Price - lastLows.First().Price;
                if (upperSpread < initialSpread * 0.75m)
                {
                    candidates.Add(new PatternDetail
                    {
                        Group = "C",
                        Name = "RISING_WEDGE",
                        Status = "CONFIRMED",
                        Confidence = "MEDIUM"
                    });
                    evidence.Add("Đỉnh và Đáy cùng dốc lên nhưng biên độ hội tụ co hẹp dần (Nêm tăng). Cảnh báo đảo chiều giảm.");
                }
                else
                {
                    candidates.Add(new PatternDetail
                    {
                        Group = "C",
                        Name = "ASCENDING_CHANNEL",
                        Status = "CONFIRMED",
                        Confidence = "MEDIUM"
                    });
                    evidence.Add("Đỉnh cao dần (HH) và Đáy cao dần (HL) di chuyển trong kênh giá tăng song song.");
                }
            }
            else if (highsDescending && lowsDescending)
            {
                var lowerSpread = lastHighs.Last().Price - lastLows.Last().Price;
                var initialSpread = lastHighs.First().Price - lastLows.First().Price;
                if (lowerSpread < initialSpread * 0.75m)
                {
                    candidates.Add(new PatternDetail
                    {
                        Group = "C",
                        Name = "FALLING_WEDGE",
                        Status = "CONFIRMED",
                        Confidence = "MEDIUM"
                    });
                    evidence.Add("Đỉnh và Đáy cùng dốc xuống nhưng biên độ hội tụ co hẹp dần (Nêm giảm). Cảnh báo đảo chiều tăng.");
                }
                else
                {
                    candidates.Add(new PatternDetail
                    {
                        Group = "C",
                        Name = "DESCENDING_CHANNEL",
                        Status = "CONFIRMED",
                        Confidence = "MEDIUM"
                    });
                    evidence.Add("Đỉnh thấp dần (LH) và Đáy thấp dần (LL) di chuyển trong kênh giá giảm song song.");
                }
            }
        }

        // Nhóm D: Continuation (Bull Flag / Bear Flag)
        if (candles.Count >= 15)
        {
            var impulseLookback = Math.Min(10, candles.Count / 2);
            var startCandle = candles[candles.Count - impulseLookback];
            var endCandle = candles.Last();
            var priceChange = endCandle.Close - startCandle.Open;

            if (priceChange > atr * 3.0m)
            {
                candidates.Add(new PatternDetail
                {
                    Group = "D",
                    Name = "BULL_FLAG",
                    Status = "DEVELOPING",
                    Confidence = "MEDIUM"
                });
                evidence.Add($"Sóng đẩy tăng mạnh (+{priceChange:F2}) trước nhịp tích lũy.");
            }
            else if (priceChange < -atr * 3.0m)
            {
                candidates.Add(new PatternDetail
                {
                    Group = "D",
                    Name = "BEAR_FLAG",
                    Status = "DEVELOPING",
                    Confidence = "MEDIUM"
                });
                evidence.Add($"Sóng đẩy giảm mạnh ({priceChange:F2}) trước nhịp tích lũy.");
            }
        }

        // 4. Quyết định Primary Pattern
        if (candidates.Count > 0)
        {
            result.PrimaryPattern = candidates.First();
            result.OtherCandidates = candidates.Skip(1).ToList();
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
        var atr = patternResult.Atr > 0 ? patternResult.Atr : 2.5m;
        var signalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol}_BO";

        var resBoundary = patternResult.Zones.Resistance.High ?? (currentPrice + atr * 2m);
        var supBoundary = patternResult.Zones.Support.Low ?? (currentPrice - atr * 2m);
        var buffer = Math.Round(atr * 0.25m, 2);

        var triggerBuy = Math.Round(resBoundary + buffer, 2);
        var triggerSell = Math.Round(supBoundary - buffer, 2);

        // Khoảng cách rủi ro SL theo quy chuẩn (tối thiểu 5 giá, tối đa 10 giá cho Vàng)
        var slDistance = Math.Clamp(Math.Round(atr * 1.8m, 2), 5.0m, 9.5m);

        var slBuy = Math.Round(triggerBuy - slDistance, 2);
        var tp1Buy = Math.Round(triggerBuy + slDistance * 1.2m, 2);
        var tp2Buy = Math.Round(triggerBuy + slDistance * 2.2m, 2);

        var slSell = Math.Round(triggerSell + slDistance, 2);
        var tp1Sell = Math.Round(triggerSell - slDistance * 1.2m, 2);
        var tp2Sell = Math.Round(triggerSell - slDistance * 2.2m, 2);

        var plan = new TradingPlanResponse
        {
            SignalId = signalId,
            Symbol = symbol,
            Bias = patternResult.MarketState == "TREND_UP" ? "BULLISH" : (patternResult.MarketState == "TREND_DOWN" ? "BEARISH" : "NEUTRAL_RANGE"),
            Context = $"Nhận diện mô hình [{patternResult.PrimaryPattern.Name}] ({patternResult.PrimaryPattern.Group}) trạng thái {patternResult.PrimaryPattern.Status}. Kháng cự: {resBoundary:F2}, Hỗ trợ: {supBoundary:F2}.",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Source = TradingPlanSource.Ai,
            MaxRiskUsd = maxRiskUsd
        };

        var scenarios = new List<EaTradeScenario>();

        // Kịch bản Breakout Mua (Breakout Up)
        var buyScenario = new EaTradeScenario
        {
            Id = $"scenario_1_breakout_buy",
            Type = EaScenarioType.BreakoutBuy,
            Name = $"Breakout Mua khi vượt {triggerBuy:F2}",
            Logic = $"Chờ nến {tf} đóng cửa dứt khoát trên mốc {triggerBuy:F2} (vượt cản {resBoundary:F2} + buffer {buffer:F2}). Sau đó chờ nhịp Retest kiểm tra lại vùng cản làm hỗ trợ mới kích hoạt Buy theo đà tiếp diễn.",
            TriggerPrice = triggerBuy,
            StopLoss = slBuy,
            MaxRiskUsd = maxRiskUsd,
            TakeProfits = new List<decimal> { tp1Buy, tp2Buy },
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

        // Kịch bản Breakdown Bán (Breakdown Down)
        var sellScenario = new EaTradeScenario
        {
            Id = $"scenario_2_breakout_sell",
            Type = EaScenarioType.BreakoutSell,
            Name = $"Breakdown Bán khi thủng {triggerSell:F2}",
            Logic = $"Chờ nến {tf} đóng cửa dứt khoát dưới mốc {triggerSell:F2} (thủng hỗ trợ {supBoundary:F2} - buffer {buffer:F2}). Sau đó chờ nhịp Retest kiểm tra lại vùng hỗ trợ cũ đóng vai trò kháng cự mới kích hoạt Sell.",
            TriggerPrice = triggerSell,
            StopLoss = slSell,
            MaxRiskUsd = maxRiskUsd,
            TakeProfits = new List<decimal> { tp1Sell, tp2Sell },
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

        // CHỈ LẬP KỊCH BẢN KHI VÙNG BIÊN ĐÃ CÓ TỐI THIỂU 2 ĐIỂM CHẠM TẠO SÓNG ĐỘC LẬP
        bool hasResConfirmed = patternResult.Zones.Resistance.TouchCount >= 2;
        bool hasSupConfirmed = patternResult.Zones.Support.TouchCount >= 2;

        if (patternResult.PrimaryPattern.Name == "ASCENDING_TRIANGLE" || patternResult.PrimaryPattern.Name == "BULL_FLAG")
        {
            if (hasResConfirmed) scenarios.Add(buyScenario);
            if (hasSupConfirmed) scenarios.Add(sellScenario);
        }
        else if (patternResult.PrimaryPattern.Name == "DESCENDING_TRIANGLE" || patternResult.PrimaryPattern.Name == "BEAR_FLAG")
        {
            if (hasSupConfirmed) scenarios.Add(sellScenario);
            if (hasResConfirmed) scenarios.Add(buyScenario);
        }
        else
        {
            // Range 2 chiều: Chỉ kích hoạt biên nào đã test ít nhất 2 lần
            if (hasResConfirmed) scenarios.Add(buyScenario);
            if (hasSupConfirmed) scenarios.Add(sellScenario);
        }

        if (scenarios.Count == 0)
        {
            plan.Context = $"Chưa đủ điều kiện Breakout: Kháng cự ({patternResult.Zones.Resistance.TouchCount} lần chạm) và Hỗ trợ ({patternResult.Zones.Support.TouchCount} lần chạm) chưa đạt tối thiểu 2 điểm chạm tạo sóng độc lập. Chờ lần test thứ 2 trước khi lập kịch bản.";
        }

        plan.Scenarios = scenarios;
        plan.EnsureDefaults(symbol, signalId, maxRiskUsd);

        // Tạo báo cáo tóm tắt
        var report = BuildSummaryReport(patternResult, buyScenario, sellScenario);

        var result = new BreakoutScenarioResult
        {
            PatternRecognition = patternResult,
            TradingPlan = plan,
            CurrentStatus = (scenarios.Count > 0 && patternResult.PrimaryPattern.Status == "CONFIRMED") ? "SCENARIO_CONFIRMED" : "WAIT",
            SummaryReport = report
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
            var patternResult = await RecognizePatternAsync(symbol, interval.ToUpperInvariant(), domainCandles, cancellationToken);
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
                scenarioResult = await PlanBreakoutScenarioAsync(patternResult, domainCandles, riskVal, cancellationToken);
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
                    scenarioResult = await PlanBreakoutScenarioAsync(aiResult.PatternRecognition, domainCandles, riskVal, cancellationToken);
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
            var patternResult = await RecognizePatternAsync(symbol, interval.ToUpperInvariant(), domainCandles, cancellationToken);
            scenarioResult = await PlanBreakoutScenarioAsync(patternResult, domainCandles, riskVal, cancellationToken);
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

    private static (Tuple<decimal?, decimal?>, int) ClusterSwings(
        List<decimal> prices,
        decimal currentPrice,
        bool isResistance,
        decimal tolerance)
    {
        if (prices.Count == 0) return (Tuple.Create<decimal?, decimal?>(null, null), 0);

        // Lọc các swing point theo đúng phía so với giá hiện tại:
        // - Kháng cự: phải nằm ở trên hoặc quanh giá hiện tại (không lấy cản quá xa bên dưới)
        // - Hỗ trợ: phải nằm ở dưới hoặc quanh giá hiện tại (không lấy hỗ trợ quá xa bên trên)
        var filteredPrices = isResistance
            ? prices.Where(p => p >= currentPrice - tolerance).ToList()
            : prices.Where(p => p <= currentPrice + tolerance).ToList();

        if (filteredPrices.Count == 0)
        {
            var nearest = prices.OrderBy(p => Math.Abs(p - currentPrice)).First();
            filteredPrices = new List<decimal> { nearest };
        }

        var clusters = new List<List<decimal>>();
        foreach (var p in filteredPrices)
        {
            var added = false;
            foreach (var c in clusters)
            {
                var avg = c.Average();
                if (Math.Abs(p - avg) <= tolerance)
                {
                    c.Add(p);
                    added = true;
                    break;
                }
            }
            if (!added)
            {
                clusters.Add(new List<decimal> { p });
            }
        }

        // Chọn cụm tốt nhất: ưu tiên cụm gần giá hiện tại nhất và có mật độ chạm cao
        var bestCluster = clusters
            .OrderBy(c => Math.Abs(c.Average() - currentPrice))
            .ThenByDescending(c => c.Count)
            .First();

        return (Tuple.Create<decimal?, decimal?>(bestCluster.Min(), bestCluster.Max()), bestCluster.Count);
    }

    private static string BuildSummaryReport(
        PatternRecognitionResult pattern,
        EaTradeScenario buyScenario,
        EaTradeScenario sellScenario)
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
        sb.AppendLine();
        sb.AppendLine("SCENARIO A - BREAKOUT UP:");
        sb.AppendLine($"- Trigger Price: {buyScenario.TriggerPrice:F2}");
        sb.AppendLine($"- Confirmation: Đóng nến trên Trigger + Retest {buyScenario.Retest?.Timeframe}");
        sb.AppendLine($"- Stop Loss: {buyScenario.StopLoss:F2} | TPs: {string.Join(", ", buyScenario.TakeProfits.Select(t => t.ToString("F2")))}");
        sb.AppendLine();
        sb.AppendLine("SCENARIO B - BREAKDOWN DOWN:");
        sb.AppendLine($"- Trigger Price: {sellScenario.TriggerPrice:F2}");
        sb.AppendLine($"- Confirmation: Đóng nến dưới Trigger + Retest {sellScenario.Retest?.Timeframe}");
        sb.AppendLine($"- Stop Loss: {sellScenario.StopLoss:F2} | TPs: {string.Join(", ", sellScenario.TakeProfits.Select(t => t.ToString("F2")))}");
        sb.AppendLine();
        sb.AppendLine($"CURRENT STATUS: {(pattern.PrimaryPattern.Status == "CONFIRMED" ? "SCENARIO_CONFIRMED" : "MONITOR")}");

        return sb.ToString();
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
