using Avalonia.Controls;

namespace Ametrin.Avalonia.Reactor;

public static class GridDefinitionsExtensions
{
    private static readonly PlainProperty<Grid, string> Rows = new((grid, rows) => grid.RowDefinitions = new RowDefinitions(rows), grid => grid.RowDefinitions = []);
    private static readonly PlainProperty<Grid, string> Columns = new((grid, columns) => grid.ColumnDefinitions = new ColumnDefinitions(columns), grid => grid.ColumnDefinitions = []);

    public static ControlElement<T> RowDefinitions<T>(this ControlElement<T> element, string rows) where T : Grid, new() => element.Set(Rows, rows);

    public static ControlElement<T> ColumnDefinitions<T>(this ControlElement<T> element, string columns) where T : Grid, new() => element.Set(Columns, columns);
}
