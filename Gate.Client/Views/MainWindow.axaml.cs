using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Gate.Client.ViewModels;

namespace Gate.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CopyToClipboardHandler = async text =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                }
            };

            vm.BrowseFileHandler = async () =>
            {
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storage != null)
                {
                    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Выберите исполняемый файл Gate.Server",
                        AllowMultiple = false
                    });
                    if (files.Count > 0)
                    {
                        return files[0].Path.LocalPath;
                    }
                }
                return null;
            };
        }
    }
}
