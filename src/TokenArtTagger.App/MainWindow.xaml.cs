using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TokenArtTagger.App.ViewModels;

namespace TokenArtTagger.App;

public partial class MainWindow : Window
{
    private System.Windows.Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void ThumbnailList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ViewModel.ReplaceSelection(ThumbnailList.SelectedItems.Cast<ImageItemViewModel>());
    }

    private void ThumbnailList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(ThumbnailList);
    }

    private void ThumbnailList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            ThumbnailList.SelectedItems.Count == 0 ||
            IsFromScrollBar(e.OriginalSource as DependencyObject) ||
            FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(ThumbnailList);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(ThumbnailList, "selected-images", System.Windows.DragDropEffects.Copy);
    }

    private async void ThumbnailImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Image { DataContext: ImageItemViewModel item })
        {
            await ViewModel.LoadThumbnailAsync(item);
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

        e.Handled = true;
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
}
