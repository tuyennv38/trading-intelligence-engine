namespace TradingIntelligenceEngine.Domain.MarketStructure;

public enum StructureLabel
{
    HH, // Higher High
    HL, // Higher Low
    LH, // Lower High
    LL  // Lower Low
}

public sealed record StructurePoint(
    SwingPoint Swing,
    StructureLabel Label
);