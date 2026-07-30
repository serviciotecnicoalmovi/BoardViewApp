using System.Text;
using BoardView.Core.Pdf;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Reconstruye palabras a partir de los caracteres y coordenadas proporcionados
/// por PDFium. No aplica OCR ni interpreta geometría electrónica.
/// </summary>
internal sealed class PdfiumTextWordBuilder
{
    private readonly List<PdfWord> words = [];
    private readonly StringBuilder text = new();
    private double left;
    private double right;
    private double bottom;
    private double top;
    private double previousRight;
    private double previousBottom;
    private double previousTop;
    private bool hasBounds;

    public void Append(char character, CharacterBox? box)
    {
        if (IsSeparator(character))
        {
            Flush();
            return;
        }

        if (box is not null && ShouldSplitBefore(box.Value))
        {
            Flush();
        }

        text.Append(character);

        if (box is not null)
        {
            Include(box.Value);
        }
    }

    public IReadOnlyList<PdfWord> Complete()
    {
        Flush();
        return words;
    }

    private bool ShouldSplitBefore(CharacterBox box)
    {
        if (!hasBounds || text.Length == 0)
        {
            return false;
        }

        double currentHeight = Math.Max(0.1D, box.Top - box.Bottom);
        double previousHeight = Math.Max(0.1D, previousTop - previousBottom);
        double verticalTolerance = Math.Max(currentHeight, previousHeight) * 0.55D;
        bool changedLine = Math.Abs(box.Bottom - previousBottom) > verticalTolerance;

        double gap = box.Left - previousRight;
        double gapThreshold = Math.Max(currentHeight, previousHeight) * 0.65D;
        bool separatedHorizontally = gap > gapThreshold;

        // Un retroceso horizontal importante normalmente indica un nuevo bloque
        // de texto aunque el PDF no haya insertado un espacio explícito.
        bool movedBackwards = box.Right < previousRight - gapThreshold;
        return changedLine || separatedHorizontally || movedBackwards;
    }

    private void Include(CharacterBox box)
    {
        if (!hasBounds)
        {
            left = box.Left;
            right = box.Right;
            bottom = box.Bottom;
            top = box.Top;
            hasBounds = true;
        }
        else
        {
            left = Math.Min(left, box.Left);
            right = Math.Max(right, box.Right);
            bottom = Math.Min(bottom, box.Bottom);
            top = Math.Max(top, box.Top);
        }

        previousRight = box.Right;
        previousBottom = box.Bottom;
        previousTop = box.Top;
    }

    private void Flush()
    {
        if (text.Length == 0)
        {
            ResetBounds();
            return;
        }

        string value = text.ToString().Trim();
        if (value.Length > 0)
        {
            double wordLeft = hasBounds ? left : 0D;
            double wordBottom = hasBounds ? bottom : 0D;
            double width = hasBounds ? Math.Max(0D, right - left) : 0D;
            double height = hasBounds ? Math.Max(0D, top - bottom) : 0D;
            words.Add(new PdfWord(value, wordLeft, wordBottom, width, height));
        }

        text.Clear();
        ResetBounds();
    }

    private void ResetBounds()
    {
        left = 0D;
        right = 0D;
        bottom = 0D;
        top = 0D;
        previousRight = 0D;
        previousBottom = 0D;
        previousTop = 0D;
        hasBounds = false;
    }

    private static bool IsSeparator(char character) =>
        char.IsWhiteSpace(character) ||
        character is '\u0000' or '\u0002' or '\u0003' or '\u000C' or '\uFFFE' or '\uFFFF';

    internal readonly record struct CharacterBox(
        double Left,
        double Right,
        double Bottom,
        double Top);
}
