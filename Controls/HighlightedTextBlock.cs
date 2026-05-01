using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AccountManager.Controls;

public sealed class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty TextValueProperty = DependencyProperty.Register(
        nameof(TextValue),
        typeof(string),
        typeof(HighlightedTextBlock),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query),
        typeof(string),
        typeof(HighlightedTextBlock),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public string TextValue
    {
        get => (string)GetValue(TextValueProperty);
        set => SetValue(TextValueProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((HighlightedTextBlock)d).Rebuild();
    }

    private void Rebuild()
    {
        Inlines.Clear();
        var text = TextValue ?? string.Empty;
        var query = Query ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Inlines.Add(new Run(text));
            return;
        }

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var current = 0;
            while (index >= 0)
            {
                if (index > current) Inlines.Add(new Run(text[current..index]));

                Inlines.Add(new Run(text.Substring(index, query.Length))
                {
                    Background = Brushes.Gold,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.SemiBold
                });

                current = index + query.Length;
                index = text.IndexOf(query, current, StringComparison.OrdinalIgnoreCase);
            }

            if (current < text.Length) Inlines.Add(new Run(text[current..]));
            return;
        }

        if (query.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            Inlines.Add(new Run(text)
            {
                Background = Brushes.LightGreen,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.SemiBold
            });
            return;
        }

        Inlines.Add(new Run(text));
    }
}
