using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Odinsons.ValheimLauncher.Avalonia.Theming;

namespace Odinsons.ValheimLauncher.Avalonia.Controls
{
    /// <summary>
    /// Draws the parsed Markdown subset. It only builds text, headings, bullets and https links, so a
    /// community's text cannot put an image, HTML or any other control on screen.
    /// </summary>
    internal sealed class MarkdownView : StackPanel
    {
        public Action<Uri> OpenLink { get; set; } = uri => Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });

        public int BlockCount => Children.Count;

        public void Show(IReadOnlyList<MarkdownBlock> blocks)
        {
            Children.Clear();
            foreach (MarkdownBlock block in blocks)
            {
                switch (block)
                {
                    case MarkdownHeading heading:
                        Children.Add(Text(heading.Spans, heading.Level == 2 ? 16 : 15, bold: true, LauncherBrushes.Text,
                            new Thickness(0, Children.Count == 0 ? 0 : heading.Level == 2 ? 18 : 12, 0, 4)));
                        break;
                    case MarkdownList list:
                        foreach (IReadOnlyList<MarkdownSpan> item in list.Items)
                        {
                            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 2) };
                            row.Children.Add(new TextBlock { Text = "•  ", FontSize = 14, Foreground = LauncherBrushes.Muted });
                            TextBlock body = Text(item, 14, bold: false, LauncherBrushes.Muted, new Thickness(0));
                            Grid.SetColumn(body, 1);
                            row.Children.Add(body);
                            Children.Add(row);
                        }
                        break;
                    case MarkdownParagraph paragraph:
                        Children.Add(Text(paragraph.Spans, 14, bold: false, LauncherBrushes.Muted, new Thickness(0, 0, 0, 8)));
                        break;
                }
            }
        }

        private TextBlock Text(IReadOnlyList<MarkdownSpan> spans, double size, bool bold, IBrush foreground, Thickness margin)
        {
            var block = new TextBlock
            {
                FontSize = size,
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap,
                Margin = margin,
                Inlines = new InlineCollection()
            };

            foreach (MarkdownSpan span in spans)
            {
                if (span.Link == null)
                {
                    var run = new Run(span.Text);
                    if (span.Bold)
                        run.FontWeight = FontWeight.Bold;
                    if (span.Italic)
                        run.FontStyle = FontStyle.Italic;
                    block.Inlines.Add(run);
                    continue;
                }

                MarkdownLink link = span.Link;
                var label = new TextBlock
                {
                    Text = $"{span.Text} ({link.Host})",
                    FontSize = size,
                    FontWeight = span.Bold || bold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = span.Italic ? FontStyle.Italic : FontStyle.Normal,
                    Foreground = LauncherBrushes.Accent,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Focusable = true
                };
                ToolTip.SetTip(label, link.Target.AbsoluteUri);
                label.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton == MouseButton.Left)
                        OpenLink(link.Target);
                };
                label.KeyDown += (_, e) =>
                {
                    if (e.Key is Key.Enter or Key.Space)
                        OpenLink(link.Target);
                };
                block.Inlines.Add(new InlineUIContainer(label));
            }
            return block;
        }
    }
}
