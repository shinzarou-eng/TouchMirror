using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using TouchMirror.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application();

        using (var fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Dark.xaml")))
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(fs));

        app.Resources["FsMeta"] = 12.0;
        app.Resources["RadiusControl"] = new CornerRadius(7);
        app.Resources["OnDarkTextBrush"] = new SolidColorBrush(Color.FromRgb(0xE9, 0xEA, 0xED));
        app.Resources["OnDarkTextDimBrush"] = new SolidColorBrush(Color.FromRgb(0x9A, 0xA1, 0xAC));
        app.Resources["AppModalSolidBrush"] = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x13, 0x19));

        const string img = "https://api.dofusdb.fr/img/monsters/";
        var items = new List<(string Name, int Lvl, bool Done, string? Img)>
        {
            ("Aboudbra le Porteur", 35, false, img + "86.png"),
            ("Abrakadnuzar", 40, true, img + "345.png"),
            ("Abrakroc l'édenté", 38, false, img + "13.png"),
            ("Ameur la Laide", 35, false, img + "85.png"),
            ("Arabord la Cruche", 40, false, img + "149.png"),
            ("Bandapar l'Exclu", 22, false, img + "255.png"),
            ("Bandson le Tonitruant", 37, false, img + "661.png"),
            ("Barchwork le Multicolore", 37, false, img + "29.png"),
            ("Bi le Partageur", 24, false, img + "168.png")
        };

        var widget = new GraphWidget();
        widget.Configure("archis", "#E2843A", true);

        void Render()
        {
            var lines = new List<OverlayLine>
            {
                new("Archis niv. 21-40 · 1/4", "h", null, null, "3/16", null),
                new("", "bar", null, null, null, 3.0 / 16)
            };
            foreach (var it in items)
                lines.Add(new(it.Name, null, null, it.Done, "niv " + it.Lvl, null, it.Img));
            lines.Add(new("‹ 1/2", "nav", null, null, null, null));
            lines.Add(new("suite ›", "nav", null, null, null, null));
            lines.Add(new("‹ Archis niv. 1-20", "nav", null, null, null, null));
            lines.Add(new("Archis niv. 21-40 · 2/4 ›", "nav", null, null, null, null));
            lines.Add(new("toutes les zones", "nav", null, null, null, null));
            lines.Add(new("12/207 cochés", "bar", null, null, null, 12.0 / 207));
            lines.Add(new("tout décocher", "nav", "#E08A7A", null, null, null));
            widget.SetLines(lines);
        }

        Render();
        widget.LineClicked += idx =>
        {
            if (idx >= 2 && idx <= items.Count + 1)
            {
                var it = items[idx - 2];
                items[idx - 2] = (it.Name, it.Lvl, !it.Done, it.Img);
                Render();
            }
        };

        var canvas = new Canvas();
        canvas.Children.Add(widget);
        Canvas.SetTop(widget, 40);
        Canvas.SetRight(widget, 24);

        var win = new Window
        {
            Title = "OverlayPreview",
            Width = 340, Height = 560,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x0E, 0x0A)),
            Content = canvas,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        app.Run(win);
    }
}
