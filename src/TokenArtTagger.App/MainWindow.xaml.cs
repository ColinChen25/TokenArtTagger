using System.Windows;
using System.Windows.Input;
using TokenArtTagger.App.ViewModels;

namespace TokenArtTagger.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void ThumbnailList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ViewModel.ReplaceSelection(ThumbnailList.SelectedItems.Cast<ImageItemViewModel>());
    }

    private void ThumbnailList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ThumbnailList.SelectedItems.Count == 0)
        {
            return;
        }

        DragDrop.DoDragDrop(ThumbnailList, "selected-images", System.Windows.DragDropEffects.Copy);
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
}
