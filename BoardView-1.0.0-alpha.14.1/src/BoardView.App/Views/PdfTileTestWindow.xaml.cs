using System.Windows;

namespace BoardView.App.Views;

/// <summary>
/// Ventana temporal para validar el renderizado de teselas PDFium
/// sin modificar el visor principal de BoardView.
/// </summary>
public partial class PdfTileTestWindow : Window
{
    public PdfTileTestWindow()
    {
        InitializeComponent();
    }
}
