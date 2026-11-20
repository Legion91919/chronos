using Chronos.Ledger.Domain;
using Xunit;

namespace Chronos.Ledger.Tests;

public class DayCountTests
{
    [Theory]
    [InlineData("2026-01-01", "2026-01-31", 30)]   // from exclusive, through inclusive
    [InlineData("2028-01-31", "2028-02-29", 29)]   // leap February counts its real days
    [InlineData("2027-12-31", "2028-12-31", 366)]  // a leap year has 366 actual days
    public void Actual365_counts_actual_calendar_days(string from, string through, int expected) =>
        Assert.Equal(expected, DayCount.ActualDays(DateOnly.Parse(from), DateOnly.Parse(through)));

    [Fact]
    public void Actual365_divides_by_365_even_in_a_leap_year()
    {
        // 100,000 at 7.30% for the 366 days of 2028 = 7,320.00 (not 7,300.00 as Actual/Actual would give)
        Assert.Equal(7320.00m, DayCount.Interest(100_000m, 0.073m, 366));
    }

    [Theory]
    [InlineData(100000, 0.073, 30, 600.00)]
    [InlineData(100000, 0.073, 29, 580.00)]
    [InlineData(800000, 0.0725, 19, 3019.18)]      // 3019.178... rounds half away from zero to the cent
    [InlineData(1, 0.05, 1, 0.00)]
    public void Interest_is_principal_times_rate_times_days_over_365_rounded_to_the_cent(double principal, double rate, int days, double expected) =>
        Assert.Equal((decimal)expected, DayCount.Interest((decimal)principal, (decimal)rate, days));

    [Fact]
    public void Midpoint_rounds_away_from_zero()
    {
        // 365 * 0.005 / 365 = 0.005 exactly -> 0.01 (commercial rounding), banker's would give 0.00
        Assert.Equal(0.01m, DayCount.Interest(365m, 0.005m, 1));
    }
}
