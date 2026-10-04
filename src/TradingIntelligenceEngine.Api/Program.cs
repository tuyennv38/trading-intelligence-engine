using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TradingIntelligenceEngine.Application.Services;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.MarketStructure;
using TradingIntelligenceEngine.Domain.Signal;
using TradingIntelligenceEngine.Liquidity;
using TradingIntelligenceEngine.MarketStructure.Events;
using TradingIntelligenceEngine.MarketStructure.Structure;
using TradingIntelligenceEngine.MarketStructure.Swings;
using TradingIntelligenceEngine.Regime;
using TradingIntelligenceEngine.Signal;
using TradingIntelligenceEngine.Signal.Strategies;
using TradingIntelligenceEngine.TechnicalAnalysis;
using TradingIntelligenceEngine.Domain.Backtesting;
using TradingIntelligenceEngine.Backtesting.Simulation;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo 
    { 
        Title = "TradingIntelligenceEngine.Api", 
        Version = "v1" 
    });
});

// 1. Configuration bindings
var swingOptions = new SwingOptions();
builder.Configuration.GetSection("MarketStructure:Swing").Bind(swingOptions);
builder.Services.AddSingleton(swingOptions);

var breakOptions = new BreakOptions();
builder.Configuration.GetSection("MarketStructure:Break").Bind(breakOptions);
builder.Services.AddSingleton(breakOptions);

var liquidityOptions = new LiquidityOptions();
builder.Configuration.GetSection("MarketStructure:Liquidity").Bind(liquidityOptions);
builder.Services.AddSingleton(liquidityOptions);

var indicatorOptions = new IndicatorOptions();
builder.Configuration.GetSection("Indicators").Bind(indicatorOptions);
builder.Services.AddSingleton(indicatorOptions);

// 2. Register domain & application services
builder.Services.AddTransient<ISwingDetector, SwingDetector>();
builder.Services.AddTransient<IStructureAnalyzer, StructureAnalyzer>();
builder.Services.AddTransient<IStructureEventDetector, StructureEventDetector>();
builder.Services.AddTransient<ITechnicalAnalyzer, TechnicalAnalyzer>();
builder.Services.AddTransient<ILiquidityAnalyzer, LiquidityAnalyzer>();
builder.Services.AddTransient<ISupportResistanceAnalyzer, SupportResistanceAnalyzer>();
builder.Services.AddTransient<IRegimeAnalyzer, RegimeAnalyzer>();

builder.Services.AddTransient<IMarketAnalyzer, MarketAnalyzerService>();

builder.Services.AddTransient<ITradingStrategy, TrendFollowingPullbackStrategy>();
builder.Services.AddTransient<IStrategyEngine, StrategyEngine>();

builder.Services.AddTransient<IBacktestEngine, BacktestEngine>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "TradingIntelligenceEngine.Api v1");
});

app.UseAuthorization();
app.MapControllers();

app.Run();