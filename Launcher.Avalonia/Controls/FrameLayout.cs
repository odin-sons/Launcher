namespace Odinsons.ValheimLauncher.Avalonia.Controls
{
    internal enum StepKind { Server, Terms, Mods, Install }

    internal enum ZoneFlow { Stacked, SideBySide }

    internal readonly record struct Cell(int Row, int Column, int RowSpan = 1, int ColumnSpan = 1);

    /// <summary>Where each zone of a step sits inside the frame of a template. A missing cell means the zone is not shown.</summary>
    internal sealed record FramePlan(
        string Columns,
        string Rows,
        Cell? Online,
        Cell? Art,
        Cell? AboutNews,
        Cell? Body,
        Cell? Header,
        ArtMode ArtMode,
        ZoneFlow Flow,
        bool ShowNews,
        bool AboutIsCard,
        bool TwoColumns);

    internal static class FrameLayout
    {
        public const int ArtColumnWidth = 190;
        public const int ArtStripHeight = 92;
        public const int ServerStripHeight = 168;

        public static FramePlan Plan(LayoutTemplate template, StepKind step, OnlineSide online) =>
            step == StepKind.Server ? ServerPlan(template, online) : StepPlan(template);

        private static FramePlan ServerPlan(LayoutTemplate template, OnlineSide online)
        {
            bool left = online == OnlineSide.Left;
            switch (template)
            {
                case LayoutTemplate.Sidebar:
                    return left
                        ? Server("Auto,3*,2*", "*", online: new Cell(0, 0), art: new Cell(0, 2), about: new Cell(0, 1))
                        : Server("3*,2*,Auto", "*", online: new Cell(0, 2), art: new Cell(0, 1), about: new Cell(0, 0));

                case LayoutTemplate.Banner:
                    string rows = $"{ServerStripHeight},*";
                    return left
                        ? Server("Auto,*", rows, online: new Cell(0, 0, 2, 1), art: new Cell(0, 1), about: new Cell(1, 1), flow: ZoneFlow.SideBySide)
                        : Server("*,Auto", rows, online: new Cell(0, 1, 2, 1), art: new Cell(0, 0), about: new Cell(1, 0), flow: ZoneFlow.SideBySide);

                case LayoutTemplate.Minimal:
                    return left
                        ? Server("Auto,*", "*", online: new Cell(0, 0), art: new Cell(0, 1), about: new Cell(0, 1), news: false, card: true)
                        : Server("*,Auto", "*", online: new Cell(0, 1), art: new Cell(0, 0), about: new Cell(0, 0), news: false, card: true);

                default:
                    return left
                        ? Server("Auto,2*,3*", "*", online: new Cell(0, 0), art: new Cell(0, 1), about: new Cell(0, 2))
                        : Server("2*,3*,Auto", "*", online: new Cell(0, 2), art: new Cell(0, 0), about: new Cell(0, 1));
            }
        }

        private static FramePlan Server(string columns, string rows, Cell online, Cell art, Cell about,
            ZoneFlow flow = ZoneFlow.Stacked, bool news = true, bool card = false) =>
            new(columns, rows, online, art, about, null, null, ArtMode.Hero, flow, news, card, false);

        private static FramePlan StepPlan(LayoutTemplate template) => template switch
        {
            LayoutTemplate.Sidebar => Step($"*,{ArtColumnWidth}", "*", art: new Cell(0, 1), body: new Cell(0, 0), ArtMode.Column, false),
            LayoutTemplate.Banner => Step("*", $"{ArtStripHeight},*", art: new Cell(0, 0), body: new Cell(1, 0), ArtMode.Strip, true),
            LayoutTemplate.Minimal => Step("*", "Auto,*", art: null, body: new Cell(1, 0), ArtMode.Strip, true, header: new Cell(0, 0)),
            _ => Step($"{ArtColumnWidth},*", "*", art: new Cell(0, 0), body: new Cell(0, 1), ArtMode.Column, false)
        };

        private static FramePlan Step(string columns, string rows, Cell? art, Cell body, ArtMode mode, bool twoColumns,
            Cell? header = null) =>
            new(columns, rows, null, art, null, body, header, mode, ZoneFlow.Stacked, true, false, twoColumns);
    }
}
