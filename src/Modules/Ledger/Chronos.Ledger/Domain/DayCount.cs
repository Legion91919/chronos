namespace Chronos.Ledger.Domain;

/// <summary>Actual/365 (Fixed): actual calendar days, always divided by 365, also in leap years.</summary>
internal static class DayCount
{
    public const string Actual365 = "Actual/365";

    /// <summary>Days after <paramref name="from"/> up to and including <paramref name="through"/>.</summary>
    public static int ActualDays(DateOnly from, DateOnly through) => through.DayNumber - from.DayNumber;

    /// <summary>Interest for <paramref name="days"/> days, rounded once to the cent, half away from zero.</summary>
    public static decimal Interest(decimal principal, decimal annualRate, int days) =>
        Round(principal * annualRate * days / 365m);

    /// <summary>Commercial rounding to the cent.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
