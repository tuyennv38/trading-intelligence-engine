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
using TradingIntelligenceEngine.AI.Providers;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Api.Data;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Signal.Notifications;
using Hangfire;
using Hangfire.MemoryStorage;
using TradingIntelligenceEngine.Api.Jobs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddMemoryCache(); // Register In-Memory Cache

// Register Hangfire (Memory Storage for Dev/Testing)
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseMemoryStorage());
builder.Services.AddHangfireServer();

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

var aiProviderOptions = new AiProviderOptions();
builder.Configuration.GetSection("AiProvider").Bind(aiProviderOptions);
builder.Services.AddSingleton(aiProviderOptions);

// 2. Register domain & application services
builder.Services.AddTransient<ISwingDetector, SwingDetector>();
builder.Services.AddTransient<IStructureAnalyzer, StructureAnalyzer>();
builder.Services.AddTransient<IStructureEventDetector, StructureEventDetector>();
builder.Services.AddTransient<ITechnicalAnalyzer, TechnicalAnalyzer>();
builder.Services.AddTransient<ILiquidityAnalyzer, LiquidityAnalyzer>();
builder.Services.AddTransient<ISupportResistanceAnalyzer, SupportResistanceAnalyzer>();
builder.Services.AddTransient<IRegimeAnalyzer, RegimeAnalyzer>();

builder.Services.AddSingleton<IMarketDataStore, InMemoryMarketDataStore>(); // Register Data Store
builder.Services.AddTransient<IMarketAnalyzer, MarketAnalyzerService>();
builder.Services.AddTransient<IAiNotificationJob, AiNotificationJob>(); // Register Hangfire Job

builder.Services.AddTransient<ITradingStrategy, TrendFollowingPullbackStrategy>();
builder.Services.AddTransient<IStrategyEngine, StrategyEngine>();

builder.Services.AddTransient<IBacktestEngine, BacktestEngine>();

builder.Services.AddHttpClient<IAiDecisionEngine, LlmDecisionEngine>();
builder.Services.AddHttpClient<ITradingStrategistEngine, LlmTradingStrategistEngine>();

// 3. Register Notification Service
var notificationProvider = builder.Configuration["Notification:Provider"];
if (notificationProvider == "GoogleChat")
{
    builder.Services.AddHttpClient<INotificationService, GoogleChatNotificationService>();
}

builder.Services.AddSingleton<IClickhouseContext, ClickhouseContext>();
builder.Services.AddSingleton<TradingIntelligenceEngine.Api.Services.IClickhouseLogger, TradingIntelligenceEngine.Api.Services.ClickhouseLogger>();

var app = builder.Build();

// Kích hoạt IClickhouseLogger ngay khi khởi động app để tạo bảng lập tức
using (var scope = app.Services.CreateScope())
{
    var clickhouseLogger = scope.ServiceProvider.GetRequiredService<TradingIntelligenceEngine.Api.Services.IClickhouseLogger>();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "TradingIntelligenceEngine.Api v1");
});

app.UseHangfireDashboard(); // Kích hoạt UI Dashboard theo dõi Background Job

app.UseAuthorization();
app.MapControllers();

app.Run();