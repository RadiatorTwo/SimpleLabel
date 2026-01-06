namespace SimpleLabel.Models;

public class LabelDocument
{
    public double CanvasWidth { get; set; }
    public double CanvasHeight { get; set; }
    public List<CanvasElement> Elements { get; set; } = new();
}
