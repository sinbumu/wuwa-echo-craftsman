using System.Windows;
using WutheringWavesEchoCraftsman.Services;

namespace WutheringWavesEchoCraftsman.Views;

public partial class HistoryWindow : Window
{
    private readonly DatabaseService _databaseService;

    public HistoryWindow(DatabaseService databaseService)
    {
        InitializeComponent();
        _databaseService = databaseService;
    }

    public async Task LoadRecordsAsync()
    {
        HistoryDataGrid.ItemsSource = await _databaseService.GetRecentResultsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadRecordsAsync();
    }
}
