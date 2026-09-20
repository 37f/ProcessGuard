using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ProcessGuard.App.ViewModels;
namespace ProcessGuard.App.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly string logDirectory;
    public MainWindow(MainViewModel viewModel, string logs) { InitializeComponent(); vm = viewModel; logDirectory = logs; DataContext = vm; }
    private void AddWhite(object sender, RoutedEventArgs e) { vm.AddWhitelist(WhiteInput.Text); if (vm.Error.Length == 0) WhiteInput.Clear(); }
    private void AddBlack(object sender, RoutedEventArgs e) { vm.AddBlacklist(BlackInput.Text); if (vm.Error.Length == 0) BlackInput.Clear(); }
    private void RemoveWhite(object sender, RoutedEventArgs e) { if (WhiteList.SelectedItem is string value) vm.RemoveWhitelist(value); }
    private void RemoveBlack(object sender, RoutedEventArgs e) { if (BlackList.SelectedItem is string value) vm.RemoveBlacklist(value); }
    private void OpenLogs(object sender, RoutedEventArgs e) {
        try { Directory.CreateDirectory(logDirectory); Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { vm.Error = "无法打开日志目录：" + ex.Message; }
    }
    private void OnSort(object sender, DataGridSortingEventArgs e) {
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in ProcessGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction; vm.Sort(e.Column.SortMemberPath, direction);
    }
}
