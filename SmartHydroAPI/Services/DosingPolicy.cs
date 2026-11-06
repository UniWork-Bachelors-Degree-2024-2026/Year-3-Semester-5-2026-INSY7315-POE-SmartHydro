namespace SmartHydroAPI.Services;

public record DosePlan(string Pump, string Source, decimal Value, decimal Low, decimal High,
    decimal Target, decimal EstimatedMl, int RuntimeMs, int Prediction);

public static class DosingPolicy
{
    public static DosePlan? Plan(decimal value, decimal low, decimal high, int prediction, bool ph)
    {
        if (low < 0 || low >= high || value < 0 || value > (ph ? 14m : 2m) || (!ph && value >= 2m)) return null;
        var target = (low + high) / 2m;
        var error = value - target;
        if (Math.Abs(error) <= (ph ? 0.10m : 0.05m)) return null;
        bool outside = value < low || value > high;
        bool ml = prediction == 1 && value < low + 0.10m && error < 0 || prediction == 0 && value > high - 0.10m && error > 0;
        if (!outside && !ml) return null;
        var scaled = Math.Clamp(ph ? (Math.Abs(error) - 0.10m) / 0.90m : Math.Abs(error) / Math.Max(high - low, 0.10m), 0m, 1m);
        var volume = Math.Round(ph ? 0.5m + 1.5m * scaled : 1m + 4m * scaled, 2, MidpointRounding.AwayFromZero);
        return new(ph ? error < 0 ? "PH_UP" : "PH_DOWN" : error < 0 ? "NUTRIENT_UP" : "DILUTION_DOWN",
            outside ? "Threshold Auto-Dosing" : "ML Recommendation", value, low, high, target, volume,
            (int)Math.Round(volume * 800m, MidpointRounding.AwayFromZero), prediction);
    }
}
