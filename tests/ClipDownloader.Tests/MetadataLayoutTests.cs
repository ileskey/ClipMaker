using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipDownloader.Application.Models;
using ClipDownloader.ViewModels;
using ClipDownloader.Views;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class MetadataLayoutTests
{
    [Theory]
    [InlineData(96)] [InlineData(144)] [InlineData(192)]
    public Task MinimumWindowLongTextAndWpfRenderAtTargetDpi(int dpi) => DispatcherTest.Run(async () =>
    {
        var s = new TestServices(); var original = s.Info;
        s.Info = new(original.Source, string.Concat(Enumerable.Repeat("한글 긴 제목 😀 줄바꿈\n", 80)), original.Duration,
            new string('채', 1000), null, null, original.State, original.Formats, [new("duration", MetadataIssueReason.Rounded)]);
        await s.ReadyAsync();
        var window = new MainWindow(s.Vm) { Width = 620, Height = 480 };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "정보 확인 사항")).IsExpanded = true;
            window.UpdateLayout();
            var scroll = Assert.IsType<ScrollViewer>(window.Content);
            var fields = Descendants(window).OfType<TextBox>().Where(t => AutomationProperties.GetAutomationId(t).StartsWith("Metadata", StringComparison.Ordinal)).ToArray();
            Assert.Equal(6, fields.Length);
            foreach (var field in fields)
            {
                Assert.True(field.IsReadOnly); Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(field)));
                Assert.InRange(field.ActualWidth, 1, scroll.ViewportWidth);
                Assert.False(System.Windows.Data.BindingOperations.IsDataBound(field, TextBox.TextProperty) && field.GetBindingExpression(TextBox.TextProperty)!.HasError);
            }
            var title = fields.Single(t => AutomationProperties.GetAutomationId(t) == "MetadataTitle");
            Assert.Equal(s.Info.Title, title.Text); Assert.InRange(title.ActualHeight, 1, 110); title.SelectAll(); Assert.Equal(title.Text, title.SelectedText);
            var uploader = fields.Single(t => AutomationProperties.GetAutomationId(t) == "MetadataUploader");
            Assert.True(title.IsTabStop); Assert.True(uploader.IsTabStop); Assert.True(scroll.ScrollableHeight > 0);
            foreach (var position in new[] { 0d, 300d, scroll.ScrollableHeight })
            {
                scroll.ScrollToVerticalOffset(position); window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(scroll.ActualWidth * dpi / 96d),
                    (int)Math.Ceiling(scroll.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    var bounds = new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight);
                    drawing.DrawRectangle(Brushes.White, null, bounds);
                    drawing.DrawRectangle(new VisualBrush(scroll)
                    {
                        ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
                        ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds
                    }, null, bounds);
                }
                bitmap.Render(visual);
                Assert.True(bitmap.PixelWidth > 0);
                // Render artifacts live beside ignored test binaries, never in tracked evidence.
                string directory = Path.Combine(AppContext.BaseDirectory, "phase3-layout"); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "observed-window-dpi.txt"), VisualTreeHelper.GetDpi(window).PixelsPerInchX.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, $"minimum-{dpi}-{position:0}.png")); encoder.Save(output);
            }
        }
        finally
        {
            window.Close(); await s.Vm.ShutdownAsync();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
