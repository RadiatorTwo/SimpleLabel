namespace SimpleLabel.Models;

public class CanvasElement
{
    public string ElementType { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // Text-specific properties
    public string? Text { get; set; }
    public double? FontSize { get; set; }
    public string? ForegroundColor { get; set; }
    public string? FontFamily { get; set; }
    public string? TextAlignment { get; set; } // "Left", "Center", "Right"
    public string? FontWeight { get; set; } // "Normal", "Bold"
    public string? FontStyle { get; set; } // "Normal", "Italic"

    // Rectangle/Ellipse-specific properties
    public string? FillColor { get; set; }
    public string? StrokeColor { get; set; }
    public double? StrokeThickness { get; set; }

    // Extended Shape properties
    public double? RadiusX { get; set; }
    public double? RadiusY { get; set; }
    public string? StrokeDashPattern { get; set; } // "Solid", "Dash", "Dot", "DashDot"

    // Gradient fill properties
    public bool? UseGradientFill { get; set; }
    public string? GradientStartColor { get; set; }
    public string? GradientEndColor { get; set; }
    public double? GradientAngle { get; set; }

    // Image-specific properties
    public string? ImagePath { get; set; }
    public bool? MonochromeEnabled { get; set; }
    public byte? Threshold { get; set; }

    // Advanced monochrome filter properties
    public string? MonochromeAlgorithm { get; set; } // "Threshold", "FloydSteinberg", "Ordered", "Atkinson"
    public bool? InvertColors { get; set; }
    public double? Brightness { get; set; } // -100 to 100
    public double? Contrast { get; set; } // -100 to 100
}
