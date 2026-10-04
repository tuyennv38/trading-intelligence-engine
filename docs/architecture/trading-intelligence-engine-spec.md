# Trading Intelligence Engine — System Architecture & Implementation Specification

> Repository: `trading-intelligence-engine`
>
> Mục tiêu: xây dựng nền tảng phân tích và ra quyết định trading có kiến trúc module hóa. **Market Structure Engine là module đầu tiên**, sau đó có thể mở rộng sang Strategy, AI, Risk, Backtesting và Execution.

## 1. Vision

```text
Market Data
    ↓
Data Normalization
    ↓
Market Analysis
    ├── Market Structure
    ├── Technical Analysis
    ├── Liquidity
    ├── Support / Resistance
    ├── Volatility
    └── Multi-Timeframe
    ↓
Market State
    ↓
Signal / Strategy Engine
    ↓
AI Decision Engine
    ↓
Risk Management
    ↓
Execution Engine
    ↓
MT5 / Exchange / Broker
```

Hệ thống **không** được thiết kế như API chỉ nhận nến và trả BUY/SELL. Phân tích thị trường, quyết định, quản trị rủi ro và execution phải là các tầng độc lập.

---

# 2. Product Scope

## V1

Chỉ triển khai:

```text
OHLCV
 ↓
Market Structure
 ↓
Technical Indicators
 ↓
Liquidity
 ↓
Market Regime
 ↓
Structured Market State
```

Chưa triển khai:

- AI autonomous trading
- Live trading
- Order execution
- Portfolio management

## Future

```text
V1  Market Analysis
V2  Signal / Strategy
V3  AI Decision
V4  Risk Management
V5  Execution
V6  Backtesting
V7  Paper Trading
V8  Live Trading
```

---

# 3. Repository / Solution

Repository:

```text
trading-intelligence-engine/
```

Solution:

```text
TradingIntelligenceEngine.sln
```

Đề xuất structure:

```text
src/
├── TradingIntelligenceEngine.Api/
├── TradingIntelligenceEngine.Application/
├── TradingIntelligenceEngine.Domain/
├── TradingIntelligenceEngine.MarketData/
├── TradingIntelligenceEngine.MarketStructure/
├── TradingIntelligenceEngine.TechnicalAnalysis/
├── TradingIntelligenceEngine.Liquidity/
├── TradingIntelligenceEngine.Regime/
├── TradingIntelligenceEngine.Signal/
├── TradingIntelligenceEngine.AI/
├── TradingIntelligenceEngine.Risk/
├── TradingIntelligenceEngine.Execution/
├── TradingIntelligenceEngine.Backtesting/
└── TradingIntelligenceEngine.Infrastructure/

tests/
├── TradingIntelligenceEngine.Domain.Tests/
├── TradingIntelligenceEngine.MarketStructure.Tests/
├── TradingIntelligenceEngine.TechnicalAnalysis.Tests/
├── TradingIntelligenceEngine.Signal.Tests/
└── TradingIntelligenceEngine.Backtesting.Tests/

docs/
├── architecture/
├── market-structure/
├── ai/
├── strategy/
└── api/
```

V1 chỉ cần triển khai các module cần thiết; không tạo code giả cho tất cả module tương lai.

---

# 4. Architectural Principles

## Separation of Concerns

Không trộn:

```text
Market Analysis
Strategy
AI
Risk
Execution
```

Ví dụ `MarketStructure` không được biết về:

```text
BUY
SELL
Order
Lot
StopLoss
TakeProfit
Broker
```

Market Structure chỉ trả:

```text
Swing
HH
HL
LH
LL
BOS
CHOCH
Trend
Structure State
```

---

# 5. Domain / Market Data

## Candle

```csharp
public sealed record Candle(
    DateTimeOffset Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume
);
```

Validation:

```text
High >= Open
High >= Close
High >= Low

Low <= Open
Low <= Close

Volume >= 0
```

Không chấp nhận candle không hợp lệ.

## Instrument

Không hard-code XAUUSD.

Hỗ trợ:

```text
XAUUSD
BTCUSD
ETHUSD
SP500
EURUSD
...
```

