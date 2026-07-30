using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace BoardView.App.Controls;

/// <summary>
/// Detecta cambios de DPI en el monitor que contiene el visor y solicita a
/// WPF/WebView2 que actualicen su superficie visual sin aplicar una segunda
/// ampliación sobre el documento PDF.
/// </summary>
internal sealed class WebView2DisplayScaleSynchronizer
{
    private readonly WebView2 browser;
    private readonly FrameworkElement visualOwner;
    private double appliedScale = -1D;

    /// <summary>
    /// Inicializa el observador de escala para el WebView2 indicado.
    /// </summary>
    public WebView2DisplayScaleSynchronizer(WebView2 browser, FrameworkElement visualOwner)
    {
        this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
        this.visualOwner = visualOwner ?? throw new ArgumentNullException(nameof(visualOwner));
    }

    /// <summary>
    /// Comprueba el DPI efectivo y fuerza una actualización visual únicamente
    /// cuando la escala del monitor cambia.
    ///
    /// La clase WPF WebView2 no expone CoreWebView2Controller públicamente.
    /// Por eso no se intenta modificar RasterizationScale directamente; el
    /// control WPF administra internamente esa escala.
    /// </summary>
    public void Synchronize()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(visualOwner);
        double requestedScale = Math.Max(1D, dpi.DpiScaleX);

        if (Math.Abs(requestedScale - appliedScale) < 0.001D)
        {
            return;
        }

        // Evita una ampliación adicional del navegador. El visor PDF interno
        // conserva sus propios controles de zoom de forma independiente.
        if (Math.Abs(browser.ZoomFactor - 1D) > 0.001D)
        {
            browser.ZoomFactor = 1D;
        }

        // Solicita a WPF que vuelva a medir y dibujar el host cuando la ventana
        // cambia a un monitor con otro porcentaje de escalado.
        browser.InvalidateMeasure();
        browser.InvalidateArrange();
        browser.InvalidateVisual();

        appliedScale = requestedScale;
    }
}
