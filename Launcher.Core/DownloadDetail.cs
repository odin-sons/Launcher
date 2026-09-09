using System;
using System.Collections.Generic;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// A ready-to-render snapshot of the download in progress, pushed to
    /// <see cref="IUpdateUi.SetDownloadDetail"/>. Text is pre-formatted by
    /// <see cref="FileDownloader"/> (same arrangement as <see cref="IUpdateUi.SetTotalProgress"/>)
    /// so the UI just draws it.
    /// </summary>
    public sealed class DownloadDetail
    {
        /// <summary>e.g. "Mods: 31 / 78  ·  412 / 1180 MB  ·  6.2 MB/s".</summary>
        public string HeadlineText { get; init; } = string.Empty;

        /// <summary>0..1 by bytes — for a thin secondary bar if the UI wants one.</summary>
        public double OverallFraction { get; init; }

        /// <summary>What's transferring right now, biggest first, already capped for display.</summary>
        public IReadOnlyList<DownloadDetailItem> Active { get; init; } = Array.Empty<DownloadDetailItem>();
    }

    public sealed class DownloadDetailItem
    {
        public string Title { get; init; } = string.Empty;
        public string SizeText { get; init; } = string.Empty; // "12.3 / 45.6 MB"
        public double Fraction { get; init; }                  // 0..1
    }
}
