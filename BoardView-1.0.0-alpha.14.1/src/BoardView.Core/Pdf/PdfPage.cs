namespace BoardView.Core.Pdf;

/// <summary>
/// Contiene la información técnica extraída de una página PDF.
/// </summary>
public sealed class PdfPage
{
    public PdfPage(int number, double widthPoints, double heightPoints, IReadOnlyList<PdfWord> words)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        Number = number;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        Words = words ?? throw new ArgumentNullException(nameof(words));
    }

    public int Number { get; }

    public double WidthPoints { get; }

    public double HeightPoints { get; }

    public IReadOnlyList<PdfWord> Words { get; }

    public string PlainText => string.Join(' ', Words.Select(word => word.Text));
}
