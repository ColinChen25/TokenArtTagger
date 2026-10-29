using System.Windows;
using TokenArtTagger.App.ViewModels;

namespace TokenArtTagger.App;

public partial class AiReviewWindow : Window
{
    public AiReviewWindow(AiReviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    private AiReviewViewModel ViewModel => (AiReviewViewModel)DataContext;
    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    { if (ViewModel.Busy) e.Cancel = true; }
    private void Window_Closed(object? sender, EventArgs e) => ViewModel.Close();
}