```csharp
public sealed record MarketInstrument(
    string Symbol,
    string AssetClass
);
```

## Timeframe

```csharp
public enum Timeframe
{
    M1,
    M5,
    M15,
    M30,
    H1,
    H4,
    D1
}
```

---

# 6. Market Structure Module

Pipeline:

```text
Candles
 ↓
ATR
 ↓
Swing Detection
 ↓
Swing Confirmation
 ↓
HH / HL / LH / LL
 ↓
BOS / CHOCH
 ↓
Structure State
```

Đây là module đầu tiên phải hoàn thiện và test kỹ.

---

# 7. Swing Detection

## Swing High

Với strength = N:

```text
High[i] > High[i-N ... i-1]
High[i] >= High[i+1 ... i+N]
```

## Swing Low

```text
Low[i] < Low[i-N ... i-1]
Low[i] <= Low[i+1 ... i+N]
```

Domain:

```csharp
public enum SwingType
{
    High,
    Low
}

public enum SwingStatus
{
    Potential,
    Confirmed,
    Invalidated
}

public sealed record SwingPoint(
    int Index,
    DateTimeOffset Time,
    SwingType Type,
    decimal Price,
    int Strength,
    SwingStatus Status
);
```

---

# 8. Adaptive Swing

Không dùng fixed threshold duy nhất.

Dùng ATR:

```text
minimumSwingDistance = ATR × MinAtrMultiplier
```

Config:

```csharp
public sealed class SwingOptions
{
    public int FractalStrength { get; init; } = 3;
    public decimal MinAtrMultiplier { get; init; } = 0.5m;
}
```

Mọi threshold phải configurable, không hard-code trong algorithm.

---

# 9. Confirmed / Potential Swing

Swing cần dữ liệu phía sau để xác nhận.

Phải phân biệt:

```text
Potential
Confirmed
Invalidated
```

Một confirmed swing không được âm thầm thay đổi khi có candle mới.

Nếu cần thay đổi phải có trạng thái `Invalidated` và lý do.

Mục tiêu là tránh repaint.

---

# 10. ZigZag

Có thể implement:

```text
IZigZagDetector
```

để lọc noise và tìm major swings.

Threshold nên dựa trên:

```text
ATR × multiplier
```

ZigZag có thể repaint swing cuối chưa confirmed, do đó API phải phân biệt confirmed và potential.

---

# 11. Structure Labels: HH / HL / LH / LL

Swing High:

```text
Current High > Previous High => HH
Current High < Previous High => LH
```

Swing Low:

```text
Current Low > Previous Low => HL
Current Low < Previous Low => LL
```

```csharp
public enum StructureLabel
{
    HH,
    HL,
    LH,
    LL
}
```

Ví dụ:

```text
HL → HH → HL → HH
```

=> Bullish structure.

```text
LH → LL → LH → LL
```

=> Bearish structure.

Không kết luận trend từ một swing duy nhất.

---

# 12. BOS — Break of Structure

## Bullish BOS

Trong bullish structure:

```text
HH → HL → HH
```

Nếu candle đóng cửa:

```text
Close > confirmed previous HH
```

=> Bullish BOS.

## Bearish BOS

Trong bearish structure:

```text
LL → LH → LL
```

Nếu:

```text
Close < confirmed previous LL
```

=> Bearish BOS.

Mặc định xác nhận bằng candle close. Wick-only break không được coi là BOS.

---

# 13. CHOCH — Change of Character

## Bullish CHOCH

Trong bearish structure:

```text
LH → LL → LH → LL
```

Nếu:

```text
Close > LH gần nhất
```

=> Bullish CHOCH.

## Bearish CHOCH

Trong bullish structure:

```text
HL → HH → HL → HH
```

Nếu:

```text
Close < HL gần nhất
```

=> Bearish CHOCH.

CHOCH chỉ là:

```text
potential structure transition
```

không phải guaranteed reversal.

---

# 14. BOS vs CHOCH

