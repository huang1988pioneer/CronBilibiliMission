using System.Xml.Linq;
using Xunit;

namespace BilibiliCookieReader.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void Right_content_uses_one_uncapped_scroll_viewer_above_the_action_bar()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "BilibiliCookieReader", "Views", "MainWindow.axaml"));
        var document = XDocument.Load(path);

        var accountStatus = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && (string?)element.Attribute("Classes") == "accountstatus");
        var contentScrollViewer = accountStatus
            .Ancestors()
            .First(element => element.Name.LocalName == "ScrollViewer");

        Assert.Null(contentScrollViewer.Attribute("MaxHeight"));
        Assert.Contains(
            contentScrollViewer.Descendants(),
            element => element.Name.LocalName == "ItemsControl");
    }

    [Fact]
    public void Cookie_fields_use_a_non_virtualizing_vertical_stack_inside_the_outer_scroll_viewer()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "BilibiliCookieReader", "Views", "MainWindow.axaml"));
        var document = XDocument.Load(path);
        var fields = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && (string?)element.Attribute("ItemsSource") == "{Binding Fields}");

        Assert.Contains(
            fields.Descendants(),
            element => element.Name.LocalName == "ItemsPanelTemplate"
                && element.Descendants().Any(child => child.Name.LocalName == "StackPanel"));
    }

    [Fact]
    public void Cookie_field_content_occupies_three_distinct_grid_rows()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "BilibiliCookieReader", "Views", "MainWindow.axaml"));
        var document = XDocument.Load(path);
        var template = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DataTemplate"
                && (string?)element.Attribute(XName.Get("DataType", "http://schemas.microsoft.com/winfx/2006/xaml"))
                    == "vm:CookieFieldViewModel");
        var fieldGrid = template
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && (string?)element.Attribute("RowDefinitions") == "Auto,Auto,Auto");
        var children = fieldGrid.Elements().ToList();

        Assert.Equal("1", (string?)children[1].Attribute("Grid.Row"));
        Assert.Equal("2", (string?)children[2].Attribute("Grid.Row"));
    }
}
