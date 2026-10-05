# GitHub Actions: context = repo root, file = src/TradingIntelligenceEngine.Api/Dockerfile

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files only (layer cache for restore)
COPY ["src/TradingIntelligenceEngine.Api/TradingIntelligenceEngine.Api.csproj", "src/TradingIntelligenceEngine.Api/"]
COPY ["src/TradingIntelligenceEngine.AI/TradingIntelligenceEngine.AI.csproj", "src/TradingIntelligenceEngine.AI/"]
COPY ["src/TradingIntelligenceEngine.Application/TradingIntelligenceEngine.Application.csproj", "src/TradingIntelligenceEngine.Application/"]
COPY ["src/TradingIntelligenceEngine.Backtesting/TradingIntelligenceEngine.Backtesting.csproj", "src/TradingIntelligenceEngine.Backtesting/"]
COPY ["src/TradingIntelligenceEngine.Domain/TradingIntelligenceEngine.Domain.csproj", "src/TradingIntelligenceEngine.Domain/"]
COPY ["src/TradingIntelligenceEngine.Execution/TradingIntelligenceEngine.Execution.csproj", "src/TradingIntelligenceEngine.Execution/"]
COPY ["src/TradingIntelligenceEngine.Infrastructure/TradingIntelligenceEngine.Infrastructure.csproj", "src/TradingIntelligenceEngine.Infrastructure/"]
COPY ["src/TradingIntelligenceEngine.Liquidity/TradingIntelligenceEngine.Liquidity.csproj", "src/TradingIntelligenceEngine.Liquidity/"]
COPY ["src/TradingIntelligenceEngine.MarketData/TradingIntelligenceEngine.MarketData.csproj", "src/TradingIntelligenceEngine.MarketData/"]
COPY ["src/TradingIntelligenceEngine.MarketStructure/TradingIntelligenceEngine.MarketStructure.csproj", "src/TradingIntelligenceEngine.MarketStructure/"]
COPY ["src/TradingIntelligenceEngine.Regime/TradingIntelligenceEngine.Regime.csproj", "src/TradingIntelligenceEngine.Regime/"]
COPY ["src/TradingIntelligenceEngine.Risk/TradingIntelligenceEngine.Risk.csproj", "src/TradingIntelligenceEngine.Risk/"]
COPY ["src/TradingIntelligenceEngine.Signal/TradingIntelligenceEngine.Signal.csproj", "src/TradingIntelligenceEngine.Signal/"]
COPY ["src/TradingIntelligenceEngine.TechnicalAnalysis/TradingIntelligenceEngine.TechnicalAnalysis.csproj", "src/TradingIntelligenceEngine.TechnicalAnalysis/"]

RUN dotnet restore "src/TradingIntelligenceEngine.Api/TradingIntelligenceEngine.Api.csproj"

# Copy the remaining source code
COPY ["src/", "src/"]

WORKDIR "/src/src/TradingIntelligenceEngine.Api"
RUN dotnet build "TradingIntelligenceEngine.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "TradingIntelligenceEngine.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TradingIntelligenceEngine.Api.dll"]