| Event | Break | Ý nghĩa |
|---|---|---|
| Bullish BOS | HH | Bullish continuation |
| Bearish BOS | LL | Bearish continuation |
| Bullish CHOCH | LH | Possible bullish transition |
| Bearish CHOCH | HL | Possible bearish transition |

Domain:

```csharp
public enum StructureEventType
{
    BOS,
    CHOCH
}

public enum EventDirection
{
    Bullish,
    Bearish
}
```

---

# 15. Liquidity Module

## Equal High

```text
abs(H1 - H2) <= ATR × tolerance
```

=> Equal High / Buy-side liquidity.

## Equal Low

```text
abs(L1 - L2) <= ATR × tolerance
```

=> Equal Low / Sell-side liquidity.

## Liquidity Sweep

Buy-side:

```text
High > liquidityLevel
AND
Close < liquidityLevel
```

Sell-side:

```text
Low < liquidityLevel
AND
Close > liquidityLevel
```

Chỉ gọi là sweep nếu liquidity level đã được xác định trước.

---

# 16. Support / Resistance

V1 sử dụng swing-based zones.

Các swing gần nhau được cluster theo ATR.

Ví dụ:

```text
3849.2
3850.1
3850.5
3851.0
```

=> resistance zone:

```text
3849.2 - 3851.0
```

---

# 17. Technical Analysis Module

Indicator chỉ cung cấp context, không trực tiếp quyết định BUY/SELL.

V1:

```text
EMA34
EMA89
RSI14
ATR14
ADX14
```

Features:

```text
EMA alignment
EMA slope
EMA distance
RSI
ATR
ATR percentile
ADX
```

---

# 18. Market Regime

```csharp
public enum MarketRegime
{
    TrendingUp,
    TrendingDown,
    Ranging,
    Transition,
    HighVolatility,
    LowVolatility
}
```

TrendingUp có thể dựa trên:

```text
Bullish structure
HH + HL
EMA34 > EMA89
ADX >= threshold
```

TrendingDown:

```text
Bearish structure
LL + LH
EMA34 < EMA89
ADX >= threshold
```

Ranging:

```text
Structure không rõ
ADX thấp
Giá nằm trong range
```

Transition:

```text
CHOCH vừa xảy ra
hoặc các signals xung đột
```

Volatility có thể là flag bổ sung thay vì luôn thay thế trend/range.

---

# 19. Market State

Tất cả phân tích được gom thành một object:

```csharp
public sealed class MarketState
{
    public string Symbol { get; init; }
    public Timeframe Timeframe { get; init; }
    public TrendState Trend { get; init; }
    public StructureState Structure { get; init; }
    public IReadOnlyList<StructureEvent> Events { get; init; }
    public LiquidityState Liquidity { get; init; }
    public TechnicalState Technical { get; init; }
    public RegimeState Regime { get; init; }
}
```

`MarketState` là output chính của Market Analysis layer.

---

# 20. Market Structure Score

Có thể tổng hợp:

```text
Structure       +30
BOS             +20
EMA alignment   +15
ADX             +10
RSI             +5
Volume          +10
Liquidity       +10
-------------------
Maximum         100
```

Score chỉ biểu thị strength của market context.

Không gọi score này là win probability.

Nếu sau này ML tạo probability thì đó là field riêng.

---

# 21. Multi-Timeframe

Thiết kế để hỗ trợ:

```text
H4
 ↓
H1
 ↓
M15
 ↓
M5
 ↓
M1
```

Ví dụ:

```text
H4 = Bullish
H1 = Bullish
M15 = Pullback
M5 = Bearish
```

Phải phân biệt:

```text
Higher Timeframe Bias
Current Timeframe State
Lower Timeframe State
```

Không coi M5 bearish là toàn thị trường bearish.

---

# 22. Strategy / Signal Layer — Future

Market Structure không tạo order.

Future:

```text
MarketState
 ↓
Strategy Engine
 ↓
Signal
```

Ví dụ:

```json
{
  "direction": "Long",
  "setup": "PullbackAfterBullishBOS",
  "score": 82
}
```

Strategy Engine chứa trading logic; Market Structure không chứa strategy-specific code.

---

