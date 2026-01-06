using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimpleLabel.Commands;

public class ChangeShapePropertyCommand : ICommand
{
    private readonly Shape element;
    private readonly string propertyName;
    private readonly object oldValue;
    private readonly object newValue;

    public ChangeShapePropertyCommand(Shape element, string propertyName, object oldValue, object newValue)
    {
        this.element = element;
        this.propertyName = propertyName;
        this.oldValue = oldValue;
        this.newValue = newValue;
    }

    public void Execute()
    {
        ApplyValue(newValue);
    }

    public void Undo()
    {
        ApplyValue(oldValue);
    }

    private void ApplyValue(object value)
    {
        switch (propertyName)
        {
            case "Fill":
                var fillColor = (Color)ColorConverter.ConvertFromString((string)value);
                element.Fill = new SolidColorBrush(fillColor);
                break;
            case "Stroke":
                var strokeColor = (Color)ColorConverter.ConvertFromString((string)value);
                element.Stroke = new SolidColorBrush(strokeColor);
                break;
            case "StrokeThickness":
                element.StrokeThickness = (double)value;
                break;

            // Extended shape properties
            case "RadiusX":
                if (element is Rectangle rect)
                    rect.RadiusX = (double)value;
                break;
            case "RadiusY":
                if (element is Rectangle rect2)
                    rect2.RadiusY = (double)value;
                break;

            case "StrokeDashPattern":
                ApplyDashPattern((string)value);
                break;

            case "UseGradientFill":
                ApplyGradientToggle((bool)value);
                break;

            case "GradientStartColor":
            case "GradientEndColor":
            case "GradientAngle":
                ApplyGradientProperty(propertyName, value);
                break;
        }
    }

    private void ApplyDashPattern(string pattern)
    {
        element.StrokeDashArray = pattern switch
        {
            "Dash" => new DoubleCollection { 2, 2 },
            "Dot" => new DoubleCollection { 1, 2 },
            "DashDot" => new DoubleCollection { 2, 2, 1, 2 },
            _ => null
        };
    }

    private void ApplyGradientToggle(bool useGradient)
    {
        if (useGradient)
        {
            // Solid → Gradient
            var currentColor = (element.Fill as SolidColorBrush)?.Color ?? Colors.White;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(currentColor, 0));
            brush.GradientStops.Add(new GradientStop(Colors.White, 1));
            element.Fill = brush;
        }
        else
        {
            // Gradient → Solid
            var gradientBrush = element.Fill as LinearGradientBrush;
            var color = gradientBrush?.GradientStops.FirstOrDefault()?.Color ?? Colors.White;
            element.Fill = new SolidColorBrush(color);
        }
    }

    private void ApplyGradientProperty(string property, object value)
    {
        if (element.Fill is not LinearGradientBrush brush || brush.GradientStops.Count < 2)
            return;

        switch (property)
        {
            case "GradientStartColor":
                var startColor = (Color)ColorConverter.ConvertFromString((string)value);
                brush.GradientStops[0] = new GradientStop(startColor, 0);
                break;
            case "GradientEndColor":
                var endColor = (Color)ColorConverter.ConvertFromString((string)value);
                brush.GradientStops[^1] = new GradientStop(endColor, 1);
                break;
            case "GradientAngle":
                double angle = (double)value;
                double radians = angle * Math.PI / 180;
                brush.StartPoint = new Point(0.5 - Math.Cos(radians) * 0.5, 0.5 - Math.Sin(radians) * 0.5);
                brush.EndPoint = new Point(0.5 + Math.Cos(radians) * 0.5, 0.5 + Math.Sin(radians) * 0.5);
                break;
        }
    }
}
