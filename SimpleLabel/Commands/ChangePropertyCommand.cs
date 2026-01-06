using System.Windows;
using System.Windows.Controls;

namespace SimpleLabel.Commands;

public class ChangePropertyCommand : ICommand
{
    private readonly UIElement element;
    private readonly string propertyName;
    private readonly double oldValue;
    private readonly double newValue;

    public ChangePropertyCommand(UIElement element, string propertyName, double oldValue, double newValue)
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

    private void ApplyValue(double value)
    {
        switch (propertyName)
        {
            case "X":
                Canvas.SetLeft(element, value);
                break;
            case "Y":
                Canvas.SetTop(element, value);
                break;
            case "Width":
                if (element is FrameworkElement fe1)
                    fe1.Width = value;
                break;
            case "Height":
                if (element is FrameworkElement fe2)
                    fe2.Height = value;
                break;
        }
    }
}
