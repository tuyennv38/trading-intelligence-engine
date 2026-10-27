using System;
using System.Globalization;
using System.Text;

namespace TradingIntelligenceEngine.Domain.MarketData;

/// <summary>
/// Thông số hợp đồng từ MT5 — lot/rủi ro phụ thuộc từng cặp (tick value, volume min).
/// </summary>
public class SymbolContractSpec
{
    public int Digits { get; set; }
    public decimal Point { get; set; }
    public decimal TickSize { get; set; }
    public decimal TickValue { get; set; }
    public decimal ContractSize { get; set; }
    public decimal VolumeMin { get; set; }
    public decimal VolumeStep { get; set; }
    public decimal VolumeMax { get; set; }
    public int StopsLevel { get; set; }

    /// <summary>Spread sàn (points, SYMBOL_SPREAD).</summary>
    public int SpreadPoints { get; set; }

    /// <summary>Spread theo giá (Ask − Bid).</summary>
    public decimal Spread { get; set; }

    public bool HasPricing => TickSize > 0 && TickValue > 0;

    public decimal SpreadPrice =>
        Spread > 0
            ? Spread
            : (Point > 0 && SpreadPoints > 0 ? SpreadPoints * Point : 0);

    /// <summary>
    /// Dịch TP sớm về phía entry: tối thiểu 1 giá (1.0 USD cho XAUUSD, hoặc 10 pips cho FX), max(3×spread, offset), không quá 25% khoảng entry→TP.
    /// </summary>
    public decimal TpEarlyOffset(decimal tpDistance = 0, string? symbol = null)
    {
        var tick = TickSize > 0 ? TickSize : (Point > 0 ? Point : 0.01m);
        var point = Point > 0 ? Point : (TickSize > 0 ? TickSize : 0.01m);

        decimal onePriceOffset;
        var sym = (symbol ?? "").Trim().ToUpperInvariant();
        if (sym.Contains("XAU") || sym.Contains("GOLD") || (Digits == 2 && tick == 0.01m))
        {
            onePriceOffset = 1.0m; // 1 giá Vàng = 1.0 USD
        }
        else if (point > 0)
        {
            onePriceOffset = point * 100m; // 100 points = 10 pips trên Forex
        }
        else
        {
            onePriceOffset = 1.0m;
        }

        var v = Math.Max(onePriceOffset, Math.Max(SpreadPrice * 3m, tick * 8m));
        if (tpDistance > 0)
            v = Math.Min(v, tpDistance * 0.25m);

        var digits = Digits > 0 ? Digits : 2;
        return decimal.Round(v, digits);
    }

    public decimal LossUsdPerLot(decimal priceDistance)
    {
        if (!HasPricing || priceDistance <= 0)
            return 0;
        return decimal.Round((priceDistance / TickSize) * TickValue, 2);
    }

    public decimal MinLotRiskUsd(decimal priceDistance)
    {
        var perLot = LossUsdPerLot(priceDistance);
        var vmin = VolumeMin > 0 ? VolumeMin : 1m;
        return decimal.Round(perLot * vmin, 2);
    }

    public decimal MaxLotsForRisk(decimal maxRiskUsd, decimal priceDistance)
    {
        var perLot = LossUsdPerLot(priceDistance);
        if (perLot <= 0 || maxRiskUsd <= 0)
            return 0;

        var lots = maxRiskUsd / perLot;
        var step = VolumeStep > 0 ? VolumeStep : 0.01m;
        lots = Math.Floor(lots / step) * step;
        if (VolumeMax > 0 && lots > VolumeMax)
            lots = VolumeMax;
        if (VolumeMin > 0 && lots < VolumeMin)
            return 0;
        return lots;
    }

    public string ToPromptBlock(string symbol, decimal maxRiskUsd)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\nHỢP ĐỒNG {symbol} — lot phụ thuộc CẶP NÀY (tick_value / contract / volume_min), không copy lot hay khoảng SL từ XAUUSD sang EURUSD/GBP:");
        if (!HasPricing)
        {
            sb.AppendLine("- Chưa có tick_size/tick_value từ EA. Vẫn: KHÔNG gửi lot; SL/TP/zone theo đơn vị giá của đúng cặp này.");
            return sb.ToString();
        }

        sb.AppendLine($"- digits={Digits}, point={Fmt(Point)}, tick_size={Fmt(TickSize)}, tick_value={Fmt(TickValue)} USD mỗi 1 tick trên 1.0 lot, contract_size={Fmt(ContractSize)}");
        sb.AppendLine($"- volume min={Fmt(VolumeMin)} step={Fmt(VolumeStep)} max={Fmt(VolumeMax)}; stops_level={StopsLevel} points");
        sb.AppendLine($"- spread ≈ {Fmt(SpreadPrice)} ({SpreadPoints} points). Sau khi chọn TP, dịch mỗi mức về phía entry tối thiểu 1 giá (≈ {Fmt(TpEarlyOffset(0, symbol))}) thay vì đặt sát cản, giúp lệnh dễ cắn TP.");
        sb.AppendLine("- EA tính: lỗ_1_lot = (khoảng_SL / tick_size) × tick_value; lot = floor(max_risk_usd / lỗ_1_lot) theo volume_step. KHÔNG gửi trường lot.");

        AppendWorkedExample(sb, maxRiskUsd, 100);
        AppendWorkedExample(sb, maxRiskUsd, 500);
        return sb.ToString();
    }

    private void AppendWorkedExample(StringBuilder sb, decimal maxRiskUsd, int ticks)
    {
        var dist = TickSize * ticks;
        var perLot = LossUsdPerLot(dist);
        var minRisk = MinLotRiskUsd(dist);
        var lots = MaxLotsForRisk(maxRiskUsd, dist);
        var lotTxt = lots > 0 ? Fmt(lots) : "0 (min lot đã vượt trần — EA không vào)";
        sb.AppendLine($"- Ví dụ SL cách {Fmt(dist)} ({ticks} tick): lỗ 1.0 lot ≈ {Fmt(perLot)} USD; min lot lỗ ≈ {Fmt(minRisk)} USD → lot tối đa ≈ {lotTxt}.");
    }

    private static string Fmt(decimal value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);
}
