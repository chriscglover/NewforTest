// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NewforTest.Core;

namespace NewforTest.Gui.Controls;

/// <summary>
/// Draws a <see cref="TeletextScreen"/> the way a subtitle decoder overlays it on video:
/// boxed cells get their background colour, unboxed text is drawn straight on the picture,
/// double-height characters span two rows. The picture is a plain grey, like the test
/// receiver, so the boxes are easy to see.
/// </summary>
public sealed class TeletextView : Control
{
    public static readonly StyledProperty<TeletextScreen?> ScreenProperty =
        AvaloniaProperty.Register<TeletextView, TeletextScreen?>(nameof(Screen));

    public static readonly StyledProperty<bool> ShowRowNumbersProperty =
        AvaloniaProperty.Register<TeletextView, bool>(nameof(ShowRowNumbers), true);

    private static readonly IBrush[] Palette =
    {
        Brushes.Black, Brushes.Red, Brushes.Lime, Brushes.Yellow,
        Brushes.Blue, Brushes.Magenta, Brushes.Cyan, Brushes.White,
    };

    private static readonly IBrush PictureBrush = new SolidColorBrush(Color.Parse("#7A7A7A"));
    private static readonly IBrush SafeAreaBrush = new SolidColorBrush(Color.Parse("#30FFFFFF"));
    private static readonly IBrush RowNumberBrush = new SolidColorBrush(Color.Parse("#B0FFFFFF"));
    private static readonly Typeface Mono = new(new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"));

    static TeletextView()
    {
        AffectsRender<TeletextView>(ScreenProperty, ShowRowNumbersProperty);
    }

    public TeletextScreen? Screen
    {
        get => GetValue(ScreenProperty);
        set => SetValue(ScreenProperty, value);
    }

    /// <summary>Show teletext row numbers down the left edge.</summary>
    public bool ShowRowNumbers
    {
        get => GetValue(ShowRowNumbersProperty);
        set => SetValue(ShowRowNumbersProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScreenProperty)
        {
            if (change.OldValue is TeletextScreen old)
                old.Changed -= InvalidateVisual;
            if (change.NewValue is TeletextScreen screen)
                screen.Changed += InvalidateVisual;
        }
    }

    public override void Render(DrawingContext context)
    {
        // 16:9 picture, as large as fits
        var bounds = Bounds;
        double pictureWidth = Math.Min(bounds.Width, bounds.Height * 16 / 9);
        double pictureHeight = pictureWidth * 9 / 16;
        var picture = new Rect((bounds.Width - pictureWidth) / 2, (bounds.Height - pictureHeight) / 2, pictureWidth, pictureHeight);
        context.FillRectangle(PictureBrush, picture);

        // Teletext uses roughly the 4:3 centre of the picture, inside the safe area
        double gridHeight = pictureHeight * 0.9;
        double gridWidth = gridHeight * 4 / 3 * 0.92;
        var grid = new Rect(picture.Center.X - gridWidth / 2, picture.Center.Y - gridHeight / 2, gridWidth, gridHeight);
        double cellWidth = gridWidth / TeletextScreen.Columns;
        double cellHeight = gridHeight / TeletextScreen.Rows;

        context.DrawRectangle(null, new Pen(SafeAreaBrush, 1), grid);

        if (ShowRowNumbers)
        {
            for (int r = 1; r < TeletextScreen.Rows - 1; r++)
            {
                var number = Text(r.ToString(), cellHeight * 0.55, RowNumberBrush);
                context.DrawText(number, new Point(grid.Left - number.Width - cellWidth * 0.4, grid.Top + r * cellHeight + (cellHeight - number.Height) / 2));
            }
        }

        if (Screen is null)
            return;

        var cells = Screen.Render();
        Rect CellRect(int r, int c) => new(grid.Left + c * cellWidth, grid.Top + r * cellHeight, cellWidth, cellHeight);

        // Backgrounds first, so a double-height glyph isn't painted over by the row below.
        // Subtitle decoders only show the background inside boxes.
        for (int r = 0; r < TeletextScreen.Rows; r++)
        {
            for (int c = 0; c < TeletextScreen.Columns; c++)
            {
                if (cells[r][c].Boxed)
                    context.FillRectangle(Palette[cells[r][c].Background], CellRect(r, c).Inflate(new Thickness(0.5, 0.5))); // overlap to avoid hairline gaps
            }
        }

        for (int r = 0; r < TeletextScreen.Rows; r++)
        {
            for (int c = 0; c < TeletextScreen.Columns; c++)
            {
                var cell = cells[r][c];
                var rect = CellRect(r, c);

                if (cell.Char == ' ' || cell.Height == CellHeight.DoubleBottom)
                    continue;

                bool tall = cell.Height == CellHeight.DoubleTop;
                var glyph = Text(cell.Char.ToString(), cellHeight * 0.95, Palette[cell.Foreground]);
                double x = rect.Left + (cellWidth - glyph.Width) / 2;
                double y = rect.Top + (cellHeight - glyph.Height) / 2;

                if (!tall)
                {
                    context.DrawText(glyph, new Point(x, y));
                    continue;
                }

                // Double height: stretch the glyph over this row and the next
                var transform = Matrix.CreateScale(1, 2) * Matrix.CreateTranslation(x, rect.Top - (glyph.Height - cellHeight) );
                using (context.PushTransform(transform))
                    context.DrawText(glyph, new Point(0, 0));
            }
        }

        if (Screen.Page is { } page)
        {
            var label = Text($"P{page}", cellHeight * 0.6, RowNumberBrush);
            context.DrawText(label, new Point(grid.Left, grid.Top + (cellHeight - label.Height) / 2));
        }
    }

    private static FormattedText Text(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, Math.Max(size, 1), brush);
}
