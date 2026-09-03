using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace AnarlogTrigger.UI;

internal static class FluentUi
{
    public static void ApplyAcrylicHostWindow(Window window)
    {
        try
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            try
            {
                window.SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
                // Backdrop not supported on this OS/build.
            }
        }
    }

    public static void ApplyAcrylicMenuFlyout(MenuFlyout menu, Func<XamlRoot> xamlRootProvider)
    {
        menu.Opened += (_, _) =>
        {
            var xamlRoot = xamlRootProvider();
            if (xamlRoot?.Content is not FrameworkElement root)
            {
                return;
            }

            // Popup presenter is attached after Opened; style on next UI tick.
            root.DispatcherQueue.TryEnqueue(() => ApplyAcrylicToFlyoutPresenter(xamlRoot));
        };
    }

    public static void StyleContentDialog(ContentDialog dialog)
    {
        dialog.Background = CreateSurfaceBrush();
        dialog.CornerRadius = new CornerRadius(8);
    }

    private static void ApplyAcrylicToFlyoutPresenter(XamlRoot xamlRoot)
    {
        if (xamlRoot.Content is not FrameworkElement root)
        {
            return;
        }

        var presenter = FindDescendant<MenuFlyoutPresenter>(root);
        if (presenter is null)
        {
            return;
        }

        presenter.Background = CreateSurfaceBrush();
        presenter.BorderBrush = CreateBorderBrush();
        presenter.BorderThickness = new Thickness(1);
        presenter.CornerRadius = new CornerRadius(8);
        presenter.Padding = new Thickness(4);
    }

    private static T? FindDescendant<T>(DependencyObject node) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match)
            {
                return match;
            }

            var found = FindDescendant<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static AcrylicBrush CreateSurfaceBrush()
    {
        var dark = IsDarkTheme();
        return new AcrylicBrush
        {
            TintColor = dark ? Windows.UI.Color.FromArgb(255, 0, 0, 0) : Windows.UI.Color.FromArgb(255, 255, 255, 255),
            TintOpacity = dark ? 0.55 : 0.65,
            FallbackColor = dark
                ? Windows.UI.Color.FromArgb(245, 32, 32, 32)
                : Windows.UI.Color.FromArgb(245, 243, 243, 243)
        };
    }

    private static SolidColorBrush CreateBorderBrush()
    {
        var dark = IsDarkTheme();
        return new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(48, 255, 255, 255)
            : Windows.UI.Color.FromArgb(32, 0, 0, 0));
    }

    private static bool IsDarkTheme()
    {
        var theme = Application.Current?.RequestedTheme ?? ApplicationTheme.Dark;
        return theme == ApplicationTheme.Dark;
    }
}
