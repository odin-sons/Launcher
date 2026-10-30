# Launcher.Avalonia.Tests

Headless tests for `Launcher.Avalonia` (Avalonia.Headless): windows are constructed and shown without a
screen, so XAML that only fails at runtime and layout rules are caught by `dotnet test`. Not wired into CI
yet, run it locally. Nothing here starts the game or reaches the network.

## MainWindowSmokeTests.cs

`MainWindow.axaml` is parsed at runtime, so a bad value in it only shows up when the window is built.

- **MainWindow_Constructs_WithoutThrowing** / **BuildActionButtons_RunsWithoutThrowing** — the window
  and its sliced-image buttons build.
- **CommunityLinkButtons_AreShown_WhenTheProfileHasLinks** / **CommunityLinkButtons_AreHidden_WhenTheProfileHasNoLinks**
  — the website and community buttons follow the profile.

## ConfirmationWindowTests.cs

The two-button dialog (launcher update, Defender exclusion, server status).

- **EachButton_IsAsWideAsItsLabel** / **Buttons_StayInsideTheWindow** / **ShortLabels_KeepTheUsualButtonSize**
  — a long translation is never cut off or pushed out of the window.

## ServerStatusDialogTests.cs

What the window does with the registry's answer before an update or launch. A signed index is served by a
fake fetcher, the profile folder is a temp folder, and the dialogs are found and clicked by the test.
Starting the game after a "yes" is not covered here (see `InstalledLaunchTests` in `Launcher.Tests` for how
it is resolved).

- **Suspended_OffersToRemove_AndKeepingLeavesEverything** — the dialog names the server and the folder; "keep"
  changes nothing and the update does not go on.
- **Suspended_RemovingTheFiles_DeletesTheProfileFolder_AndSaysSo** — "remove" deletes the profile folder only,
  then confirms it.
- **Suspended_NotInstalled_ExplainsAndOffersNothingToRemove** — a plain message, no removal offer.
- **NeedsApproval_WarnsFirst_AndCancelStartsNothing** — the warning comes before anything starts.
- **NeedsApproval_WithoutPlaying_ExplainsThatUpdatesArePaused** — a check or update request gets the notice.
- **Approved_GoesStraightToTheUpdate** / **TheBuiltInProfile_GoesStraightToTheUpdate_WithoutAskingTheRegistry**
  — no dialog; the built-in profile never touches the registry.

## ThemingTests.cs

The four layout templates and the two colour schemes on a real window, built headless. Nothing is drawn to a
screen, so how it looks is for a person to judge; these tests hold the structure.

- **EveryTemplate_BuildsAllSteps_InBothSchemes** — each template frames the Server, Mods and Install steps in
  the dark and the light scheme, with the palette and theme variant of that scheme.
- **ServerStep_PlacesTheOnlineListAtTheNamedSide** / **ServerStep_ArrangesTheZonesTheWayEachTemplateSays** /
  **OtherSteps_FrameTheirContentAndChooseOneOrTwoColumns** — zone positions and column counts per template.
- **TheHeader_ShowsTheProductNameFromAResource_NotFromTheProfile** / **TheHeader_StaysDark_InTheLightScheme**.
- **TrustIcon_*** — a name, a tooltip and a different shape for every registry status.
- **ReadabilityRules_ApplyPerScheme_AgainstItsOwnPalette** / **TheWizardButton_TakesItsShapeFromTheTheme**.
- **TheSchemeChoice_IsSavedInConfig_AndRestoredByTheNextWindow** / **System_FollowsTheOperatingSystem** /
  **TheSchemeSelector_SitsNextToTheLanguageSelector_WithThreeChoices**.
- **Darkening_*** / **TheArt_DarkensItsOwnFrame_*** — the darkening layer follows the part of the picture each
  frame shows, and a colour or gradient uses its brightest point.

