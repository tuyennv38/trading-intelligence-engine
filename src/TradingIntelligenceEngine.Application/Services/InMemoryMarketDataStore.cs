using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;

namespace TradingIntelligenceEngine.Application.Services;

public class InMemoryMarketDataStore : IMarketDataStore
{
    private readonly IMemoryCache _cache;
    private static readonly object _lock = new object();

    public InMemoryMarketDataStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    private string GetCacheKey(string symbol, string timeframe) => $"MarketData_{symbol}_{timeframe}";

    public Task InitializeCandlesAsync(string symbol, string timeframe, List<RawCandleDto> candles)
    {
        var key = GetCacheKey(symbol, timeframe);
        
        lock (_lock)
        {
            _cache.Set(key, new List<RawCandleDto>(candles));
        }
        
        return Task.CompletedTask;
    }

    public Task AppendCandleAsync(string symbol, string timeframe, RawCandleDto newCandle, int maxCapacity = 1000)
    {
        var key = GetCacheKey(symbol, timeframe);

        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out List<RawCandleDto>? currentCandles) || currentCandles == null)
            {
                currentCandles = new List<RawCandleDto>();
            }

            // Xử lý nến: Kiểm tra xem nến mới này là nến tiếp theo (Time mới) hay là update giá cho nến hiện tại
            var lastCandle = currentCandles.LastOrDefault();
            if (lastCandle != null && lastCandle.Time == newCandle.Time)
            {
                // Update nến hiện hành (chưa đóng)
                currentCandles[currentCandles.Count - 1] = newCandle;
            }
            else
            {
                // Thêm nến mới vào cuối
                currentCandles.Add(newCandle);
                
                // Cắt bỏ nến cũ nhất nếu vượt quá Capacity (Ví dụ: > 1000 nến)
                if (currentCandles.Count > maxCapacity)
                {
                    currentCandles.RemoveAt(0);
                }
            }

            _cache.Set(key, currentCandles);
        }

        return Task.CompletedTask;
    }

    public Task<List<RawCandleDto>> GetCandlesAsync(string symbol, string timeframe)
    {
        var key = GetCacheKey(symbol, timeframe);
        
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out List<RawCandleDto>? currentCandles) && currentCandles != null)
            {
                return Task.FromResult(new List<RawCandleDto>(currentCandles));
            }
        }
        
        return Task.FromResult(new List<RawCandleDto>());
    }

    public Task SetLatestStateAsync(string symbol, Dictionary<string, Domain.MarketState.MarketState> state)
    {
        var key = $"MarketState_{symbol}";
        lock (_lock) { _cache.Set(key, state); }
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, Domain.MarketState.MarketState>?> GetLatestStateAsync(string symbol)
    {
        var key = $"MarketState_{symbol}";
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out Dictionary<string, Domain.MarketState.MarketState>? state))
                return Task.FromResult(state);
            return Task.FromResult<Dictionary<string, Domain.MarketState.MarketState>?>(null);
        }
    }

    public Task SetLatestPlanAsync(string symbol, Domain.AI.TradingPlanResponse plan)
    {
        var key = $"TradingPlan_{symbol}";
        lock (_lock) { _cache.Set(key, plan); }
        return Task.CompletedTask;
    }

    public Task<Domain.AI.TradingPlanResponse?> GetLatestPlanAsync(string symbol)
    {
        var key = $"TradingPlan_{symbol}";
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out Domain.AI.TradingPlanResponse? plan))
                return Task.FromResult(plan);
            return Task.FromResult<Domain.AI.TradingPlanResponse?>(null);
        }
    }
}