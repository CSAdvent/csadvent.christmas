using System.Globalization;

namespace CSAdvent.Web.Retrospectives;

/// <summary>A column in a <c>ColumnChart</c>.</summary>
/// <param name="Label">The axis label, such as "5" or "2024".</param>
/// <param name="Name">The column's full name, for its tooltip and the table: "December 5".</param>
/// <param name="Values">One value per series, bottom first.</param>
/// <param name="ShowLabel">Whether the axis shows its label (on a crowded axis, only some do).</param>
/// <param name="Highlight">The column the chart is about, in the full color while the rest are lighter.</param>
/// <param name="ValueLabel">Whether its total is written on its cap. Charts label one or two, never every column.</param>
public sealed record ChartColumn(string Label, string Name, IReadOnlyList<int> Values, bool ShowLabel = true, bool Highlight = false, bool ValueLabel = false)
{
    public int Total => Values.Sum();
}

/// <summary>A row in a <c>BarList</c>.</summary>
public sealed record BarListItem(string Label, int Value);

/// <summary>Axis scales for the retrospective's charts.</summary>
public static class ChartScale
{
    /// <summary>Round ticks from 0 up to at least <paramref name="max"/>, about <paramref name="target"/> steps apart: 0, 20, 40, 60.</summary>
    public static IReadOnlyList<int> Ticks(int max, int target = 4)
    {
        if (max <= 0)
        {
            return [0, 1];
        }

        var rough = (double)max / target;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double[] multiples = magnitude >= 10 ? [1, 2, 2.5, 5, 10] : [1, 2, 5, 10];
        var step = Math.Max(1, (int)Math.Ceiling(multiples.Select(m => m * magnitude).First(s => s >= rough)));
        var top = (int)Math.Ceiling((double)max / step) * step;
        return [.. Enumerable.Range(0, (top / step) + 1).Select(i => i * step)];
    }

    /// <summary>An axis tick: 0, 250, 1K, 1.5K, 12K.</summary>
    public static string Compact(int value) =>
        value >= 1000 ? (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A CSS percentage of <paramref name="top"/>, such as <c>42.5%</c>.</summary>
    public static string Percent(int value, int top) =>
        (top <= 0 ? 0 : value * 100.0 / top).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>A fraction of <paramref name="top"/> for <c>calc()</c>, such as <c>0.425</c>.</summary>
    public static string Fraction(int value, int top) =>
        (top <= 0 ? 0 : (double)value / top).ToString("0.####", CultureInfo.InvariantCulture);
}
