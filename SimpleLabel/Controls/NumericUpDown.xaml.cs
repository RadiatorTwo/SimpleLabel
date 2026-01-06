using System.Windows;
using System.Windows.Controls;

namespace SimpleLabel.Controls;

public partial class NumericUpDown : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericUpDown),
            new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericUpDown),
            new PropertyMetadata(double.MinValue));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericUpDown),
            new PropertyMetadata(double.MaxValue));

    public static readonly DependencyProperty IncrementProperty =
        DependencyProperty.Register(nameof(Increment), typeof(double), typeof(NumericUpDown),
            new PropertyMetadata(1.0));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Increment
    {
        get => (double)GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public new event RoutedEventHandler? GotFocus;
    public new event RoutedEventHandler? LostFocus;
    public event EventHandler<double>? ValueChanged;

    private bool isUpdatingText;
    private bool suppressValueChangedEvent;

    public NumericUpDown()
    {
        InitializeComponent();
        UpdateTextBox();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumericUpDown control)
        {
            control.Value = Math.Clamp((double)e.NewValue, control.Minimum, control.Maximum);
            control.UpdateTextBox();
        }
    }

    private void UpdateTextBox()
    {
        if (!isUpdatingText)
        {
            isUpdatingText = true;
            textBox.Text = Value.ToString("F2");
            isUpdatingText = false;
        }
    }

    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        GotFocus?.Invoke(this, e);
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ValidateAndUpdateValue();
        LostFocus?.Invoke(this, e);
    }

    private void TextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Handle Enter key to confirm input
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ValidateAndUpdateValue();
            // Move focus away from textbox to trigger LostFocus on parent
            textBox.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void ValidateAndUpdateValue()
    {
        if (double.TryParse(textBox.Text, out double value))
        {
            Value = Math.Clamp(value, Minimum, Maximum);
        }
        else
        {
            UpdateTextBox();
        }
    }

    private void BtnUp_Click(object sender, RoutedEventArgs e)
    {
        Value = Math.Min(Value + Increment, Maximum);
        ValueChanged?.Invoke(this, Value);
    }

    private void BtnDown_Click(object sender, RoutedEventArgs e)
    {
        Value = Math.Max(Value - Increment, Minimum);
        ValueChanged?.Invoke(this, Value);
    }
}
