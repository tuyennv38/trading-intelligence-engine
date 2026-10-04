using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.MarketStructure.Structure;

public class StructureAnalyzer : IStructureAnalyzer
{
    public IReadOnlyList<StructurePoint> Analyze(IReadOnlyList<SwingPoint> swings)
    {
        var validSwings = swings.Where(s => s.Status != SwingStatus.Invalidated).ToList();
        var structurePoints = new List<StructurePoint>();

        SwingPoint? lastHigh = null;
        SwingPoint? lastLow = null;

        foreach (var swing in validSwings)
        {
            StructureLabel label;

            if (swing.Type == SwingType.High)
            {
                if (lastHigh == null)
                {
                    // First high, default to HH for lack of reference, or we could leave it unlabelled if we supported it.
                    // We'll use HH as default first high.
                    label = StructureLabel.HH;
                }
                else
                {
                    label = swing.Price > lastHigh.Price ? StructureLabel.HH : StructureLabel.LH;
                }
                lastHigh = swing;
            }
            else
            {
                if (lastLow == null)
                {
                    label = StructureLabel.LL;
                }
                else
                {
                    label = swing.Price > lastLow.Price ? StructureLabel.HL : StructureLabel.LL;
                }
                lastLow = swing;
            }

            structurePoints.Add(new StructurePoint(swing, label));
        }

        return structurePoints;
    }
}