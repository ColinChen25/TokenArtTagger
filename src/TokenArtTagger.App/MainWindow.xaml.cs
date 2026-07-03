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
            _dragStartPoint = e.GetPosition(list);
        }
    }

    private void ImageList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
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
        else if (sender is FrameworkElement { DataContext: BucketDefinitionViewModel bucket })
        {
            ViewModel.ApplyBucketCommand.Execute(bucket);
        }

        e.Handled = true;
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