# 23. AI Decision Engine — Future

Không:

```text
Candle → LLM → BUY
```

Mà:

```text
Candle
 ↓
Market Analysis
 ↓
MarketState
 ↓
Strategy Context
 ↓
AI Decision
```

AI có thể nhận structured context:

```json
{
  "symbol": "XAUUSD",
  "marketState": {
    "trend": "Bullish",
    "structure": ["HL", "HH", "HL", "HH"],
    "lastEvent": "BOS",
    "regime": "TrendingUp"
  },
  "technical": {
    "ema34": 3848.2,
    "ema89": 3839.5,
    "rsi": 64.2,
    "adx": 31.4,
    "atr": 5.2
  },
  "multiTimeframe": {
    "H4": "Bullish",
    "H1": "Bullish",
    "M15": "Pullback",
    "M5": "Bullish"
  }
}
```

AI output phải structured:

```json
{
  "decision": "BUY",
  "confidence": 0.78,
  "reasoning": [
    "Higher timeframe bullish",
    "Bullish BOS confirmed",
    "M15 pullback",
    "M5 structure aligned"
  ],
  "invalidations": [
    "Price breaks below last HL"
  ]
}
```

AI không được trực tiếp gửi order.

---

# 24. AI Provider Abstraction

Không khóa vào một provider:

```csharp
public interface IAiDecisionEngine
{
    Task<AiDecision> DecideAsync(
        MarketDecisionContext context,
        CancellationToken cancellationToken);
}
```

Có thể implement:

```text
OpenAI
Anthropic
Local LLM
Custom ML Model
Rule-based Engine
```

---

# 25. Decision Layer

Tách AI khỏi decision orchestration.

Có thể hỗ trợ:

```text
Rule Strategy
AI Strategy
Hybrid Strategy
```

Ví dụ:

```text
Rule Engine → BUY
AI → BUY
Risk Engine → APPROVE
```

=> Execute.

Nếu:

```text
Rule Engine → BUY
AI → SELL
```

=> configurable, mặc định `WAIT / REVIEW`.

---

# 26. Risk Management — Future

Risk Engine nhận:

```text
Signal
MarketState
AccountState
```

và quyết định:

```text
Allowed / Rejected
Position Size
Stop Loss
Take Profit
Max Risk
```

Ví dụ:

```json
{
  "approved": true,
  "riskPercent": 0.5,
  "positionSize": 0.12,
  "stopLoss": 3838.5,
  "takeProfit": 3865.0
}
```

AI không được bypass Risk Engine.

---

# 27. Execution Engine — Future

```text
Risk-approved Order
       ↓
Execution Engine
       ↓
Broker Adapter
```

Adapters tương lai:

```text
MT5
Binance
Bybit
Exness
Vantage
Other Broker
```

```csharp
public interface ITradingExecutor
{
    Task<OrderResult> ExecuteAsync(
        TradingOrder order,
        CancellationToken cancellationToken);
}
```

Market Analysis không biết broker API.

---

# 28. Backtesting Engine — Future

Backtest phải sử dụng **cùng Market Analysis code** với live system:

```text
Historical Candle
       ↓
Market Analysis
       ↓
Strategy
       ↓
Risk
       ↓
Virtual Execution
       ↓
Result
```

Không tạo một implementation riêng cho backtest.

---

# 29. Data Flow

Historical:

```text
Database
 ↓
Candle Repository
 ↓
Market Analysis
 ↓
Strategy
 ↓
Backtest
```

Live:

```text
Market Data Provider
 ↓
Candle Stream
 ↓
Market Analysis
 ↓
Strategy
 ↓
AI
 ↓
Risk
 ↓
Execution
```

---

# 30. API Design

V1:

```http
POST /api/v1/market-analysis/analyze
```

Request:

```json
{
  "symbol": "XAUUSD",
  "timeframe": "M5",
  "candles": []
}
```

Response:

