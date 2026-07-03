using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BadmintonClub.Helpers;

/// <summary>Petites animations réutilisables pour l'interface.</summary>
public static class Animations
{
    private static readonly Duration Fast = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Fait apparaître un élément avec un léger glissement vers le haut (transition de page).</summary>
    public static void ContentIn(UIElement element)
    {
        var transform = new TranslateTransform(0, 16);
        element.RenderTransform = transform;
        element.Opacity = 0;

        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, Fast) { EasingFunction = Ease });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, Fast) { EasingFunction = Ease });
    }

    /// <summary>Affiche une barre avec fondu + glissement (barre d'actions groupées).</summary>
    public static void SlideDownIn(FrameworkElement element)
    {
        if (element.Visibility == Visibility.Visible)
            return;

        element.Visibility = Visibility.Visible;
        var transform = new TranslateTransform(0, -12);
        element.RenderTransform = transform;
        element.Opacity = 0;

        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-12, 0, Fast) { EasingFunction = Ease });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, Fast) { EasingFunction = Ease });
    }

    /// <summary>Masque une barre avec un fondu, puis la replie.</summary>
    public static void FadeOutCollapse(FrameworkElement element)
    {
        if (element.Visibility != Visibility.Visible)
            return;

        var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(160)));
        anim.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }
}
