namespace TradingIntelligenceEngine.Api.Models;

public record AiFeedbackRequestDto(
    System.Guid SessionId,
    string Outcome, // "WIN", "LOSS", "BREAKEVEN"
    decimal PnlPips,
    string ExitReason
);