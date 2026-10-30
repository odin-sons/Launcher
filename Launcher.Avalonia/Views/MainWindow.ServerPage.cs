using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Odinsons.ValheimLauncher.Avalonia.Controls;

namespace Odinsons.ValheimLauncher.Avalonia.Views
{
    public partial class MainWindow
    {
        private ServerPage _page = ServerPage.Empty;
        private MeasuredFacts _measured = MeasuredFacts.None;
        private bool _hasAboutZone;
        private bool _hasNewsZone;

        internal bool HasAboutZone => _hasAboutZone;
        internal bool HasNewsZone => _hasNewsZone;
        internal FactsView AboutFactsView => AboutFacts;
        internal MarkdownView AboutDescriptionView => AboutDescription;
        internal MarkdownView NewsView => NewsContent;

        internal void ShowServerPage(ServerPage page)
        {
            _page = page;
            RenderServerPage();
        }

        internal void ShowMeasuredFacts(MeasuredFacts measured)
        {
            _measured = measured.WithOnline(_measured.Online, _measured.Capacity);
            RenderServerPage();
        }

        internal void ShowOnlineFact(int? online, int? capacity)
        {
            _measured = _measured.WithOnline(online, capacity);
            RenderServerPage();
        }

        private void RenderServerPage() => Dispatcher.UIThread.Invoke(() =>
        {
            if (_theme == null)
                return;

            string language = Loc.Language;
            AboutInfo about = _page.About ?? AboutInfo.Empty;
            var facts = ServerPage.Facts(_measured, about);
            var description = MarkdownSubset.Parse(about.DescriptionFor(language));
            var news = MarkdownSubset.Parse(_page.News);

            AboutFacts.Show(facts);
            AboutDescription.Show(description);
            NewsContent.Show(news);

            _hasAboutZone = facts.Count > 0 || description.Count > 0;
            _hasNewsZone = news.Count > 0;
            AboutZone.IsVisible = _hasAboutZone;

            string? tagline = about.TaglineFor(language);
            foreach (StepFrame frame in new[] { ServerFrame, ModsFrame, InstallFrame })
                frame.Art.Tagline = tagline;

            ArrangeServerZones(ServerFrame.Plan);
        });

        private async Task LoadServerPageAsync()
        {
            try
            {
                if (string.Equals(_profile.Id, ServerProfile.Default.Id, StringComparison.Ordinal))
                    await LoadBuiltInPageAsync();
                else
                    await LoadCommunityPageAsync();
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Could not load the About and News text", ex);
            }

            await LoadMeasuredFactsAsync();
        }

        private async Task LoadCommunityPageAsync()
        {
            ServerPage? page = await Task.Run(() => OfficialRegistry.LoadPageAsync(Environment.CurrentDirectory, _profile.Id));
            if (page != null)
                ShowServerPage(page);
        }

        private async Task LoadBuiltInPageAsync()
        {
            string? info = await ReadCachedTextAsync("info.md");
            string? news = await ReadCachedTextAsync("changelog.md");
            if (info != null || news != null)
                ShowServerPage(BuiltInPage(info, news));

            string? freshInfo = await FetchBuiltInTextAsync("info.md", DisplayFileLimits.About * 2);
            string? freshNews = await FetchBuiltInTextAsync("changelog.md", DisplayFileLimits.Changelog);
            if ((freshInfo ?? info) != info || (freshNews ?? news) != news)
                ShowServerPage(BuiltInPage(freshInfo ?? info, freshNews ?? news));
        }

        private ServerPage BuiltInPage(string? info, string? news)
        {
            _serverDescriptions.TryGetValue(SelectedServer, out string? tagline);
            return new ServerPage { About = AboutInfo.FromLegacy(tagline ?? string.Empty, info ?? string.Empty), News = news ?? string.Empty };
        }

        private async Task<string?> ReadCachedTextAsync(string name)
        {
            try
            {
                string path = Path.Combine(ClientFolder, name);
                return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Could not read the cached {name}", ex);
                return null;
            }
        }

        private async Task<string?> FetchBuiltInTextAsync(string name, int maxLength)
        {
            try
            {
                string text = await HttpClient.GetStringAsync(ActiveLauncherUrl + SelectedServer + "/" + name);
                if (text.Length > maxLength)
                    return null;
                await File.WriteAllTextAsync(Path.Combine(ClientFolder, name), text);
                return text;
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Could not refresh {name}", ex);
                return null;
            }
        }

        private async Task LoadMeasuredFactsAsync()
        {
            string? modpack = await FetchManifestTextAsync("update.info");
            string? game = await FetchManifestTextAsync(MeasuredFacts.GameManifestName);
            ShowMeasuredFacts(MeasuredFacts.FromManifests(modpack, game));
        }

        private async Task<string?> FetchManifestTextAsync(string fileName)
        {
            try
            {
                return await HttpClient.GetStringAsync(ActiveLauncherUrl + SelectedServer + "/" + fileName);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Could not load {fileName} for the About facts", ex);
                return null;
            }
        }
    }
}
