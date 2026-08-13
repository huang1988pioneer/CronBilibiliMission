using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using BilibiliCookieReader.ViewModels;

namespace BilibiliCookieReader.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Initialize(this);
    }

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel vm)
            return;
        vm.LoadCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer?.TryGetFiles();
        e.DragEffects = files?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var path = e.DataTransfer?.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        vm.LoadFromPath(path);
    }
}
