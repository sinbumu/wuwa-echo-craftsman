using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using WutheringWavesEchoCraftsman.Models;

namespace WutheringWavesEchoCraftsman.Views;

public partial class SubstatSettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _save;
    private readonly ObservableCollection<SubstatSettingRow> _rows = [];

    public SubstatSettingsWindow(AppConfig config, Action<AppConfig> save)
    {
        InitializeComponent();
        _config = config;
        _save = save;
        RequiredCountTextBox.Text = config.RequiredValidSubstatCount.ToString(CultureInfo.InvariantCulture);
        LoadRows();
        SubstatDataGrid.ItemsSource = _rows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadRows()
    {
        _rows.Clear();

        foreach (var stat in SubstatInfo.All)
        {
            var rule = _config.SubstatRules.FirstOrDefault(item => item.Key == stat.Key);
            _rows.Add(new SubstatSettingRow(
                stat.Key,
                stat.DisplayName,
                rule?.Enabled ?? false,
                rule?.Required ?? false));
        }
    }

    private void SaveSettings()
    {
        _config.RequiredValidSubstatCount = Math.Clamp(ParseInt(RequiredCountTextBox.Text, 2), 0, 5);
        _config.SubstatRules = _rows
            .Where(row => row.Enabled)
            .Select(row => new SubstatRule(row.Key, 0, true, row.Required))
            .ToList();

        _save(_config);
    }

    private void SubstatDataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var row in _rows)
            {
                row.NormalizeSelection();
            }
        });
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }
}

public sealed class SubstatSettingRow : INotifyPropertyChanged
{
    private bool _enabled;
    private bool _required;

    public SubstatSettingRow(string key, string displayName, bool enabled, bool required)
    {
        Key = key;
        DisplayName = displayName;
        _enabled = enabled || required;
        _required = required;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string DisplayName { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_required && !value)
            {
                value = true;
            }

            SetField(ref _enabled, value);
        }
    }

    public bool Required
    {
        get => _required;
        set
        {
            if (SetField(ref _required, value) && value)
            {
                Enabled = true;
            }
        }
    }

    public void NormalizeSelection()
    {
        if (Required && !Enabled)
        {
            Enabled = true;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
