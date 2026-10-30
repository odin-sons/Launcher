using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Odinsons.ValheimLauncher;
using Odinsons.ValheimLauncher.Avalonia.Controls;
using Odinsons.ValheimLauncher.Avalonia.Theming;
using Odinsons.ValheimLauncher.Avalonia.Views;
using Xunit;

namespace Launcher.Avalonia.Tests
{
    internal static class ServerPageSamples
    {
        public static AboutInfo About(string description = "A long-running server.\n\n- Weekly events", string tagline = "Hardcore survival") => new()
        {
            Tagline = new Dictionary<string, string> { ["en"] = tagline },
            Description = new Dictionary<string, string> { ["en"] = description },
            Facts = new[] { new KeyValuePair<AboutFact, string>(AboutFact.Pvp, "off") }
        };

        public static ServerPage Full { get; } = new() { About = About(), News = "## 1.0\n\n- first release" };
    }

    /// <summary>The About and News zones of the Server step on a real window, built headless.</summary>
    [Collection("WorkingDirectory")]
    public sealed class ServerPageTests : IDisposable
    {
        private static readonly ServerProfile Community = new("community", "Community", new[] { "https://mirror.test/" });

        private readonly string _previousDirectory = Environment.CurrentDirectory;
        private readonly string _root = Directory.CreateTempSubdirectory("odinsons-serverpage-").FullName;
        private readonly string _language = Loc.Language;

        public ServerPageTests()
        {
            Environment.CurrentDirectory = _root;
            Loc.Use("en");
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = _previousDirectory;
            Loc.Use(_language);
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private static MainWindow Open()
        {
            var window = new MainWindow(Community);
            window.Show();
            window.UseTheme(new LoadedTheme
            {
                Spec = ThemeSpec.Default,
                Images = new Dictionary<string, byte[]>(),
                Problems = Array.Empty<string>()
            });
            window.UpdateLayout();
            return window;
        }

        private static void Show(MainWindow window, ServerPage page)
        {
            window.ShowServerPage(page);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        private static IEnumerable<string> TextsOf(Control root) =>
            root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Concat((t.Inlines ?? new InlineCollection()).OfType<Run>().Select(r => r.Text)));

        private static MarkdownView ViewOf(string markdown, Action<Uri>? open = null)
        {
            var view = new MarkdownView();
            if (open != null)
                view.OpenLink = open;
            view.Show(MarkdownSubset.Parse(markdown));
            return view;
        }

        [AvaloniaFact]
        public void WithNothingToShow_BothZonesCollapse()
        {
            MainWindow window = Open();

            Show(window, ServerPage.Empty);

            Assert.False(window.AboutZone.IsVisible);
            Assert.False(window.NewsZone.IsVisible);
            Assert.False(window.HasAboutZone);
            Assert.False(window.HasNewsZone);
        }

        [AvaloniaFact]
        public void ADescriptionAlone_ShowsAbout_AndNoNews()
        {
            MainWindow window = Open();

            Show(window, new ServerPage { About = ServerPageSamples.About(), News = string.Empty });

            Assert.True(window.AboutZone.IsVisible);
            Assert.False(window.NewsZone.IsVisible);
            Assert.Contains(TextsOf(window.AboutDescriptionView), t => t.Contains("A long-running server."));
        }

        [AvaloniaFact]
        public void NewsAlone_ShowsNews_AndNoAbout()
        {
            MainWindow window = Open();

            Show(window, new ServerPage { About = AboutInfo.Empty, News = "- fixed a crash" });

            Assert.False(window.AboutZone.IsVisible);
            Assert.True(window.NewsZone.IsVisible);
        }

        [AvaloniaFact]
        public void ATemplateWithoutNews_KeepsTheNewsZoneHidden()
        {
            MainWindow window = Open();
            window.UseTheme(new LoadedTheme
            {
                Spec = new ThemeSpec
                {
                    Layout = LayoutTemplate.Minimal,
                    Online = OnlineSide.Left,
                    Font = ThemeSpec.DefaultFont,
                    ButtonRadius = 6,
                    ButtonBorderWidth = 0,
                    Dark = ThemeSpec.DefaultScheme(ColorSchemeKind.Dark),
                    Light = ThemeSpec.DefaultScheme(ColorSchemeKind.Light)
                },
                Images = new Dictionary<string, byte[]>(),
                Problems = Array.Empty<string>()
            });

            Show(window, ServerPageSamples.Full);

            Assert.True(window.AboutZone.IsVisible);
            Assert.False(window.NewsZone.IsVisible);
        }

        [AvaloniaFact]
        public void TheTagline_IsDrawnOnTheArt()
        {
            MainWindow window = Open();

            Show(window, ServerPageSamples.Full);

            Assert.Equal("Hardcore survival", window.ServerStepFrame.Art.Tagline);

            Show(window, ServerPage.Empty);

            Assert.Null(window.ServerStepFrame.Art.Tagline);
        }

        [AvaloniaFact]
        public void MeasuredFacts_AreListedFirst_AndMarked()
        {
            MainWindow window = Open();
            window.ShowMeasuredFacts(new MeasuredFacts("1_2", 12, 2048, null, null));
            Show(window, ServerPageSamples.Full);

            string[] texts = TextsOf(window.AboutFactsView).ToArray();

            Assert.Equal(4, window.AboutFactsView.FactCount);
            Assert.True(Array.IndexOf(texts, "Game version") < Array.IndexOf(texts, "PvP"));
            Assert.Equal(3, texts.Count(t => t == "◆"));
        }

        [AvaloniaFact]
        public void TheOnlineFact_FollowsTheServerQuery()
        {
            MainWindow window = Open();
            Show(window, ServerPage.Empty);
            Assert.False(window.AboutZone.IsVisible);

            window.ShowOnlineFact(4, 20);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.AboutZone.IsVisible);
            Assert.Contains(TextsOf(window.AboutFactsView), t => t == "4 / 20");
        }

