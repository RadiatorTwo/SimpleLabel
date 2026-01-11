using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Event args for layer reorder requests.
/// </summary>
public class ReorderEventArgs : EventArgs
{
    public int OldIndex { get; }
    public int NewIndex { get; }

    public ReorderEventArgs(int oldIndex, int newIndex)
    {
        OldIndex = oldIndex;
        NewIndex = newIndex;
    }
}

/// <summary>
/// Layers panel for managing canvas element Z-order.
/// </summary>
public partial class LayersPanel : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;

    /// <summary>
    /// Dependency property for the layers collection.
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(ObservableCollection<LayerItem>),
            typeof(LayersPanel),
            new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// Gets or sets the collection of layer items to display.
    /// </summary>
    public ObservableCollection<LayerItem>? ItemsSource
    {
        get => (ObservableCollection<LayerItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected layer item (primary selection).
    /// </summary>
    public LayerItem? SelectedLayerItem
    {
        get => layersList.SelectedItem as LayerItem;
        set => layersList.SelectedItem = value;
    }

    /// <summary>
    /// Gets the collection of all selected layer items (for multi-selection).
    /// </summary>
    public System.Collections.IList SelectedLayerItems => layersList.SelectedItems;

    /// <summary>
    /// Occurs when the selected layer changes.
    /// </summary>
    public event EventHandler<LayerItem?>? SelectionChanged;

    /// <summary>
    /// Occurs when layers should be reordered via drag&drop.
    /// </summary>
    public event EventHandler<ReorderEventArgs>? ReorderRequested;

    public LayersPanel()
    {
        InitializeComponent();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LayersPanel panel)
        {
            panel.layersList.ItemsSource = e.NewValue as ObservableCollection<LayerItem>;
        }
    }

    private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectionChanged?.Invoke(this, SelectedLayerItem);
    }

    private void LayersList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(layersList);
        _isDragging = false;
    }

    private void LayersList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPos = e.GetPosition(layersList);
        Vector diff = currentPos - _dragStartPoint;

        // Check if we've moved far enough to start a drag
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (_isDragging)
                return;

            // Find the ListBoxItem under the mouse
            var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (listBoxItem == null)
                return;

            // Get the LayerItem from the ListBoxItem
            var layerItem = listBoxItem.DataContext as LayerItem;
            if (layerItem == null)
                return;

            _isDragging = true;

            // Start drag operation
            var data = new DataObject(typeof(LayerItem), layerItem);
            DragDrop.DoDragDrop(listBoxItem, data, DragDropEffects.Move);

            _isDragging = false;
        }
    }

    private void LayersList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(LayerItem)))
        {
            e.Effects = DragDropEffects.None;
            HideDropIndicator();
        }
        else
        {
            e.Effects = DragDropEffects.Move;
            UpdateDropIndicator(e);
        }
        e.Handled = true;
    }

    private void LayersList_DragLeave(object sender, DragEventArgs e)
    {
        HideDropIndicator();
    }

    private void UpdateDropIndicator(DragEventArgs e)
    {
        if (ItemsSource == null || ItemsSource.Count == 0)
        {
            HideDropIndicator();
            return;
        }

        // Find the target ListBoxItem under the mouse
        Point pos = e.GetPosition(layersList);
        var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

        if (targetItem != null)
        {
            // Get item position relative to ListBox
            Point itemPos = targetItem.TranslatePoint(new Point(0, 0), layersList);
            double itemHeight = targetItem.ActualHeight;

            // Determine if we're in the top or bottom half of the item
            double relativeY = pos.Y - itemPos.Y;
            bool insertAbove = relativeY < itemHeight / 2;

            // Position the indicator
            double indicatorY = insertAbove ? itemPos.Y : itemPos.Y + itemHeight;

            ShowDropIndicatorAt(indicatorY);
        }
        else
        {
            // Not over an item - check if we're below the last item
            var lastContainer = layersList.ItemContainerGenerator.ContainerFromIndex(ItemsSource.Count - 1) as ListBoxItem;
            if (lastContainer != null)
            {
                Point lastItemBottom = lastContainer.TranslatePoint(new Point(0, lastContainer.ActualHeight), layersList);

                // If mouse is below the last item, show indicator at bottom
                if (pos.Y >= lastItemBottom.Y - 10)
                {
                    ShowDropIndicatorAt(lastItemBottom.Y);
                }
                else
                {
                    HideDropIndicator();
                }
            }
            else
            {
                HideDropIndicator();
            }
        }
    }

    private void ShowDropIndicatorAt(double y)
    {
        Canvas.SetLeft(dropIndicator, 4);
        Canvas.SetTop(dropIndicator, y - 1.5);
        dropIndicator.Width = layersList.ActualWidth - 8;
        dropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator()
    {
        dropIndicator.Visibility = Visibility.Collapsed;
    }

    private void LayersList_Drop(object sender, DragEventArgs e)
    {
        HideDropIndicator();

        if (!e.Data.GetDataPresent(typeof(LayerItem)))
            return;

        var droppedItem = e.Data.GetData(typeof(LayerItem)) as LayerItem;
        if (droppedItem == null || ItemsSource == null || ItemsSource.Count == 0)
            return;

        int oldIndex = ItemsSource.IndexOf(droppedItem);
        if (oldIndex < 0)
            return;

        // Find the target ListBoxItem
        var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

        int newIndex;

        if (targetItem != null)
        {
            var targetLayerItem = targetItem.DataContext as LayerItem;
            if (targetLayerItem == null || targetLayerItem == droppedItem)
                return;

            int targetIndex = ItemsSource.IndexOf(targetLayerItem);

            // Determine if dropping above or below the target based on mouse position
            Point pos = e.GetPosition(targetItem);
            bool insertBelow = pos.Y > targetItem.ActualHeight / 2;

            newIndex = targetIndex;
            if (insertBelow)
            {
                newIndex++;
            }
        }
        else
        {
            // Dropped outside of any item - check if below the last item
            Point pos = e.GetPosition(layersList);

            // Get the last item's bounds
            var lastContainer = layersList.ItemContainerGenerator.ContainerFromIndex(ItemsSource.Count - 1) as ListBoxItem;
            if (lastContainer != null)
            {
                Point lastItemBottom = lastContainer.TranslatePoint(new Point(0, lastContainer.ActualHeight), layersList);
                if (pos.Y >= lastItemBottom.Y - 5) // Small tolerance
                {
                    // Drop at end of list
                    newIndex = ItemsSource.Count;
                }
                else
                {
                    return; // Dropped in empty space above items, ignore
                }
            }
            else
            {
                return;
            }
        }

        // Adjust index if moving from above to below
        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        if (newIndex >= 0 && newIndex < ItemsSource.Count && oldIndex != newIndex)
        {
            ReorderRequested?.Invoke(this, new ReorderEventArgs(oldIndex, newIndex));
        }
    }

    /// <summary>
    /// Finds an ancestor of the specified type in the visual tree.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T found)
                return found;

            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
