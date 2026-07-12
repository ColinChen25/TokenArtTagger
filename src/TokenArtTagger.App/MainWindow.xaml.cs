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
    private const string DragImageItemsFormat = "TokenArtTagger.ImageItems";
    private System.Windows.Point _dragStartPoint;
    private IReadOnlyList<ImageItemViewModel> _dragSelectionSnapshot = [];
    private System.Windows.Point _rectangleStartPoint;
    private System.Windows.Controls.ListBox? _rectangleList;
    private UIElement? _rectangleCaptureElement;
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
        ViewModel.DebugLog = App.DebugLog;
        ViewModel.RequestBucketSelectionAction += HandleBucketSelectionAction;
        App.DebugLog.Write("MainWindowConstructed", CurrentMode(), SelectedSummary());
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        using var logScope = App.DebugLog.Enter("SelectionChanged", "Library", SelectedSummary(LibraryList), new Dictionary<string, string?>
        {
            ["added"] = e.AddedItems.Count.ToString(),
            ["removed"] = e.RemovedItems.Count.ToString()
        });
        ViewModel.ReplaceSelection(LibraryList.SelectedItems.Cast<ImageItemViewModel>());
    }

    private void BucketList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        using var logScope = App.DebugLog.Enter("SelectionChanged", "Bucket", SelectedSummary(BucketList), new Dictionary<string, string?>
        {
            ["added"] = e.AddedItems.Count.ToString(),
            ["removed"] = e.RemovedItems.Count.ToString()
        });
        ViewModel.ReplaceBucketSelection(BucketList.SelectedItems.Cast<ImageItemViewModel>());
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        App.DebugLog.Write("MainWindowLoaded", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["logFile"] = App.DebugLog.LogFilePath
        });
        App.DebugLog.Write(BucketList.IsVisible ? "BucketModeActivated" : "LibraryModeActivated", CurrentMode(), SelectedSummary());
        ViewModel.ShowInfo($"Debug log: {App.DebugLog.LogFilePath}");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        App.DebugLog.Write("MainWindowClosing", CurrentMode(), SelectedSummary());
    }

    private void ImageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        using var logScope = App.DebugLog.Enter("PreviewMouseLeftButtonDown", ListMode(sender), SelectedSummary(sender as System.Windows.Controls.ListBox), new Dictionary<string, string?>
        {
            ["source"] = e.OriginalSource?.GetType().Name,
            ["isScrollBar"] = IsFromScrollBar(e.OriginalSource as DependencyObject).ToString(),
            ["isTile"] = (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null).ToString()
        });
        if (sender is System.Windows.Controls.ListBox list)
        {
            list.Focus();
            _dragStartPoint = e.GetPosition(list);
            _dragSelectionSnapshot = [];
            if (IsFromScrollBar(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } tile)
            {
                if (tile.DataContext is ImageItemViewModel origin)
                {
                    _dragSelectionSnapshot = DragSelection.PayloadForDragStart(
                        list.SelectedItems.Cast<ImageItemViewModel>(),
                        origin,
                        tile.IsSelected);

                    if (tile.IsSelected && Keyboard.Modifiers == ModifierKeys.None)
                    {
                        e.Handled = true;
                    }
                }

                return;
            }

            BeginRectangleSelection(list, _dragStartPoint, list);
            e.Handled = true;
        }
    }

    private void ImageSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<System.Windows.Controls.ListBox>(source) is not null ||
            IsFromInteractiveControl(source))
        {
            return;
        }

        var list = ReferenceEquals(sender, BucketImageSurface) ? BucketList : LibraryList;
        using var logScope = App.DebugLog.Enter("ImageSurfacePreviewMouseLeftButtonDown", ListMode(list), SelectedSummary(list), new Dictionary<string, string?>
        {
            ["source"] = e.OriginalSource?.GetType().Name
        });
        list.Focus();
        _dragStartPoint = e.GetPosition(list);
        _dragSelectionSnapshot = [];
        BeginRectangleSelection(list, _dragStartPoint, (UIElement)sender);
        e.Handled = true;
    }

    private void BeginRectangleSelection(System.Windows.Controls.ListBox list, System.Windows.Point startPoint, UIElement captureElement)
    {
        _rectangleList = list;
        _rectangleCaptureElement = captureElement;
        _rectangleStartPoint = startPoint;
        _isRectangleSelecting = true;
        _rubberBandLayer = AdornerLayer.GetAdornerLayer(list);
        _rubberBandAdorner = new RubberBandAdorner(list);
        _rubberBandLayer?.Add(_rubberBandAdorner);
        captureElement.CaptureMouse();
        App.DebugLog.Write("RectangleSelectionStart", ListMode(list), SelectedSummary(list));
    }

    private void ImageList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isRectangleSelecting && e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        using var logScope = App.DebugLog.Enter("ImageListMouseMove", ListMode(sender), SelectedSummary(sender as System.Windows.Controls.ListBox), new Dictionary<string, string?>
        {
            ["leftButton"] = e.LeftButton.ToString(),
            ["isRectangleSelecting"] = _isRectangleSelecting.ToString()
        });
        if (_isRectangleSelecting && _rectangleList is not null)
        {
            try
            {
                UpdateRubberBand(e.GetPosition(_rectangleList));
            }
            catch (InvalidOperationException ex)
            {
                CancelRectangleSelection("grid layout changed", ex, showWarning: true);
            }

            return;
        }

        if (sender is not System.Windows.Controls.ListBox list ||
            e.LeftButton != MouseButtonState.Pressed ||
            IsFromScrollBar(e.OriginalSource as DependencyObject) ||
            FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        var payload = _dragSelectionSnapshot.Count > 0
            ? _dragSelectionSnapshot
            : list.SelectedItems.Cast<ImageItemViewModel>().ToList();
        if (payload.Count == 0)
        {
            return;
        }

        var currentPoint = e.GetPosition(list);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new System.Windows.DataObject();
        data.SetData(DragImageItemsFormat, payload);
        data.SetData(System.Windows.DataFormats.StringFormat, "selected-images");
        DragDrop.DoDragDrop(list, data, System.Windows.DragDropEffects.Copy);
        _dragSelectionSnapshot = [];
    }

    private void ImageList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        using var logScope = App.DebugLog.Enter("PreviewMouseLeftButtonUp", ListMode(sender), SelectedSummary(sender as System.Windows.Controls.ListBox), new Dictionary<string, string?>
        {
            ["isRectangleSelecting"] = _isRectangleSelecting.ToString()
        });
        if (!_isRectangleSelecting || _rectangleList is null)
        {
            _dragSelectionSnapshot = [];
            return;
        }

        var list = _rectangleList;
        System.Windows.Point endPoint;
        try
        {
            endPoint = e.GetPosition(list);
        }
        catch (InvalidOperationException ex)
        {
            CancelRectangleSelection("grid layout changed", ex, showWarning: true);
            e.Handled = true;
            return;
        }

        EndRectangleSelection(list, endPoint);
        e.Handled = true;
    }

    private void ImageSurface_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isRectangleSelecting ||
            _rectangleList is null ||
            !ReferenceEquals(sender, _rectangleCaptureElement))
        {
            return;
        }

        try
        {
            UpdateRubberBand(e.GetPosition(_rectangleList));
        }
        catch (InvalidOperationException ex)
        {
            CancelRectangleSelection("grid layout changed", ex, showWarning: true);
        }

        e.Handled = true;
    }

    private void ImageSurface_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRectangleSelecting ||
            _rectangleList is null ||
            !ReferenceEquals(sender, _rectangleCaptureElement))
        {
            return;
        }

        System.Windows.Point endPoint;
        try
        {
            endPoint = e.GetPosition(_rectangleList);
        }
        catch (InvalidOperationException ex)
        {
            CancelRectangleSelection("grid layout changed", ex, showWarning: true);
            e.Handled = true;
            return;
        }

        EndRectangleSelection(_rectangleList, endPoint);
        e.Handled = true;
    }

    private void ImageSurface_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isRectangleSelecting && ReferenceEquals(sender, _rectangleCaptureElement))
        {
            CancelRectangleSelection("pointer left image surface");
        }
    }

    private void ImageList_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isRectangleSelecting && ReferenceEquals(sender, _rectangleCaptureElement))
        {
            CancelRectangleSelection("pointer left grid");
        }
    }

    private void ImageList_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isRectangleSelecting && ReferenceEquals(sender, _rectangleCaptureElement))
        {
            CancelRectangleSelection("mouse capture lost");
        }
    }

    private void ImageSurface_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isRectangleSelecting && ReferenceEquals(sender, _rectangleCaptureElement))
        {
            CancelRectangleSelection("mouse capture lost");
        }
    }

    private void EndRectangleSelection(System.Windows.Controls.ListBox list, System.Windows.Point endPoint)
    {
        RemoveRubberBand();
        var captureElement = _rectangleCaptureElement;
        _isRectangleSelecting = false;
        _rectangleList = null;
        _rectangleCaptureElement = null;
        captureElement?.ReleaseMouseCapture();

        if (Math.Abs(endPoint.X - _rectangleStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(endPoint.Y - _rectangleStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            SelectByRectangle(list, _rectangleStartPoint, endPoint, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            App.DebugLog.Write("RectangleSelectionEnd", ListMode(list), SelectedSummary(list));
        }
        catch (InvalidOperationException ex)
        {
            ViewModel.ShowWarning("Rectangle selection was skipped because the grid changed while selecting.");
            _ = CrashLogService.WriteCrashLogAsync(ex);
        }
    }

    private void CancelRectangleSelection(string reason, Exception? exception = null, bool showWarning = false)
    {
        App.DebugLog.Write("RectangleSelectionCancel", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["reason"] = reason,
            ["exception"] = exception?.GetType().Name
        });
        RemoveRubberBand();
        _isRectangleSelecting = false;
        _rectangleCaptureElement?.ReleaseMouseCapture();
        _rectangleList = null;
        _rectangleCaptureElement = null;
        if (showWarning)
        {
            ViewModel.ShowWarning("Rectangle selection was canceled because the grid layout changed.");
        }

        if (exception is not null)
        {
            _ = CrashLogService.WriteCrashLogAsync(exception);
        }
    }

    private void UpdateRubberBand(System.Windows.Point endPoint)
    {
        if (_rubberBandAdorner is null)
        {
            return;
        }

        var rectangle = RectangleFromDrag(_rectangleStartPoint, endPoint, _rectangleList);
        if (!rectangle.IsValid)
        {
            _rubberBandAdorner.SelectionBounds = Rect.Empty;
            _rubberBandAdorner.InvalidateVisual();
            return;
        }

        _rubberBandAdorner.SelectionBounds = new Rect(
            new System.Windows.Point(rectangle.Left, rectangle.Top),
            new System.Windows.Point(rectangle.Right, rectangle.Bottom));
        _rubberBandAdorner.InvalidateVisual();
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
        using var logScope = App.DebugLog.Enter("WindowPreviewKeyDown", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["key"] = e.Key.ToString(),
            ["modifiers"] = Keyboard.Modifiers.ToString()
        });
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox)
        {
            return;
        }

        if (BucketList.IsVisible &&
            Keyboard.Modifiers == ModifierKeys.None &&
            KeyToDigit(e.Key) is { } digit)
        {
            ViewModel.ApplyBucketShortcut(digit);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.A ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var list = BucketList.IsVisible ? BucketList : LibraryList;
        list.Focus();
        list.SelectAll();
        e.Handled = true;
    }

    private static int? KeyToDigit(Key key)
    {
        return key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            Key.D9 or Key.NumPad9 => 9,
            _ => null
        };
    }

    private async void ThumbnailImage_Loaded(object sender, RoutedEventArgs e)
    {
        var loadedItem = (sender as FrameworkElement)?.DataContext as ImageItemViewModel;
        using var logScope = App.DebugLog.Enter("ThumbnailImageLoaded", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["file"] = loadedItem?.FileName
        });
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
        using var logScope = App.DebugLog.Enter("WindowSizeChanged", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["height"] = ActualHeight.ToString("F0"),
            ["state"] = WindowState.ToString(),
            ["width"] = ActualWidth.ToString("F0")
        });
        if (_isRectangleSelecting)
        {
            CancelRectangleSelection("window resized");
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
            || e.Data.GetDataPresent(DragImageItemsFormat)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void TagButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var draggedItems = DraggedItemsFromData(e.Data);
        if (sender is FrameworkElement { DataContext: TagButtonViewModel tag })
        {
            ViewModel.ApplyTag(tag, draggedItems);
        }
        else if (sender is FrameworkElement { DataContext: BucketDefinitionViewModel bucket })
        {
            ViewModel.ApplyBucket(bucket, draggedItems);
        }

        _dragSelectionSnapshot = [];
        e.Handled = true;
    }

    private void ImageTile_RightClick(object sender, MouseButtonEventArgs e)
    {
        using var logScope = App.DebugLog.Enter("ImageTileRightClick", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["source"] = e.OriginalSource?.GetType().Name
        });
        var item = FindDataContext<ImageItemViewModel>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        OpenImageInspector(item);
        e.Handled = true;
    }

    private void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        App.DebugLog.Write("ScanButtonClicked", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["folderName"] = DebugEventLogger.SafeNameFromPath(ViewModel.FolderPath)
        });
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        using var logScope = App.DebugLog.Enter("OpenLogsFolder", CurrentMode(), SelectedSummary());
        try
        {
            Directory.CreateDirectory(DebugEventLogger.DefaultLogFolder());
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{DebugEventLogger.DefaultLogFolder()}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            ViewModel.ShowWarning($"Could not open logs folder: {ex.Message}");
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!e.AddedItems.OfType<TabItem>().Any() && !e.RemovedItems.OfType<TabItem>().Any())
        {
            return;
        }

        using var logScope = App.DebugLog.Enter("TabSelectionChanged", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["added"] = string.Join(",", e.AddedItems.OfType<TabItem>().Select(item => item.Header?.ToString())),
            ["removed"] = string.Join(",", e.RemovedItems.OfType<TabItem>().Select(item => item.Header?.ToString()))
        });
        if (_isRectangleSelecting)
        {
            CancelRectangleSelection("tab switched");
        }

        var bucketModeActive = e.AddedItems.OfType<TabItem>().Any(item => string.Equals(item.Header?.ToString(), "Bucket Tagging", StringComparison.Ordinal));
        ViewModel.SetBucketModeActive(bucketModeActive);
        App.DebugLog.Write(bucketModeActive ? "BucketModeActivated" : "LibraryModeActivated", CurrentMode(), SelectedSummary());
    }

    private void ShowSelectedInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var selected = (sender as FrameworkElement)?.DataContext as ImageItemViewModel
            ?? ViewModel.SelectedItem
            ?? ViewModel.BucketSelectedItem;
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
        var selection = RectangleFromDrag(start, end, list);
        if (!selection.IsValid)
        {
            return;
        }

        var tiles = new List<SelectionTile<ImageItemViewModel>>();
        foreach (var item in list.Items.Cast<ImageItemViewModel>())
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
            {
                continue;
            }

            Rect bounds;
            try
            {
                bounds = container.TransformToAncestor(list).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            tiles.Add(new SelectionTile<ImageItemViewModel>(
                item,
                new SelectionRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
        }

        var selected = RectangleSelection.Intersecting(tiles, selection);
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

    private static SelectionRectangle RectangleFromDrag(
        System.Windows.Point start,
        System.Windows.Point end,
        FrameworkElement? boundsElement)
    {
        var selection = new SelectionRectangle(start.X, start.Y, end.X - start.X, end.Y - start.Y);
        if (boundsElement is null)
        {
            return selection;
        }

        var bounds = new SelectionRectangle(0, 0, boundsElement.ActualWidth, boundsElement.ActualHeight);
        return selection.ClampTo(bounds);
    }

    private void OpenImageInspector(ImageItemViewModel item)
    {
        using var logScope = App.DebugLog.Enter("OpenImageInspector", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
        {
            ["file"] = item.FileName
        });
        try
        {
            App.DebugLog.Write("DetailImageLoad.enter", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
            {
                ["file"] = item.FileName
            });
            var frames = LoadInspectorFrames(item.FullPath);
            App.DebugLog.Write("DetailImageLoad.exit", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
            {
                ["file"] = item.FileName,
                ["frames"] = frames.Count.ToString()
            });
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            App.DebugLog.Write("OpenImageInspector.error", CurrentMode(), SelectedSummary(), new Dictionary<string, string?>
            {
                ["file"] = item.FileName,
                ["message"] = ex.Message,
                ["type"] = ex.GetType().Name
            });
            ViewModel.ShowWarning($"Could not open image preview: {ex.Message}");
        }
    }

    private string CurrentMode()
    {
        return BucketList.IsVisible ? "Bucket" : "Library";
    }

    private static string ListMode(object? sender)
    {
        return sender is System.Windows.Controls.ListBox { Name: "BucketList" } ? "Bucket" : "Library";
    }

    private string SelectedSummary()
    {
        return CurrentMode() == "Bucket" ? SelectedSummary(BucketList) : SelectedSummary(LibraryList);
    }

    private static string SelectedSummary(System.Windows.Controls.ListBox? list)
    {
        if (list is null)
        {
            return "-";
        }

        var last = list.SelectedItems.OfType<ImageItemViewModel>().LastOrDefault();
        return last is null
            ? $"count={list.SelectedItems.Count};file=-"
            : $"count={list.SelectedItems.Count};file={last.FileName}";
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

    private static IReadOnlyList<ImageItemViewModel>? DraggedItemsFromData(System.Windows.IDataObject data)
    {
        return data.GetDataPresent(DragImageItemsFormat)
            ? data.GetData(DragImageItemsFormat) as IReadOnlyList<ImageItemViewModel>
            : null;
    }

    private static bool IsFromScrollBar(DependencyObject? source)
    {
        return FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null;
    }

    private static bool IsFromInteractiveControl(DependencyObject? source)
    {
        return FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
            FindAncestor<System.Windows.Controls.Primitives.TextBoxBase>(source) is not null ||
            FindAncestor<System.Windows.Controls.Primitives.Selector>(source) is not null ||
            FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null;
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
