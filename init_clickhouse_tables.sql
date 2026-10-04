CREATE TABLE IF NOT EXISTS ai_decision_logs (
    session_id UUID,
    symbol String,
    timeframe String,
    timestamp DateTime,
    market_state_json String,
    system_prompt String,
    ai_response_raw String,
    decision String,
    confidence Float32,
    order_type String,
    entry_price Float64,
    sl Float64,
    tp Float64,
    latency_ms Int32,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
ORDER BY (timestamp, symbol);

CREATE TABLE IF NOT EXISTS ai_trade_outcomes (
    session_id UUID,
    outcome String,
    pnl_pips Float64,
    exit_reason String,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
ORDER BY (created_at, session_id);