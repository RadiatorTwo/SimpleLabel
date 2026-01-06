using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimpleLabel.Commands;

public class ChangeTextPropertyCommand : ICommand
{
    private readonly TextBlock element;
    private readonly string propertyName;
    private readonly object oldValue;
    private readonly object newValue;

    public ChangeTextPropertyCommand(TextBlock element, string propertyName, object oldValue, object newValue)
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
            case "Text":
                element.Text = (string)value;
                break;
            case "FontFamily":
                element.FontFamily = new FontFamily((string)value);
                break;
            case "FontSize":
                element.FontSize = (double)value;
                break;
            case "Foreground":
                var color = (Color)ColorConverter.ConvertFromString((string)value);
                element.Foreground = new SolidColorBrush(color);
                break;
            case "TextAlignment":
                element.TextAlignment = Enum.Parse<TextAlignment>((string)value);
                break;
            case "FontWeight":
                element.FontWeight = (string)value == "Bold" ? FontWeights.Bold : FontWeights.Normal;
                break;
            case "FontStyle":
                element.FontStyle = (string)value == "Italic" ? FontStyles.Italic : FontStyles.Normal;
                break;
        }
    }
}
