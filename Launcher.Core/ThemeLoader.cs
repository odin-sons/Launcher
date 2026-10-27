using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    /// <summary>A theme whose every picture has passed the hash and the limits; what did not is gone from the spec.</summary>
    public sealed class LoadedTheme
    {
        public ThemeSpec Spec { get; init; }

        /// <summary>The verified bytes of each picture the spec still names, by path.</summary>
        public IReadOnlyDictionary<string, byte[]> Images { get; init; }

        public IReadOnlyList<string> Problems { get; init; }
    }

    public static class ThemeLoader
    {
        public static async Task<LoadedTheme> LoadAsync(RegistryEntry entry, DisplayFiles files,
            CancellationToken cancellationToken = default)
        {
            var problems = new List<string>();
            var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            if (entry.Theme == null)
                return Result(ThemeSpec.Default, images, problems);

            DisplayFileResult themeFile = await files.LoadAsync(entry.Theme, DisplayFileLimits.Theme, cancellationToken)
                .ConfigureAwait(false);
            if (!themeFile.Ok)
            {
                problems.Add(themeFile.Problem);
                return Result(ThemeSpec.Default, images, problems);
            }

            ThemeSpec spec = ThemeReader.Read(themeFile.Bytes);

            string logo = null;
            if (spec.LogoFile != null && await LoadImageAsync(files, spec.LogoFile, ImageRules.Logo, images, problems, cancellationToken)
                    .ConfigureAwait(false))
                logo = spec.LogoFile;

            SchemeSpec dark = await FixArtAsync(spec.Dark, files, images, problems, cancellationToken).ConfigureAwait(false);
            SchemeSpec light = await FixArtAsync(spec.Light, files, images, problems, cancellationToken).ConfigureAwait(false);

            var usable = new ThemeSpec
            {
                Layout = spec.Layout,
                Online = spec.Online,
                Font = spec.Font,
                LogoFile = logo,
                ButtonRadius = spec.ButtonRadius,
                ButtonBorderWidth = spec.ButtonBorderWidth,
                Dark = dark,
                Light = light
            };

            string[] kept = usable.Files().ToArray();
            foreach (string path in images.Keys.Where(p => !kept.Contains(p, StringComparer.Ordinal)).ToList())
                images.Remove(path);

            return Result(usable, images, problems);
        }

        private static async Task<SchemeSpec> FixArtAsync(SchemeSpec scheme, DisplayFiles files,
            Dictionary<string, byte[]> images, List<string> problems, CancellationToken cancellationToken)
        {
            if (scheme.Art.Kind != ArtKind.Image)
                return scheme;

            bool ok = await LoadImageAsync(files, scheme.Art.File, ImageRules.Art, images, problems, cancellationToken)
                .ConfigureAwait(false);
            return ok ? scheme : scheme with { Art = ArtSpec.Default };
        }

        private static async Task<bool> LoadImageAsync(DisplayFiles files, string path, ImageRules rules,
            Dictionary<string, byte[]> images, List<string> problems, CancellationToken cancellationToken)
        {
            byte[] bytes = images.TryGetValue(path, out byte[] known) ? known : null;
            if (bytes == null)
            {
                DisplayFileResult file = await files.LoadAsync(path, rules.MaxBytes, cancellationToken).ConfigureAwait(false);
                if (!file.Ok)
                {
                    problems.Add(file.Problem);
                    return false;
                }
                bytes = file.Bytes;
            }

            if (!ImageInspector.Accepts(bytes, rules, out _, out string reason))
            {
                problems.Add($"'{path}' is not used: {reason}.");
                return false;
            }

            images[path] = bytes;
            return true;
        }

        private static LoadedTheme Result(ThemeSpec spec, Dictionary<string, byte[]> images, List<string> problems) =>
            new LoadedTheme { Spec = spec, Images = images, Problems = problems };
    }
}