```json
{
  "symbol": "XAUUSD",
  "timeframe": "M5",
  "marketState": {
    "trend": {
      "direction": "Bullish",
      "strength": 0.82
    },
    "structure": {
      "lastLabel": "HH",
      "sequence": ["HL", "HH", "HL", "HH"]
    },
    "events": [
      {
        "type": "BOS",
        "direction": "Bullish",
        "price": 3852.4,
        "candleIndex": 120
      }
    ],
    "liquidity": {
      "buySide": [3860.5],
      "sellSide": [3840.2]
    },
    "technical": {
      "ema34": 3848.2,
      "ema89": 3839.5,
      "rsi": 64.2,
      "adx": 31.4,
      "atr": 5.2
    },
    "regime": {
      "type": "TrendingUp",
      "confidence": 0.86
    }
  }
}
```

Future endpoints:

```text
POST /api/v1/signals/analyze
POST /api/v1/ai/decision
POST /api/v1/risk/evaluate
POST /api/v1/orders/execute
POST /api/v1/backtest/run
```

---

# 31. Dependency Rules

Dependency direction:

```text
Api
 ↓
Application
 ↓
Domain
```

Module dependencies:

```text
MarketStructure → Domain
TechnicalAnalysis → Domain
Liquidity → Domain
Regime → Domain + Analysis abstractions

Strategy → MarketState
AI → MarketState + Strategy Context
Risk → Signal + MarketState + AccountState
Execution → Risk-approved Order
```

Không được:

```text
Domain → API
Domain → AI Provider
Domain → Broker
MarketStructure → MT5
MarketStructure → OpenAI
```

---

# 32. Core Interfaces

```csharp
public interface ISwingDetector
{
    IReadOnlyList<SwingPoint> Detect(
        IReadOnlyList<Candle> candles,
        SwingOptions options);
}
```

```csharp
public interface IStructureAnalyzer
{
    IReadOnlyList<StructurePoint> Analyze(
        IReadOnlyList<SwingPoint> swings);
}
```

```csharp
public interface IStructureEventDetector
{
    IReadOnlyList<StructureEvent> Detect(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<StructurePoint> structure);
}
```

```csharp
public interface ITechnicalAnalyzer
{
    TechnicalState Analyze(
        IReadOnlyList<Candle> candles);
}
```

```csharp
public interface IMarketAnalyzer
{
    MarketState Analyze(
        MarketAnalysisRequest request);
}
```

```csharp
public interface IAiDecisionEngine
{
    Task<AiDecision> DecideAsync(
        MarketDecisionContext context,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IRiskEngine
{
    RiskDecision Evaluate(
        TradingSignal signal,
        MarketState marketState,
        AccountState accountState);
}
```

```csharp
public interface ITradingExecutor
{
    Task<OrderResult> ExecuteAsync(
        TradingOrder order,
        CancellationToken cancellationToken);
}
```

---

# 33. Explainability

Mọi major output phải giải thích được.

Ví dụ:

```json
{
  "trend": "Bullish",
  "evidence": [
    "Latest confirmed swing sequence is HL → HH → HL → HH",
    "Bullish BOS confirmed at 3852.4",
    "EMA34 is above EMA89",
    "ADX is 31.4"
  ]
}
```

Không chỉ trả kết quả cuối cùng mà phải lưu evidence để debug và audit.

---

# 34. No Repainting / No Look-Ahead Bias

Mọi result quan trọng phải có:

```text
timestamp
candleIndex
status
confirmedAt
```

Ví dụ:

```json
{
  "type": "BOS",
  "status": "Confirmed",
  "candleIndex": 120,
  "confirmedAt": "2026-10-04T10:05:00Z"
}
```

Trong historical backtest, tại candle N không được biết candle N+1.

Ví dụ fractal strength = 3:

```text
Swing tại N
chỉ confirmed sau N+1, N+2, N+3
```

Backtest phải phản ánh delay này.

---

# 35. Incremental Processing

Live system không nên phân tích lại toàn bộ 10,000 candles khi có candle mới.

```text
Initial 1000 candles
       ↓
Full Analysis
       ↓
State

New candle
       ↓
Incremental Update
       ↓
Updated State
```

State cần giữ:

