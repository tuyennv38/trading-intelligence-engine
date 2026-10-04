using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octonica.ClickHouseClient;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.MarketState;

namespace TradingIntelligenceEngine.Api.Services;

public interface IClickhouseLogger
{
    Task LogDecisionAsync(Guid sessionId, string symbol, string timeframe, MarketState state, AiDecisionResult result);
    Task LogOutcomeAsync(Guid sessionId, string outcome, decimal pnlPips, string exitReason);
}

public class ClickhouseLogger : IClickhouseLogger
{
    private readonly string _connectionString;
    private readonly ILogger<ClickhouseLogger> _logger;

    public ClickhouseLogger(IConfiguration configuration, ILogger<ClickhouseLogger> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("ClickHouse") ?? string.Empty;
        
        if (!string.IsNullOrEmpty(_connectionString))
        {
            EnsureTablesCreatedAsync().ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning("ClickHouse connection string is missing. Logging is disabled.");
        }
    }

    private async Task EnsureTablesCreatedAsync()
    {
        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync();

            var createLogsTable = @"
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
                ORDER BY (timestamp, symbol);";

            var createOutcomesTable = @"
                CREATE TABLE IF NOT EXISTS ai_trade_outcomes (
                    session_id UUID,
                    outcome String,
                    pnl_pips Float64,
                    exit_reason String,
                    created_at DateTime DEFAULT now()
                ) ENGINE = MergeTree()
                ORDER BY (created_at, session_id);";

            await using var cmd1 = connection.CreateCommand(createLogsTable);
            await cmd1.ExecuteNonQueryAsync();
            
            await using var cmd2 = connection.CreateCommand(createOutcomesTable);
            await cmd2.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ClickHouse tables.");
        }
    }

    public async Task LogDecisionAsync(Guid sessionId, string symbol, string timeframe, MarketState state, AiDecisionResult result)
    {
        if (string.IsNullOrEmpty(_connectionString)) return;

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                INSERT INTO ai_decision_logs 
                (session_id, symbol, timeframe, timestamp, market_state_json, system_prompt, ai_response_raw, decision, confidence, order_type, entry_price, sl, tp, latency_ms)
                VALUES 
                (@session_id, @symbol, @timeframe, @timestamp, @market_state_json, @system_prompt, @ai_response_raw, @decision, @confidence, @order_type, @entry_price, @sl, @tp, @latency_ms)";

            await using var cmd = connection.CreateCommand(query);
            cmd.Parameters.AddWithValue("session_id", sessionId);
            cmd.Parameters.AddWithValue("symbol", symbol);
            cmd.Parameters.AddWithValue("timeframe", timeframe);
            cmd.Parameters.AddWithValue("timestamp", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("market_state_json", JsonSerializer.Serialize(state));
            cmd.Parameters.AddWithValue("system_prompt", result.Prompt);
            cmd.Parameters.AddWithValue("ai_response_raw", result.RawResponse);
            cmd.Parameters.AddWithValue("decision", result.Decision.Decision.ToString());
            cmd.Parameters.AddWithValue("confidence", (float)result.Decision.Confidence);
            cmd.Parameters.AddWithValue("order_type", result.Decision.SuggestedType?.ToString() ?? string.Empty);
            cmd.Parameters.AddWithValue("entry_price", (double)(result.Decision.SuggestedEntry ?? 0m));
            cmd.Parameters.AddWithValue("sl", (double)(result.Decision.SuggestedStopLoss ?? 0m));
            cmd.Parameters.AddWithValue("tp", (double)(result.Decision.SuggestedTakeProfit ?? 0m));
            cmd.Parameters.AddWithValue("latency_ms", (int)result.LatencyMs);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log decision to ClickHouse");
        }
    }

    public async Task LogOutcomeAsync(Guid sessionId, string outcome, decimal pnlPips, string exitReason)
    {
        if (string.IsNullOrEmpty(_connectionString)) return;

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                INSERT INTO ai_trade_outcomes 
                (session_id, outcome, pnl_pips, exit_reason)
                VALUES 
                (@session_id, @outcome, @pnl_pips, @exit_reason)";

            await using var cmd = connection.CreateCommand(query);
            cmd.Parameters.AddWithValue("session_id", sessionId);
            cmd.Parameters.AddWithValue("outcome", outcome);
            cmd.Parameters.AddWithValue("pnl_pips", (double)pnlPips);
            cmd.Parameters.AddWithValue("exit_reason", exitReason);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log outcome to ClickHouse");
        }
    }
}