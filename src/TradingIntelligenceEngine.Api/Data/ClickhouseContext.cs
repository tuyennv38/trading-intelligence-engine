using System;
using Microsoft.Extensions.Configuration;
using ClickHouse.Client.ADO;

namespace TradingIntelligenceEngine.Api.Data;

public interface IClickhouseContext
{
    ClickHouseConnection CreateConnection();
    string ConnectionString { get; }
}

public class ClickhouseContext : IClickhouseContext
{
    public string ConnectionString { get; }

    public ClickhouseContext(IConfiguration configuration)
    {
        // Ưu tiên đọc từ Environment Variable (chuẩn của cscmobi-gsm-dashboard), sau đó fallback về appsettings.json
        ConnectionString = Environment.GetEnvironmentVariable("CLICKHOUSE_LOG_URI") 
            ?? configuration.GetConnectionString("ClickHouse") 
            ?? string.Empty;
    }

    public ClickHouseConnection CreateConnection()
    {
        if (string.IsNullOrEmpty(ConnectionString))
        {
            throw new InvalidOperationException("ClickHouse connection string is not configured.");
        }

        var csBuilder = new ClickHouseConnectionStringBuilder(ConnectionString);
        
        // Bạn có thể thiết lập các cấu hình mặc định (ví dụ timeout) tại đây
        // csBuilder.CommandTimeout = 120;
        
        return new ClickHouseConnection(csBuilder.ConnectionString);
    }
}