```text
Last confirmed swing
Last potential swing
Current structure
Last BOS
Last CHOCH
Liquidity levels
Indicator state
Regime
```

---

# 36. Configuration

Tất cả thresholds tập trung:

```json
{
  "MarketStructure": {
    "Swing": {
      "FractalStrength": 3,
      "MinAtrMultiplier": 0.5
    },
    "Break": {
      "Confirmation": "Close"
    },
    "Liquidity": {
      "EqualLevelAtrTolerance": 0.2
    }
  },
  "Indicators": {
    "EmaFast": 34,
    "EmaSlow": 89,
    "RsiPeriod": 14,
    "AtrPeriod": 14,
    "AdxPeriod": 14
  }
}
```

Không hard-code parameters trong classes.

---

# 37. Logging / Observability

Major events:

```text
Swing confirmed
BOS detected
CHOCH detected
Liquidity created
Liquidity swept
Regime changed
Signal generated
AI decision generated
Risk rejected/approved
Order executed
```

Future metrics:

```text
Analysis latency
Candles processed
Signals generated
AI latency
Risk rejection rate
Execution latency
Order success rate
```

---

# 38. ML Evolution

Sau khi có historical data:

```text
Candles
 ↓
Market Analysis
 ↓
Features
 ↓
Historical Outcome
 ↓
Dataset
 ↓
ML
```

Features:

```text
trend
structure sequence
BOS count
CHOCH count
ATR
ATR percentile
ADX
RSI
EMA distance
volume ratio
liquidity distance
HTF trend
distance to support
distance to resistance
```

ML không thay thế deterministic Market Structure Engine.

ML học:

```text
Current market state
→ probability / expected outcome
```

---

# 39. Confidence Separation

Không gộp mọi thứ thành một `confidence`.

Phân biệt:

```text
Structure Strength
Strategy Score
AI Confidence
ML Probability
```

Ví dụ:

```json
{
  "structureStrength": 0.84,
  "strategyScore": 82,
  "aiConfidence": 0.78,
  "modelProbability": 0.71
}
```

---

# 40. Trading Safety

Khi live trading:

```text
AI
 ↓
Signal
 ↓
Risk Engine
 ↓
Execution Policy
 ↓
Broker
```

Risk Engine có quyền:

```text
REJECT
```

bất kể AI quyết định gì.

Không cho AI gọi broker trực tiếp.

---

# 41. Implementation Roadmap

## Phase 1 — Foundation

```text
Solution
Domain
API
Candle
Timeframe
Instrument
Validation
Configuration
```

## Phase 2 — Market Structure

```text
ATR
Swing
Adaptive Swing
ZigZag
HH/HL/LH/LL
BOS
CHOCH
Tests
```

## Phase 3 — Market Context

```text
EMA
RSI
ADX
ATR
Liquidity
Support/Resistance
Market Regime
```

## Phase 4 — Market State

```text
MarketState
Multi-Timeframe
Explainability
Incremental processing
```

## Phase 5 — Strategy

```text
Signal
Strategy abstraction
Rule-based strategies
```

## Phase 6 — Backtesting

```text
Historical data
Event simulation
Strategy simulation
Performance metrics
```

## Phase 7 — AI

```text
AI Provider abstraction
AI Decision Engine
Prompt/context builder
Structured output
AI evaluation
```

## Phase 8 — Risk

```text
Position sizing
SL/TP
Max risk
Daily loss
Exposure
Correlation
```

## Phase 9 — Execution

```text
MT5 adapter
Exchange adapters
Order management
Position management
```

## Phase 10 — Live Trading

```text
Paper trading
Monitoring
Kill switch
Alerts
Audit
```

---

# 42. V1 Definition of Done

