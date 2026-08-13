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
        Closed += OnClosed;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Initialize(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.StopBackgroundServices();
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
        var canDrop = files?.Any() == true;
        e.DragEffects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Classes.Set("active", canDrop);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) =>
        DropZone.Classes.Set("active", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Set("active", false);
        if (DataContext is not MainViewModel vm)
            return;

        var path = e.DataTransfer?.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        vm.LoadFromPath(path);
    }
}
