using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TokenArtTagger.Core;
using TokenArtTagger.App.ViewModels;

namespace TokenArtTagger.App;

public partial class MainWindow : Window
{
    private System.Windows.Point _dragStartPoint;
    private System.Windows.Point _rectangleStartPoint;
    private System.Windows.Controls.ListBox? _rectangleList;
    private IReadOnlyList<SelectionTile<ImageItemViewModel>> _rectangleSnapshot = [];
    private bool _isRectangleSelecting;
    private RubberBandAdorner? _rubberBandAdorner;
    private AdornerLayer? _rubberBandLayer;
    private System.Windows.Point? _inspectorPanStart;
    private double _inspectorPanStartX;
    private double _inspectorPanStartY;
    private CancellationTokenSource? _resizeQuietCancellation;
    private bool _isResizing;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.RequestBucketSelectionAction += HandleBucketSelectionAction;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.ReplaceSelection(LibraryList.SelectedItems.Cast<ImageItemViewModel>());
    }

    private void BucketList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.ReplaceBucketSelection(BucketList.SelectedItems.Cast<ImageItemViewModel>());
    }

    private void ImageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox list)
        {
            list.Focus();
            _dragStartPoint = e.GetPosition(list);
            if (IsFromScrollBar(e.OriginalSource as DependencyObject) ||
                FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
            {
                return;
            }

            _rectangleList = list;
            _rectangleStartPoint = ClampPointToList(list, _dragStartPoint);
            _rectangleSnapshot = CaptureSelectionSnapshot(list);
            _isRectangleSelecting = true;
            _rubberBandLayer = AdornerLayer.GetAdornerLayer(list);
            _rubberBandAdorner = new RubberBandAdorner(list);
            _rubberBandLayer?.Add(_rubberBandAdorner);
            list.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ImageList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isRectangleSelecting && _rectangleList is not null)
        {
            try
            {
                UpdateRubberBand(e.GetPosition(_rectangleList));
            }
            catch (InvalidOperationException ex)
            {
                CancelRectangleSelection("Rectangle selection was canceled because the grid layout changed.", ex);
            }

            return;
        }

        if (sender is not System.Windows.Controls.ListBox list ||
            e.LeftButton != MouseButtonState.Pressed ||
            list.SelectedItems.Count == 0 ||
            IsFromScrollBar(e.OriginalSource as DependencyObject) ||
            FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(list);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(list, "selected-images", System.Windows.DragDropEffects.Copy);
    }

    private void ImageList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRectangleSelecting || _rectangleList is null)
        {
            return;
        }

        var list = _rectangleList;
        System.Windows.Point endPoint;
        try
        {
            endPoint = ClampPointToList(list, e.GetPosition(list));
        }
        catch (InvalidOperationException ex)
        {
            CancelRectangleSelection("Rectangle selection was canceled because the grid layout changed.", ex);
            e.Handled = true;
            return;
        }

        if (Math.Abs(endPoint.X - _rectangleStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(endPoint.Y - _rectangleStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            ResetRectangleSelectionState();
            e.Handled = true;
            return;
        }

        try
        {
            SelectByRectangle(list, _rectangleStartPoint, endPoint, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }
        catch (InvalidOperationException ex)
        {
            ViewModel.ShowWarning("Rectangle selection was skipped because the grid changed while selecting.");
            _ = CrashLogService.WriteCrashLogAsync(ex);
        }

        ResetRectangleSelectionState();
        e.Handled = true;
    }

    private void CancelRectangleSelection(string message, Exception exception)
    {
        ResetRectangleSelectionState();
        ViewModel.ShowWarning(message);
        _ = CrashLogService.WriteCrashLogAsync(exception);
    }

    private void UpdateRubberBand(System.Windows.Point endPoint)
    {
        if (_rubberBandAdorner is null)
        {
            return;
        }

        var clampedEnd = _rectangleList is null
            ? endPoint
            : ClampPointToList(_rectangleList, endPoint);
        _rubberBandAdorner.SelectionBounds = new Rect(_rectangleStartPoint, clampedEnd);
        _rubberBandAdorner.InvalidateVisual();
    }

    private void ResetRectangleSelectionState()
    {
        RemoveRubberBand();
        _isRectangleSelecting = false;
        if (_rectangleList?.IsMouseCaptured == true)
        {
            _rectangleList.ReleaseMouseCapture();
        }

        _rectangleList = null;
        _rectangleSnapshot = [];
    }

    private void RemoveRubberBand()
    {
        if (_rubberBandLayer is not null && _rubberBandAdorner is not null)
        {
            _rubberBandLayer.Remove(_rubberBandAdorner);
        }

        _rubberBandLayer = null;
        _rubberBandAdorner = null;
    }

    private void LibraryList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list)
        {
            return;
        }

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            list.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            list.SelectedItems.Clear();
            e.Handled = true;
        }
    }

    private void BucketList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        LibraryList_KeyDown(sender, e);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.A ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox)
        {
            return;
        }

        var list = BucketList.IsVisible ? BucketList : LibraryList;
        list.Focus();
        list.SelectAll();
        e.Handled = true;
    }

    private async void ThumbnailImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Image { DataContext: ImageItemViewModel item } image)
        {
            while (_isResizing && image.IsLoaded)
            {
                await Task.Delay(250);
            }

            if (!image.IsLoaded)
            {
                return;
            }

            await ViewModel.LoadThumbnailAsync(item);
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isRectangleSelecting)
        {
            ResetRectangleSelectionState();
        }

        _isResizing = true;
        _resizeQuietCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _resizeQuietCancellation = cancellation;
        _ = MarkResizeQuietAfterDelayAsync(cancellation);
    }

    private async Task MarkResizeQuietAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(250, cancellation.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!cancellation.IsCancellationRequested)
                {
                    _isResizing = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_resizeQuietCancellation, cancellation))
            {
                _resizeQuietCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void TagButton_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void TagButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagButtonViewModel tag })
        {
            ViewModel.ApplyTag(tag);
        }
        else if (sender is FrameworkElement { DataContext: BucketDefinitionViewModel bucket })
        {
            ViewModel.ApplyBucketCommand.Execute(bucket);
        }

        e.Handled = true;
    }

    private void ImageTile_RightClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindDataContext<ImageItemViewModel>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        SelectSingleItemForContext(item);
        _ = Dispatcher.BeginInvoke(() => OpenImageInspectorAsync(item), DispatcherPriority.Background);
        e.Handled = true;
    }

    private void ImageList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
        }
    }

    private void ImageList_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isRectangleSelecting && ReferenceEquals(sender, _rectangleList))
        {
            ResetRectangleSelectionState();
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRectangleSelecting)
        {
            ResetRectangleSelectionState();
        }
    }

    private void ShowSelectedInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedItem;
        if (selected is null)
        {
            ViewModel.ShowWarning("Select an image before opening File Explorer.");
            return;
        }

        if (!File.Exists(selected.FullPath))
        {
            ViewModel.ShowWarning("The selected file is unavailable or has been moved.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{selected.FullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ViewModel.ShowWarning($"Could not open File Explorer: {ex.Message}");
        }
    }

    private void HandleBucketSelectionAction(BucketSelectionAction action)
    {
        switch (action)
        {
            case BucketSelectionAction.Invert:
                InvertBucketSelection();
                break;
            case BucketSelectionAction.SelectUntagged:
                SelectUntaggedBucketItems();
                break;
        }
    }

    private void InvertBucketSelection()
    {
        var selected = BucketList.SelectedItems.Cast<ImageItemViewModel>().ToHashSet();
        BucketList.SelectedItems.Clear();
        foreach (var item in BucketList.Items.Cast<ImageItemViewModel>().Where(item => !selected.Contains(item)))
        {
            BucketList.SelectedItems.Add(item);
        }
    }

    private void SelectUntaggedBucketItems()
    {
        BucketList.SelectedItems.Clear();
        foreach (var item in BucketList.Items.Cast<ImageItemViewModel>().Where(ViewModel.IsMissingForCurrentBucketPass))
        {
            BucketList.SelectedItems.Add(item);
        }
    }

    private void SelectByRectangle(System.Windows.Controls.ListBox list, System.Windows.Point start, System.Windows.Point end, bool toggle)
    {
        var listBounds = new SelectionRectangle(0, 0, list.ActualWidth, list.ActualHeight);
        var selection = new SelectionRectangle(start.X, start.Y, end.X - start.X, end.Y - start.Y).ClampTo(listBounds);
        var selected = RectangleSelection.Intersecting(_rectangleSnapshot, selection);
        if (selected.Count == 0)
        {
            if (!toggle)
            {
                list.SelectedItems.Clear();
            }

            return;
        }

        if (!toggle)
        {
            list.SelectedItems.Clear();
        }

        foreach (var item in selected)
        {
            if (toggle && list.SelectedItems.Contains(item))
            {
                list.SelectedItems.Remove(item);
            }
            else if (!list.SelectedItems.Contains(item))
            {
                list.SelectedItems.Add(item);
            }
        }
    }

    private static IReadOnlyList<SelectionTile<ImageItemViewModel>> CaptureSelectionSnapshot(System.Windows.Controls.ListBox list)
    {
        var tiles = new List<SelectionTile<ImageItemViewModel>>();
        foreach (var item in list.Items.OfType<ImageItemViewModel>())
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
            {
                continue;
            }

            if (container.ActualWidth <= 0 || container.ActualHeight <= 0)
            {
                continue;
            }

            Rect bounds;
            try
            {
                bounds = container.TransformToAncestor(list).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                continue;
            }

            tiles.Add(new SelectionTile<ImageItemViewModel>(
                item,
                new SelectionRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
        }

        return tiles;
    }

    private static System.Windows.Point ClampPointToList(System.Windows.Controls.ListBox list, System.Windows.Point point)
    {
        var maxX = Math.Max(0, list.ActualWidth);
        var maxY = Math.Max(0, list.ActualHeight);
        return new System.Windows.Point(
            Math.Clamp(point.X, 0, maxX),
            Math.Clamp(point.Y, 0, maxY));
    }

    private void SelectSingleItemForContext(ImageItemViewModel item)
    {
        var list = BucketList.IsVisible && BucketList.Items.Contains(item)
            ? BucketList
            : LibraryList.Items.Contains(item)
                ? LibraryList
                : null;
        if (list is null || list.SelectedItems.Contains(item))
        {
            return;
        }

        list.SelectedItems.Clear();
        list.SelectedItems.Add(item);
        list.ScrollIntoView(item);
    }

    private async Task OpenImageInspectorAsync(ImageItemViewModel item)
    {
        try
        {
            var frames = await Task.Run(() => LoadInspectorFrames(item.FullPath));
            if (frames.Count == 0)
            {
                ViewModel.ShowWarning("Could not open image preview: no decodable image frames were found.");
                return;
            }

            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform();
            var transforms = new TransformGroup();
            transforms.Children.Add(scale);
            transforms.Children.Add(translate);

            var image = new System.Windows.Controls.Image
            {
                Source = frames[0],
                Stretch = Stretch.Uniform,
                RenderTransform = transforms,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };
            DispatcherTimer? animationTimer = null;
            var frameIndex = 0;
            var host = new Grid
            {
                Background = System.Windows.Media.Brushes.Black,
                ClipToBounds = true
            };
            host.Children.Add(image);

            var window = new Window
            {
                Owner = this,
                Title = item.FileName,
                Content = host,
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            window.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    window.Close();
                }
            };
            window.Closed += (_, _) => animationTimer?.Stop();
            if (frames.Count > 1)
            {
                animationTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                animationTimer.Tick += (_, _) =>
                {
                    frameIndex = (frameIndex + 1) % frames.Count;
                    image.Source = frames[frameIndex];
                };
                animationTimer.Start();
            }
            host.MouseRightButtonUp += (_, _) => window.Close();
            host.MouseWheel += (_, args) =>
            {
                var delta = args.Delta > 0 ? 1.12 : 0.9;
                var next = Math.Clamp(scale.ScaleX * delta, 0.25, 8);
                scale.ScaleX = next;
                scale.ScaleY = next;
            };
            host.MouseLeftButtonDown += (_, args) =>
            {
                _inspectorPanStart = args.GetPosition(host);
                _inspectorPanStartX = translate.X;
                _inspectorPanStartY = translate.Y;
                host.CaptureMouse();
            };
            host.MouseMove += (_, args) =>
            {
                if (_inspectorPanStart is null || args.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                var point = args.GetPosition(host);
                translate.X = _inspectorPanStartX + point.X - _inspectorPanStart.Value.X;
                translate.Y = _inspectorPanStartY + point.Y - _inspectorPanStart.Value.Y;
            };
            host.MouseLeftButtonUp += (_, _) =>
            {
                _inspectorPanStart = null;
                host.ReleaseMouseCapture();
            };

            window.ShowDialog();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            ViewModel.ShowWarning($"Could not open image preview: {ex.Message}");
        }
    }

    private static IReadOnlyList<ImageSource> LoadInspectorFrames(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frames = new List<ImageSource>();
        foreach (var frame in decoder.Frames)
        {
            frame.Freeze();
            frames.Add(frame);
        }

        return frames;
    }

    private static bool IsFromScrollBar(DependencyObject? source)
    {
        return FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static T? FindDataContext<T>(DependencyObject? source)
        where T : class
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: T match })
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}

internal sealed class RubberBandAdorner : Adorner
{
    private static readonly SolidColorBrush Fill = new(System.Windows.Media.Color.FromArgb(42, 180, 35, 24));
    private static readonly System.Windows.Media.Pen Stroke = new(new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 35, 24)), 1.5);

    public RubberBandAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    public Rect SelectionBounds { get; set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (SelectionBounds.Width == 0 && SelectionBounds.Height == 0)
        {
            return;
        }

        drawingContext.DrawRectangle(Fill, Stroke, SelectionBounds);
    }
}