        [AvaloniaFact]
        public void TheLabels_FollowTheInterfaceLanguage()
        {
            MainWindow window = Open();
            window.ShowMeasuredFacts(new MeasuredFacts("1_2", null, null, null, null));
            Loc.Use("ru");
            Show(window, ServerPage.Empty);

            Assert.Contains("Версия игры", TextsOf(window.AboutFactsView));
        }

        [AvaloniaFact]
        public void Markdown_NeverDrawsImagesOrHtml()
        {
            MarkdownView view = ViewOf("![logo](https://x.test/a.png)\n\n<img src=\"https://x.test/b.png\"> <b>bold</b>\n\n<script>alert(1)</script>");

            Assert.Empty(view.GetVisualDescendants().OfType<global::Avalonia.Controls.Image>());
            string all = string.Join("\n", TextsOf(view)) + string.Concat(view.GetVisualDescendants().OfType<TextBlock>()
                .SelectMany(t => t.Inlines?.OfType<Run>() ?? Enumerable.Empty<Run>()).Select(r => r.Text));
            Assert.Contains("<b>bold</b>", all);
            Assert.Contains("<script>", all);
            Assert.Contains("![logo]", all);
        }

        [AvaloniaFact]
        public void ALink_ShowsItsHostFirst_AndOpensOnlyItsHttpsTarget()
        {
            var opened = new List<Uri>();
            MarkdownView view = ViewOf("Read the [rules](https://rules.example.test/page) and [bad](javascript:alert(1)).", opened.Add);
            var window = new Window { Content = view };
            window.Show();

            TextBlock link = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "rules (rules.example.test)");
            link.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

            Assert.Equal(new Uri("https://rules.example.test/page"), Assert.Single(opened));
            Assert.Equal(1, view.GetVisualDescendants().OfType<TextBlock>().Count(t => t.TextDecorations != null && t.TextDecorations.Count > 0));
            Assert.Equal("https://rules.example.test/page", ToolTip.GetTip(link));
        }

        [AvaloniaFact]
        public void Markdown_DrawsHeadingsAndBullets()
        {
            MarkdownView view = ViewOf("## Title\n\n- one\n- two\n\nplain");

            Assert.Equal(4, view.BlockCount);
            Assert.Contains(TextsOf(view), t => t == "Title");
            Assert.Equal(2, TextsOf(view).Count(t => t == "•  "));
        }
    }
}