- API nhận OHLCV.
- Candle validation hoạt động.
- Swing detection hoạt động.
- Adaptive Swing hoạt động.
- Confirmed/Potential Swing được phân biệt.
- HH/HL/LH/LL chính xác.
- Bullish/Bearish structure xác định được.
- Bullish/Bearish BOS chính xác.
- Bullish/Bearish CHOCH chính xác.
- Wick-only break không bị nhầm là BOS khi dùng Close confirmation.
- Liquidity levels xác định được.
- Liquidity Sweep xác định được.
- EMA34/EMA89 hoạt động.
- RSI14 hoạt động.
- ATR14 hoạt động.
- ADX14 hoạt động.
- Market Regime hoạt động.
- MarketState được tạo.
- Multi-Timeframe có kiến trúc hỗ trợ.
- Unit tests bao phủ các case quan trọng.
- Không có look-ahead bias.
- Không repaint confirmed results.
- API response có explanation.
- Configuration không hard-code.
- Market Structure không phụ thuộc AI.
- Market Structure không phụ thuộc broker/execution.

---

# 43. Coding Instructions for AI Agent

AI coding agent phải:

1. Đọc toàn bộ specification trước khi code.
2. Không triển khai tất cả phase cùng lúc.
3. Bắt đầu từ Phase 1.
4. Sau đó triển khai Phase 2.
5. Viết unit test sau mỗi algorithm.
6. Chạy test trước khi chuyển phase.
7. Không thêm BUY/SELL logic vào MarketStructure.
8. Không gọi AI từ Domain layer.
9. Không gọi broker từ Market Analysis.
10. Không hard-code thresholds.
11. Không sử dụng future candles trong historical decision.
12. Không repaint confirmed events.
13. Mọi event phải có timestamp và candle index.
14. Giữ algorithm deterministic ở V1.
15. Thiết kế interface để thay thế implementation.
16. Không tạo dependency ngược giữa các layers.
17. Khi định nghĩa trading chưa rõ, chọn behavior deterministic và ghi assumption vào test/documentation.
18. Không thêm framework/dependency lớn nếu chưa cần.
19. Ưu tiên correctness và testability trước performance ở V1.
20. Không tự động biến phân tích thành trading order.

---

# 44. Final Architecture

```text
                         ┌───────────────────┐
                         │   Market Data     │
                         │ OHLCV / OI / Vol  │
                         └─────────┬─────────┘
                                   ↓
                         ┌───────────────────┐
                         │ Market Analysis   │
                         │                   │
                         │ Structure         │
                         │ Technical         │
                         │ Liquidity         │
                         │ Regime            │
                         │ Multi-Timeframe   │
                         └─────────┬─────────┘
                                   ↓
                         ┌───────────────────┐
                         │   Market State    │
                         └─────────┬─────────┘
                                   ↓
                         ┌───────────────────┐
                         │ Strategy / Signal │
                         └─────────┬─────────┘
                                   ↓
                         ┌───────────────────┐
                         │   AI Decision     │
                         │ BUY / SELL / WAIT │
                         └─────────┬─────────┘
                                   ↓
                         ┌───────────────────┐
                         │ Risk Management   │
                         └─────────┬─────────┘
                                   ↓
                         ┌───────────────────┐
                         │ Execution Engine  │
                         └─────────┬─────────┘
                                   ↓
                    ┌──────────────┴──────────────┐
                    ↓                             ↓
                   MT5                         Exchange
```

Core principle:

```text
Market Analysis
       ≠
Strategy
       ≠
AI Decision
       ≠
Risk
       ≠
Execution
```

Mỗi tầng độc lập, có interface rõ ràng và test riêng.

---

# 45. Long-term Goal

Hệ thống cuối cùng có khả năng:

```text
1. Nhận market data
2. Hiểu cấu trúc thị trường
3. Xác định trend / regime / liquidity
4. Xây dựng MarketState
5. Áp dụng strategy
6. Cho AI đánh giá context
7. Tạo BUY / SELL / WAIT
8. Kiểm tra risk
9. Backtest
10. Paper trade
11. Live trade
12. Ghi lại reasoning và outcome
```

Nhưng **Market Structure Engine luôn phải tồn tại độc lập với AI**.

Có thể vận hành:

```text
AI OFF
→ Rule-based analysis/trading

AI ON
→ AI-assisted trading

ML ON
→ Model-assisted trading

AI/ML OFF
→ Pure deterministic analysis
```

Đây là kiến trúc nền tảng cho repository `trading-intelligence-engine`.