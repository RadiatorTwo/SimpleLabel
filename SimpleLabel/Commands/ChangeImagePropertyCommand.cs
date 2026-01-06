using System.Windows;
using System.Windows.Controls;

namespace SimpleLabel.Commands;

public class ChangeImagePropertyCommand : ICommand
{
    private readonly Image element;
    private readonly string propertyName;
    private readonly object oldValue;
    private readonly object newValue;
    private readonly Action<Image> applyFilterCallback;

    public ChangeImagePropertyCommand(Image element, string propertyName, object oldValue, object newValue, Action<Image> applyFilterCallback)
    {
        this.element = element;
        this.propertyName = propertyName;
        this.oldValue = oldValue;
        this.newValue = newValue;
        this.applyFilterCallback = applyFilterCallback;
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
        // Get the CanvasElement from Image.Tag
        if (element.Tag is Tuple<Models.CanvasElement, System.Windows.Media.Imaging.BitmapSource> tuple)
        {
            var canvasElement = tuple.Item1;

            switch (propertyName)
            {
                case "MonochromeEnabled":
                    canvasElement.MonochromeEnabled = (bool)value;
                    break;
                case "Threshold":
                    canvasElement.Threshold = (byte)value;
                    break;
                case "Algorithm":
                    canvasElement.MonochromeAlgorithm = (string)value;
                    break;
                case "Brightness":
                    canvasElement.Brightness = (double)value;
                    break;
                case "Contrast":
                    canvasElement.Contrast = (double)value;
                    break;
                case "Invert":
                    canvasElement.InvertColors = (bool)value;
                    break;
            }

            // Apply the filter to update the visual display
            applyFilterCallback(element);
        }
    }
}
