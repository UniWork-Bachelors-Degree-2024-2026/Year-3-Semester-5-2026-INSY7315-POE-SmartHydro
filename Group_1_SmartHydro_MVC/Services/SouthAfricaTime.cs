using System;
namespace Group_1_SmartHydro_MVC.Services;
public static class SouthAfricaTime
{
    private static readonly Lazy<TimeZoneInfo> LazySast = new(() => TimeZoneSource());
    private static TimeZoneInfo TimeZoneSource()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time"); }
        catch (TimeZoneNotFoundException)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"); }
            catch (TimeZoneNotFoundException)
            { return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "South Africa Standard Time", "SAST"); }
        }
    }
    public static TimeZoneInfo Zone => LazySast.Value;
    public static DateTime ToSast(DateTime utcTime)
    {
        if (utcTime == DateTime.MinValue || utcTime == DateTime.MaxValue) return utcTime;
        var utc = utcTime.Kind switch
        {
            DateTimeKind.Utc => utcTime,
            DateTimeKind.Local => utcTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcTime, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }
    public static DateTime? ToSast(DateTime? utcTime)
    {
        if (!utcTime.HasValue) return null;
        return ToSast(utcTime.Value);
    }
}