using System.Collections.Generic;
using System.Linq;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// The install window's step/group state machine (<see cref="InstallStepModel"/>): the
    /// two-level list the progress overlay renders, when a finished group folds itself away,
    /// and the one overall bar that must only ever move forward.
    /// </summary>
    public sealed class InstallStepModelTests
    {
        private static InstallStepModel ThreeGroupRun() => new(new List<InstallStep>
        {
            new("Checking files", "Verifying Steam install"),
            new("Checking files", "Checking game files"),
            new("Checking files", "Checking optional mods"),
            new("Downloading content", "Downloading files"),
            new("Finishing up", "Finishing up"),
        });

        [Fact]
        public void Groups_AreBuiltFromTheGroupTags_InFirstSeenOrder()
        {
            InstallStepModel model = ThreeGroupRun();

            Assert.Equal(
                new[] { "Checking files", "Downloading content", "Finishing up" },
                model.Groups.Select(g => g.Label));
            Assert.Equal(3, model.Groups[0].Steps.Count);
            Assert.Equal(1, model.Groups[1].Steps.Count);
        }

        [Fact]
        public void EveryGroupStartsExpanded()
        {
            Assert.All(ThreeGroupRun().Groups, g => Assert.False(g.Collapsed));
        }

        [Fact]
        public void StartingAStepInTheNextGroup_FoldsAwayThePreviousFinishedGroup()
        {
            InstallStepModel model = ThreeGroupRun();

            model.Start(0); model.Finish(0);
            model.Start(1); model.Finish(1);
            model.Start(2); model.Finish(2);
            Assert.False(model.Groups[0].Collapsed); // still the active group

            model.Start(3); // first step of "Downloading content"

            Assert.True(model.Groups[0].Collapsed);
            Assert.False(model.Groups[1].Collapsed); // the group now being worked on
        }

        [Fact]
        public void TheActiveGroupIsNeverCollapsed()
        {
            InstallStepModel model = ThreeGroupRun();
            model.Start(0);
            Assert.False(model.Groups[0].Collapsed);
            model.Finish(0);
            model.Start(1);
            Assert.False(model.Groups[0].Collapsed);
        }

        [Fact]
        public void AGroupThatIsNotFullyDone_IsNotFoldedAway()
        {
            InstallStepModel model = ThreeGroupRun();
            model.Start(0); model.Finish(0);
            model.Start(1); // group 0 step 2 still pending — leave it visible
            model.Start(3);

            Assert.False(model.Groups[0].Collapsed);
        }

        [Fact]
        public void TheDownloadGroupFoldsAway_OnceFinishingUpBegins()
        {
            InstallStepModel model = ThreeGroupRun();
            for (int i = 0; i < 4; i++) { model.Start(i); model.Finish(i); }

            model.Start(4); // "Finishing up"

            Assert.True(model.Groups[1].Collapsed);
        }

        [Fact]
        public void ToggleGroup_ReopensAFoldedGroup_AndItStaysOpenAfterwards()
        {
            InstallStepModel model = ThreeGroupRun();
            for (int i = 0; i < 3; i++) { model.Start(i); model.Finish(i); }
            model.Start(3);
            Assert.True(model.Groups[0].Collapsed);

            model.ToggleGroup(0);
            Assert.False(model.Groups[0].Collapsed);

            model.Start(4); // another group transition — the user's choice must survive it
            Assert.False(model.Groups[0].Collapsed);
        }

        [Fact]
        public void OverallPercent_NeverMovesBackwards()
        {
            InstallStepModel model = ThreeGroupRun();
            double last = 0;

            void Observe() { Assert.True(model.OverallPercent >= last); last = model.OverallPercent; }

            model.Start(0); Observe();
            model.SetProgress(80); Observe();
            model.Finish(0); Observe();
            model.Start(1); Observe();            // active fraction resets to 0 here
            model.SetProgress(10); Observe();
            model.Finish(1); Observe();
        }

        [Fact]
        public void OverallPercent_IsDoneStepsWhole_PlusTheActiveStepFraction()
        {
            InstallStepModel model = ThreeGroupRun(); // 5 steps
            model.Start(0); model.Finish(0);
            model.Start(1);
            model.SetProgress(50);

            // 1 done + 0.5 active, out of 5 steps
            Assert.Equal(30, model.OverallPercent, 3);
        }

        [Fact]
        public void CurrentOrdinalAndLabel_FollowTheActiveStep_ThenRestOnTheLast()
        {
            InstallStepModel model = ThreeGroupRun();

            model.Start(1);
            Assert.Equal(2, model.CurrentOrdinal);
            Assert.Equal("Checking game files", model.CurrentLabel);

            for (int i = 0; i < 5; i++) { model.Start(i); model.Finish(i); }
            Assert.Equal(5, model.CurrentOrdinal);
            Assert.Equal("Finishing up", model.CurrentLabel);
        }
    }
}
