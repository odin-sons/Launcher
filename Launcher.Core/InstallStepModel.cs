using System;
using System.Collections.Generic;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// The state behind the install progress overlay: a flat, ordered list of steps folded
    /// into the collapsible groups the window draws. <see cref="FileDownloader"/> drives it
    /// through the same index-based <see cref="Start"/>/<see cref="SetProgress"/>/<see cref="Finish"/>
    /// calls it always made — the grouping and the fold-away behaviour live here so they can
    /// be tested without a window.
    ///
    /// Fold rule: the moment a step in a later group goes active, every earlier group whose
    /// steps are all done collapses to a single summary row (the user can reopen it, and that
    /// choice then sticks). The group currently being worked on is always open. This is why
    /// the check phase shrinks to one line once downloading starts, and downloading shrinks
    /// once "finishing up" starts.
    /// </summary>
    public sealed class InstallStepModel
    {
        public enum ItemState { Pending, Active, Done }

        public sealed class StepView
        {
            public string Label { get; init; } = string.Empty;
            public ItemState State { get; internal set; } = ItemState.Pending;

            /// <summary>0..100 within this step — cosmetic, feeds the smooth overall bar.</summary>
            public double Progress { get; internal set; }
        }

        public sealed class GroupView
        {
            public string Label { get; init; } = string.Empty;
            public IReadOnlyList<StepView> Steps { get; init; } = Array.Empty<StepView>();

            /// <summary>Folded to a one-line summary. Toggled automatically as the run advances,
            /// or by the user through <see cref="InstallStepModel.ToggleGroup"/>.</summary>
            public bool Collapsed { get; internal set; }

            public ItemState State =>
                Steps.All(s => s.State == ItemState.Done) ? ItemState.Done
                : Steps.Any(s => s.State != ItemState.Pending) ? ItemState.Active
                : ItemState.Pending;
        }

        private readonly List<StepView> _steps;
        private readonly List<GroupView> _groups;
        private readonly HashSet<int> _userOpened = new();
        private int _activeIndex = -1;
        private double _overall;

        public InstallStepModel(IReadOnlyList<InstallStep> steps)
        {
            _steps = steps.Select(s => new StepView { Label = s.Label }).ToList();

            _groups = steps
                .Select((s, i) => (s.Group, i))
                .GroupBy(x => x.Group)
                .Select(g => new GroupView
                {
                    Label = g.Key,
                    Steps = g.Select(x => _steps[x.i]).ToList(),
                })
                .ToList();
        }

        public IReadOnlyList<GroupView> Groups => _groups;
        public IReadOnlyList<StepView> Steps => _steps;
        public double OverallPercent => _overall;

        public int CurrentOrdinal =>
            _activeIndex >= 0
                ? _activeIndex + 1
                : Math.Min(_steps.Count(s => s.State == ItemState.Done), _steps.Count);

        public string CurrentLabel =>
            _activeIndex >= 0 ? _steps[_activeIndex].Label
            : _steps.Count > 0 ? _steps[^1].Label
            : string.Empty;

        public void Start(int index)
        {
            if (index < 0 || index >= _steps.Count) return;

            _activeIndex = index;
            _steps[index].State = ItemState.Active;
            _steps[index].Progress = 0;

            ReconcileFolding();
            RecomputeOverall();
        }

        public void SetProgress(double percent)
        {
            if (_activeIndex < 0) return;

            _steps[_activeIndex].Progress = Math.Clamp(percent, 0, 100);
            RecomputeOverall();
        }

        public void Finish(int index)
        {
            if (index < 0 || index >= _steps.Count) return;

            _steps[index].State = ItemState.Done;
            _steps[index].Progress = 100;
            if (_activeIndex == index) _activeIndex = -1;

            RecomputeOverall();
        }

        /// <summary>User clicked a group's fold chevron. An explicit open sticks — the run
        /// won't fold that group again.</summary>
        public void ToggleGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= _groups.Count) return;

            GroupView group = _groups[groupIndex];
            group.Collapsed = !group.Collapsed;

            if (group.Collapsed) _userOpened.Remove(groupIndex);
            else _userOpened.Add(groupIndex);
        }

        private void ReconcileFolding()
        {
            if (_activeIndex < 0) return;

            StepView activeStep = _steps[_activeIndex];
            GroupView activeGroup = _groups.First(g => g.Steps.Contains(activeStep));

            for (int i = 0; i < _groups.Count; i++)
            {
                GroupView group = _groups[i];

                if (ReferenceEquals(group, activeGroup))
                {
                    group.Collapsed = false;
                    continue;
                }

                if (_userOpened.Contains(i)) continue;

                group.Collapsed = group.State == ItemState.Done;
            }
        }

        /// <summary>Done steps count as a whole each, the active step adds its own fraction.
        /// Clamped so the bar never steps backwards between phases.</summary>
        private void RecomputeOverall()
        {
            if (_steps.Count == 0) { _overall = 0; return; }

            double completed = _steps.Count(s => s.State == ItemState.Done);
            double activeFraction = _activeIndex >= 0 ? _steps[_activeIndex].Progress / 100.0 : 0;
            double next = Math.Clamp((completed + activeFraction) / _steps.Count * 100, 0, 100);

            if (next > _overall) _overall = next;
        }
    }
}
