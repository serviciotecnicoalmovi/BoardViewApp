using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.App.Samples;

/// <summary>Construye un documento determinista para validar el modelo interno y el renderizador.</summary>
public static class DemonstrationBoardFactory
{
    /// <summary>Crea una placa de demostración completa y válida.</summary>
    public static BoardDocument Create()
    {
        BoardDocument document = new("Placa de demostración", "boardview://demonstration");
        document.AddLayer(new BoardLayer("outline", "Contorno", LayerType.Outline, BoardSide.Both, 0));
        document.AddLayer(new BoardLayer("top-copper", "Cobre superior", LayerType.Copper, BoardSide.Top, 10));
        document.AddLayer(new BoardLayer("bottom-copper", "Cobre inferior", LayerType.Copper, BoardSide.Bottom, 20));
        document.AddNet(new BoardNet("gnd", "GND"));
        document.AddNet(new BoardNet("vcc", "+3V3"));
        document.AddNet(new BoardNet("signal", "DATA"));
        document.AddComponent(new BoardComponent("u1", "U1", "MCU", new Point2D(55, 37), 0, BoardSide.Top));
        document.AddComponent(new BoardComponent("j1", "J1", "HEADER", new Point2D(18, 37), 90, BoardSide.Top));
        document.AddComponent(new BoardComponent("r1", "R1", "10K", new Point2D(83, 24), 0, BoardSide.Top));
        document.AddElement(new PolygonElement("board-outline", "outline", [new Point2D(5, 5), new Point2D(105, 5), new Point2D(105, 69), new Point2D(5, 69)], false));
        AddConnectorPads(document);
        AddMicrocontrollerPads(document);
        AddRoutes(document);
        AddVias(document);
        return document;
    }

    private static void AddConnectorPads(BoardDocument document)
    {
        for (int index = 0; index < 6; index++)
        {
            double y = 22 + (index * 6);
            string netId = (index % 3) switch { 0 => "gnd", 1 => "vcc", _ => "signal" };
            document.AddElement(new PadElement($"j1-pad-{index + 1}", "top-copper", new Point2D(18, y), 4.2, 4.2, PadShape.Circle, netId));
        }
    }

    private static void AddMicrocontrollerPads(BoardDocument document)
    {
        for (int index = 0; index < 8; index++)
        {
            double x = 39 + (index * 4.6);
            string netId = (index % 3) switch { 0 => "gnd", 1 => "vcc", _ => "signal" };
            document.AddElement(new PadElement($"u1-top-{index + 1}", "top-copper", new Point2D(x, 27), 2.6, 5.2, PadShape.RoundedRectangle, netId));
            document.AddElement(new PadElement($"u1-bottom-{index + 1}", "top-copper", new Point2D(x, 47), 2.6, 5.2, PadShape.RoundedRectangle, netId));
        }
    }

    private static void AddRoutes(BoardDocument document)
    {
        document.AddElement(new TrackElement("track-1", "top-copper", new Point2D(20, 22), new Point2D(38, 27), 1.0, "gnd"));
        document.AddElement(new TrackElement("track-2", "top-copper", new Point2D(20, 28), new Point2D(42.6, 27), 0.8, "vcc"));
        document.AddElement(new TrackElement("track-3", "top-copper", new Point2D(20, 34), new Point2D(47.2, 27), 0.65, "signal"));
        document.AddElement(new TrackElement("track-4", "top-copper", new Point2D(20, 40), new Point2D(47.2, 47), 0.65, "gnd"));
        document.AddElement(new TrackElement("track-5", "top-copper", new Point2D(20, 46), new Point2D(51.8, 47), 0.8, "vcc"));
        document.AddElement(new TrackElement("track-6", "top-copper", new Point2D(20, 52), new Point2D(56.4, 47), 1.0, "signal"));
        document.AddElement(new TrackElement("track-7", "top-copper", new Point2D(70.2, 27), new Point2D(83, 24), 0.7, "signal"));
        document.AddElement(new TrackElement("track-8", "bottom-copper", new Point2D(61, 47), new Point2D(91, 57), 0.9, "gnd"));
    }

    private static void AddVias(BoardDocument document)
    {
        document.AddElement(new ViaElement("via-1", "top-copper", new Point2D(32, 25.3), 3.0, 1.2, "gnd"));
        document.AddElement(new ViaElement("via-2", "top-copper", new Point2D(76, 29), 3.0, 1.2, "signal"));
        document.AddElement(new ViaElement("via-3", "bottom-copper", new Point2D(91, 57), 3.4, 1.3, "gnd"));
    }
}
