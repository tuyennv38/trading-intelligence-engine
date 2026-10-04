using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClickHouse.Client.ADO;
using ClickHouse.Client.ADO.Parameters;
using ClickHouse.Client.Copy;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Api.Data;

namespace TradingIntelligenceEngine.Api.Services;

public interface IClickhouseLogger
{
    Task LogDecisionAsync(Guid sessionId, string symbol, string timeframe, string requestJson, MarketState state, AiDecisionResult result);
    Task LogOutcomeAsync(Guid sessionId, string outcome, decimal pnlPips, string exitReason);
}

public class ClickhouseLogger : IClickhouseLogger
{
    private readonly IClickhouseContext _context;
    private readonly ILogger<ClickhouseLogger> _logger;

    public ClickhouseLogger(IClickhouseContext context, ILogger<ClickhouseLogger> logger)
    {
        _context = context;
        _logger = logger;
        
        if (!string.IsNullOrEmpty(_context.ConnectionString))
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
            await using var connection = _context.CreateConnection();
            await connection.OpenAsync();

            var createLogsTable = @"
                CREATE TABLE IF NOT EXISTS ai_decision_logs (
                    session_id UUID,
                    symbol String,
                    timeframe String,
                    timestamp DateTime,
                    request_json String,
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

            await using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = createLogsTable;
            await cmd1.ExecuteNonQueryAsync();
            
            // Add column request_json if not exists
            var alterLogsTable = "ALTER TABLE ai_decision_logs ADD COLUMN IF NOT EXISTS request_json String;";
            await using var cmdAlter = connection.CreateCommand();
            cmdAlter.CommandText = alterLogsTable;
            await cmdAlter.ExecuteNonQueryAsync();
            
            await using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = createOutcomesTable;
            await cmd2.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ClickHouse tables.");
        }
    }

    public async Task LogDecisionAsync(Guid sessionId, string symbol, string timeframe, string requestJson, MarketState state, AiDecisionResult result)
    {
        if (string.IsNullOrEmpty(_context.ConnectionString)) return;

        try
        {
            await using var connection = _context.CreateConnection();
            await connection.OpenAsync();

        var query = @"
            INSERT INTO ai_decision_logs 
            (session_id, symbol, timeframe, timestamp, request_json, market_state_json, system_prompt, ai_response_raw, decision, confidence, order_type, entry_price, sl, tp, latency_ms)
            VALUES 
            (@session_id, @symbol, @timeframe, @timestamp, @request_json, @market_state_json, @system_prompt, @ai_response_raw, @decision, @confidence, @order_type, @entry_price, @sl, @tp, @latency_ms)";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = query;
        cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "session_id", Value = sessionId });
        cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "symbol", Value = symbol });
        cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "timeframe", Value = timeframe });
        cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "timestamp", Value = DateTime.UtcNow });
        cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "request_json", Value = requestJson });
        cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "market_state_json", Value = JsonSerializer.Serialize(state) });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "system_prompt", Value = result.Prompt });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "ai_response_raw", Value = result.RawResponse });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "decision", Value = result.Decision.Decision.ToString() });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "confidence", Value = (float)result.Decision.Confidence });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "order_type", Value = result.Decision.SuggestedType?.ToString() ?? string.Empty });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "entry_price", Value = (double)(result.Decision.SuggestedEntry ?? 0m) });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "sl", Value = (double)(result.Decision.SuggestedStopLoss ?? 0m) });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "tp", Value = (double)(result.Decision.SuggestedTakeProfit ?? 0m) });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "latency_ms", Value = (int)result.LatencyMs });

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log decision to ClickHouse");
        }
    }

    public async Task LogOutcomeAsync(Guid sessionId, string outcome, decimal pnlPips, string exitReason)
    {
        if (string.IsNullOrEmpty(_context.ConnectionString)) return;

        try
        {
            await using var connection = _context.CreateConnection();
            await connection.OpenAsync();

            var query = @"
                INSERT INTO ai_trade_outcomes 
                (session_id, outcome, pnl_pips, exit_reason)
                VALUES 
                (@session_id, @outcome, @pnl_pips, @exit_reason)";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = query;
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "session_id", Value = sessionId });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "outcome", Value = outcome });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "pnl_pips", Value = (double)pnlPips });
            cmd.Parameters.Add(new ClickHouseDbParameter { ParameterName = "exit_reason", Value = exitReason });

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log outcome to ClickHouse");
        }
    }
}