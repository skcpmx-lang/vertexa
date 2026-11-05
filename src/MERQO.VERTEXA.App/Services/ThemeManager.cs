using System.Windows;
using Merqo.Vertexa.Core.Settings;

namespace Merqo.Vertexa.App.Services;

/// <summary>Swaps the design-token dictionary at runtime; views use DynamicResource only.</summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var application = Application.Current;
        if (application is null) return;

        var source = theme == AppTheme.Light ? "Themes/Tokens.Light.xaml" : "Themes/Tokens.Dark.xaml";
        var uri = new Uri(source, UriKind.Relative);
        var dictionaries = application.Resources.MergedDictionaries;

        ResourceDictionary? tokenDictionary = null;
        foreach (var dictionary in dictionaries)
        {
            var candidate = dictionary.Source?.OriginalString ?? string.Empty;
            if (candidate.Contains("Tokens.", StringComparison.OrdinalIgnoreCase))
            {
                tokenDictionary = dictionary;
                break;
            }
        }

        if (tokenDictionary is null)
        {
            dictionaries.Insert(0, new ResourceDictionary { Source = uri });
            return;
        }

        if (tokenDictionary.Source?.OriginalString == source) return;
        var index = dictionaries.IndexOf(tokenDictionary);
        dictionaries.RemoveAt(index);
        dictionaries.Insert(index, new ResourceDictionary { Source = uri });
    }
}
