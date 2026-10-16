using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WinGuard.Localization;
using WinGuard.Models;

namespace WinGuard.Converters;

public sealed class RecommendationToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        Recommendation.Recommended => new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
        Recommendation.LimitedRecommended => new SolidColorBrush(Color.FromRgb(0xF5, 0xB0, 0x41)),
        _ => new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x6C))
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class RecommendationToTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        Recommendation.Recommended => Loc.T("Recommended"),
        Recommendation.LimitedRecommended => Loc.T("Limited"),
        _ => Loc.T("NotRecommended")
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is Visibility.Visible ^ Invert;
}

public sealed class BoolToStateTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Loc.T(value is true ? "On" : "Off");
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>For RadioButton groups: IsChecked = (value == parameter).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Equals(value?.ToString(), p?.ToString());
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is true ? p : Binding.DoNothing;
}

/// <summary>Maps an internal (English) category key to its display name. Pass the language as 2nd binding to refresh live.</summary>
public sealed class CategoryNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) => values.Length > 0 && values[0] is string s ? Strings.Category(s) : "";
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Formats the "· N settings" group suffix in the current language.</summary>
public sealed class SettingsCountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) => Loc.F("SettingsCount", values.Length > 0 ? values[0] : 0);
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length == 2 && values[0] is double pct && values[1] is double w) return Math.Max(0, w * pct / 100.0);
        return 0.0;
    }
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Converts a 0-100 percentage into an arc geometry for the circular privacy gauge. Parameter = diameter.</summary>
public sealed class PercentToArcConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        double pct = Math.Clamp(System.Convert.ToDouble(value, CultureInfo.InvariantCulture), 0, 100);
        double size = double.Parse(p?.ToString() ?? "120", CultureInfo.InvariantCulture);
        double stroke = 10, r = (size - stroke) / 2, cx = size / 2, cy = size / 2;
        if (pct <= 0) return Geometry.Empty;
        if (pct >= 99.999) return new EllipseGeometry(new Point(cx, cy), r, r);
        double angle = pct / 100 * 2 * Math.PI;
        var start = new Point(cx, cy - r);
        var end = new Point(cx + r * Math.Sin(angle), cy - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, pct > 50, SweepDirection.Clockwise, true));
        var g = new PathGeometry(); g.Figures.Add(fig); g.Freeze();
        return g;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
