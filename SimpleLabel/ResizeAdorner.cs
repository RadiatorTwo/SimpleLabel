using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SimpleLabel
{
    public class ResizeAdorner : Adorner
    {
        // 8 Thumb controls for resize handles
        private Thumb topLeft, topCenter, topRight;
        private Thumb middleLeft, middleRight;
        private Thumb bottomLeft, bottomCenter, bottomRight;

        private VisualCollection visualChildren;
        private FrameworkElement? adornedElement;
        private Size initialSize; // Track initial size for undo/redo
        private MainWindow? mainWindow; // Reference to MainWindow for command execution

        public ResizeAdorner(UIElement adornedElement) : base(adornedElement)
        {
            this.adornedElement = adornedElement as FrameworkElement;
            visualChildren = new VisualCollection(this);

            // Find MainWindow
            mainWindow = Window.GetWindow(adornedElement) as MainWindow;

            // Create 8 Thumb controls with consistent styling
            BuildThumb(ref topLeft, Cursors.SizeNWSE);
            BuildThumb(ref topCenter, Cursors.SizeNS);
            BuildThumb(ref topRight, Cursors.SizeNESW);
            BuildThumb(ref middleLeft, Cursors.SizeWE);
            BuildThumb(ref middleRight, Cursors.SizeWE);
            BuildThumb(ref bottomLeft, Cursors.SizeNESW);
            BuildThumb(ref bottomCenter, Cursors.SizeNS);
            BuildThumb(ref bottomRight, Cursors.SizeNWSE);

            // Wire up DragStarted and DragCompleted events
            topLeft.DragStarted += Thumb_DragStarted;
            topCenter.DragStarted += Thumb_DragStarted;
            topRight.DragStarted += Thumb_DragStarted;
            middleLeft.DragStarted += Thumb_DragStarted;
            middleRight.DragStarted += Thumb_DragStarted;
            bottomLeft.DragStarted += Thumb_DragStarted;
            bottomCenter.DragStarted += Thumb_DragStarted;
            bottomRight.DragStarted += Thumb_DragStarted;

            topLeft.DragCompleted += Thumb_DragCompleted;
            topCenter.DragCompleted += Thumb_DragCompleted;
            topRight.DragCompleted += Thumb_DragCompleted;
            middleLeft.DragCompleted += Thumb_DragCompleted;
            middleRight.DragCompleted += Thumb_DragCompleted;
            bottomLeft.DragCompleted += Thumb_DragCompleted;
            bottomCenter.DragCompleted += Thumb_DragCompleted;
            bottomRight.DragCompleted += Thumb_DragCompleted;

            // Wire up DragDelta events
            topLeft.DragDelta += HandleTopLeft;
            topCenter.DragDelta += HandleTop;
            topRight.DragDelta += HandleTopRight;
            middleLeft.DragDelta += HandleLeft;
            middleRight.DragDelta += HandleRight;
            bottomLeft.DragDelta += HandleBottomLeft;
            bottomCenter.DragDelta += HandleBottom;
            bottomRight.DragDelta += HandleBottomRight;
        }

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (adornedElement != null)
            {
                initialSize = new Size(adornedElement.Width, adornedElement.Height);
            }
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (adornedElement != null && mainWindow != null)
            {
                Size finalSize = new Size(adornedElement.Width, adornedElement.Height);
                // Only create command if size actually changed
                if (initialSize != finalSize)
                {
                    mainWindow.ExecuteResizeCommand(adornedElement, initialSize, finalSize);
                }
            }
        }

        private void BuildThumb(ref Thumb thumb, Cursor cursor)
        {
            thumb = new Thumb
            {
                Width = 8,
                Height = 8,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Cursor = cursor
            };
            visualChildren.Add(thumb);
        }

        protected override int VisualChildrenCount => visualChildren.Count;
        protected override Visual GetVisualChild(int index) => visualChildren[index];

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (adornedElement == null) return finalSize;

            double width = adornedElement.ActualWidth;
            double height = adornedElement.ActualHeight;
            double thumbSize = 8;
            double halfThumb = thumbSize / 2;

            // Arrange 8 thumbs at corners and edges
            topLeft.Arrange(new Rect(-halfThumb, -halfThumb, thumbSize, thumbSize));
            topCenter.Arrange(new Rect(width/2 - halfThumb, -halfThumb, thumbSize, thumbSize));
            topRight.Arrange(new Rect(width - halfThumb, -halfThumb, thumbSize, thumbSize));
            middleLeft.Arrange(new Rect(-halfThumb, height/2 - halfThumb, thumbSize, thumbSize));
            middleRight.Arrange(new Rect(width - halfThumb, height/2 - halfThumb, thumbSize, thumbSize));
            bottomLeft.Arrange(new Rect(-halfThumb, height - halfThumb, thumbSize, thumbSize));
            bottomCenter.Arrange(new Rect(width/2 - halfThumb, height - halfThumb, thumbSize, thumbSize));
            bottomRight.Arrange(new Rect(width - halfThumb, height - halfThumb, thumbSize, thumbSize));

            return finalSize;
        }

        private void HandleBottomRight(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newWidth = adornedElement.Width + e.HorizontalChange;
            double newHeight = adornedElement.Height + e.VerticalChange;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                // Use the larger change to determine the new size
                if (Math.Abs(e.HorizontalChange) > Math.Abs(e.VerticalChange))
                {
                    newHeight = newWidth / aspectRatio;
                }
                else
                {
                    newWidth = newHeight * aspectRatio;
                }
            }

            if (newWidth > 10) adornedElement.Width = newWidth;
            if (newHeight > 10) adornedElement.Height = newHeight;

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }

        private void HandleTopLeft(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newWidth = adornedElement.Width - e.HorizontalChange;
            double newHeight = adornedElement.Height - e.VerticalChange;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                // Use the larger change to determine the new size
                if (Math.Abs(e.HorizontalChange) > Math.Abs(e.VerticalChange))
                {
                    newHeight = newWidth / aspectRatio;
                }
                else
                {
                    newWidth = newHeight * aspectRatio;
                }
            }

            if (newWidth > 10)
            {
                double currentLeft = Canvas.GetLeft(adornedElement);
                if (double.IsNaN(currentLeft)) currentLeft = 0.0;
                Canvas.SetLeft(adornedElement, currentLeft + (adornedElement.Width - newWidth));
                adornedElement.Width = newWidth;
            }
            if (newHeight > 10)
            {
                double currentTop = Canvas.GetTop(adornedElement);
                if (double.IsNaN(currentTop)) currentTop = 0.0;
                Canvas.SetTop(adornedElement, currentTop + (adornedElement.Height - newHeight));
                adornedElement.Height = newHeight;
            }

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }

        private void HandleTop(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newHeight = adornedElement.Height - e.VerticalChange;
            double newWidth = adornedElement.Width;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                newWidth = newHeight * aspectRatio;
            }

            if (newWidth > 10 && newHeight > 10)
            {
                double currentTop = Canvas.GetTop(adornedElement);
                if (double.IsNaN(currentTop)) currentTop = 0.0;
                Canvas.SetTop(adornedElement, currentTop + (adornedElement.Height - newHeight));
                adornedElement.Height = newHeight;
                adornedElement.Width = newWidth;
            }

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }

        private void HandleTopRight(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newWidth = adornedElement.Width + e.HorizontalChange;
            double newHeight = adornedElement.Height - e.VerticalChange;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                // Use the larger change to determine the new size
                if (Math.Abs(e.HorizontalChange) > Math.Abs(e.VerticalChange))
                {
                    newHeight = newWidth / aspectRatio;
                }
                else
                {
                    newWidth = newHeight * aspectRatio;
                }
            }

            if (newWidth > 10)
            {
                adornedElement.Width = newWidth;
            }
            if (newHeight > 10)
            {
                double currentTop = Canvas.GetTop(adornedElement);
                if (double.IsNaN(currentTop)) currentTop = 0.0;
                Canvas.SetTop(adornedElement, currentTop + (adornedElement.Height - newHeight));
                adornedElement.Height = newHeight;
            }

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }

        private void HandleLeft(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newWidth = adornedElement.Width - e.HorizontalChange;
            double newHeight = adornedElement.Height;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                newHeight = newWidth / aspectRatio;
            }

            if (newWidth > 10 && newHeight > 10)
            {
                double currentLeft = Canvas.GetLeft(adornedElement);
                if (double.IsNaN(currentLeft)) currentLeft = 0.0;
                Canvas.SetLeft(adornedElement, currentLeft + (adornedElement.Width - newWidth));
                adornedElement.Width = newWidth;
                adornedElement.Height = newHeight;
            }

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }

        private void HandleRight(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newWidth = adornedElement.Width + e.HorizontalChange;
            double newHeight = adornedElement.Height;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                newHeight = newWidth / aspectRatio;
            }

            if (newWidth > 10 && newHeight > 10)
            {
                adornedElement.Width = newWidth;
                adornedElement.Height = newHeight;
            }

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }

        private void HandleBottomLeft(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newWidth = adornedElement.Width - e.HorizontalChange;
            double newHeight = adornedElement.Height + e.VerticalChange;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                // Use the larger change to determine the new size
                if (Math.Abs(e.HorizontalChange) > Math.Abs(e.VerticalChange))
                {
                    newHeight = newWidth / aspectRatio;
                }
                else
                {
                    newWidth = newHeight * aspectRatio;
                }
            }

            if (newWidth > 10)
            {
                double currentLeft = Canvas.GetLeft(adornedElement);
                if (double.IsNaN(currentLeft)) currentLeft = 0.0;
                Canvas.SetLeft(adornedElement, currentLeft + (adornedElement.Width - newWidth));
                adornedElement.Width = newWidth;
            }
            if (newHeight > 10)
            {
                adornedElement.Height = newHeight;
            }

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }

        private void HandleBottom(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            double newHeight = adornedElement.Height + e.VerticalChange;
            double newWidth = adornedElement.Width;

            // If Shift is pressed, maintain aspect ratio
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double aspectRatio = adornedElement.Width / adornedElement.Height;
                newWidth = newHeight * aspectRatio;
            }

            if (newWidth > 10 && newHeight > 10)
            {
                adornedElement.Height = newHeight;
                adornedElement.Width = newWidth;
            }

            // Update properties panel in real-time during resize
            mainWindow?.UpdatePropertiesPanel();
        }
    }
}
