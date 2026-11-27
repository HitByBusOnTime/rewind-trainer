// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osuTK;

namespace osu.Game.Tests.Visual.SongSelect
{
    [TestFixture]
    public partial class TestSceneBeatmapCarouselDifficultyGrouping : BeatmapCarouselTestScene
    {
        [SetUpSteps]
        public void SetUpSteps()
        {
            RemoveAllBeatmaps();
            CreateCarousel();

            SortAndGroupBy(SortMode.Difficulty, GroupMode.Difficulty);

            AddBeatmaps(10, 3);
            WaitForDrawablePanels();
        }

        [Test]
        public void TestYourMapsAreMarked()
        {
            // Only what's on screen gets drawn, so keep the list short.
            RemoveAllBeatmaps();

            AddStep("add a set with a difficulty of your own, and a plain set", () =>
            {
                var set = CreateTestBeatmapSetInfo(3, false);
                set.Beatmaps[0].OnlineID = -1;
                set.Beatmaps[0].DifficultyName = "practic";
                BeatmapSets.Add(set);
                BeatmapSets.Add(CreateTestBeatmapSetInfo(2, false));
            });

            // One folder is open at a time, so open the one holding your difficulty.
            AddUntilStep("open the folder with your difficulty", () =>
            {
                var group = Carousel.GetCarouselItems()?.Select(i => i.Model).OfType<GroupedBeatmap>().FirstOrDefault(b => b.Beatmap.OnlineID == -1)?.Group;
                var panel = Carousel.ChildrenOfType<PanelGroupStarDifficulty>().FirstOrDefault(p => p.Item?.Model is GroupDefinition g && g.Equals(group));

                if (panel == null)
                    return false;

                if (!group!.Equals(Carousel.OpenGroup))
                    panel.TriggerClick();

                return true;
            });

            AddUntilStep("your difficulty says YOUR MAP and the official ones say HAS YOUR MAP", () =>
            {
                var panels = Carousel.ChildrenOfType<PanelBeatmapStandalone>().Where(p => p.Item != null).ToList();
                var mine = panels.Where(p => ((GroupedBeatmap)p.Item!.Model).Beatmap.OnlineID == -1).ToList();
                var siblings = panels.Where(p => ((GroupedBeatmap)p.Item!.Model).Beatmap.BeatmapSet!.Beatmaps.Any(b => b.OnlineID == -1)
                                                 && ((GroupedBeatmap)p.Item!.Model).Beatmap.OnlineID != -1).ToList();

                return mine.Count > 0 && mine.All(p => p.ChildrenOfType<YourMapBadge>().Single().Kind == YourMapBadge.BadgeKind.YourMap)
                                      && siblings.All(p => p.ChildrenOfType<YourMapBadge>().Single().Kind == YourMapBadge.BadgeKind.HasYourMap)
                                      && panels.Except(mine).Except(siblings).All(p => p.ChildrenOfType<YourMapBadge>().Single().Kind == YourMapBadge.BadgeKind.None);
            });
        }

        [Test]
        public void TestNavigatorJumpsBetweenStarFolders()
        {
            GroupNavigator navigator = null!;
            GroupDefinition[] groups = null!;

            AddStep("add the folder bar", () => Add(navigator = new GroupNavigator(Carousel)));
            AddUntilStep("there are several star folders", () => (groups = Carousel.GetGroups().ToArray()).Length, () => Is.GreaterThanOrEqualTo(3));

            AddUntilStep("the bar says no folder is open, and offers the first", () =>
            {
                navigator.Refresh();
                return navigator.CurrentText.ToString() == "No folder open" && navigator.NextText.ToString() == $"↓ {groups[0].Title}";
            });

            AddStep("click the folder below", () => navigator.ChildrenOfType<RoundedButton>().Last().TriggerClick());
            AddUntilStep("the first folder is open", () => Carousel.OpenGroup, () => Is.EqualTo(groups[0]));

            AddStep("click the folder below", () => navigator.ChildrenOfType<RoundedButton>().Last().TriggerClick());
            AddUntilStep("the second folder is open", () => Carousel.OpenGroup, () => Is.EqualTo(groups[1]));
            AddUntilStep("the bar shows the folders around it", () =>
            {
                navigator.Refresh();
                return navigator.CurrentText.ToString() == groups[1].Title.ToString()
                       && navigator.PreviousText.ToString() == $"↑ {groups[0].Title}"
                       && navigator.NextText.ToString() == $"↓ {groups[2].Title}";
            });

            AddStep("click the folder above", () => navigator.ChildrenOfType<RoundedButton>().First().TriggerClick());
            AddUntilStep("back to the first folder", () => Carousel.OpenGroup, () => Is.EqualTo(groups[0]));
        }

        [Test]
        public void TestOpenCloseGroupWithNoSelectionMouse()
        {
            AddAssert("no beatmaps visible", () => Carousel.ChildrenOfType<PanelBeatmapStandalone>().Count(p => p.Alpha > 0), () => Is.Zero);
            CheckNoSelection();

            ClickVisiblePanel<PanelGroupStarDifficulty>(0);
            AddUntilStep("some beatmaps visible", () => Carousel.ChildrenOfType<PanelBeatmapStandalone>().Count(p => p.Alpha > 0), () => Is.GreaterThan(0));
            CheckNoSelection();

            ClickVisiblePanel<PanelGroupStarDifficulty>(0);
            AddUntilStep("no beatmaps visible", () => Carousel.ChildrenOfType<PanelBeatmapStandalone>().Count(p => p.Alpha > 0), () => Is.Zero);
            CheckNoSelection();
        }

        [Test]
        public void TestOpenCloseGroupWithNoSelectionKeyboard()
        {
            AddAssert("no beatmaps visible", () => Carousel.ChildrenOfType<PanelBeatmapStandalone>().Count(p => p.Alpha > 0), () => Is.Zero);
            CheckNoSelection();

            SelectNextPanel();
            Select();
            AddUntilStep("some beatmaps visible", () => Carousel.ChildrenOfType<PanelBeatmapStandalone>().Count(p => p.Alpha > 0), () => Is.GreaterThan(0));
            AddAssert("keyboard selected is expanded", () => GetKeyboardSelectedPanel()?.Expanded.Value, () => Is.True);
            CheckNoSelection();

            Select();
            AddUntilStep("no beatmaps visible", () => Carousel.ChildrenOfType<PanelBeatmapStandalone>().Count(p => p.Alpha > 0), () => Is.Zero);
            AddAssert("keyboard selected is collapsed", () => GetKeyboardSelectedPanel()?.Expanded.Value, () => Is.False);
            CheckNoSelection();
        }

        [Test]
        public void TestCarouselRemembersSelection()
        {
            SelectNextSet();

            object? selection = null;

            AddStep("store drawable selection", () => selection = GetSelectedPanel()?.Item?.Model);

            CheckHasSelection();
            AddAssert("drawable selection non-null", () => selection, () => Is.Not.Null);
            AddAssert("drawable selection matches carousel selection", () => selection, () => Is.EqualTo(Carousel.CurrentGroupedBeatmap));

            RemoveAllBeatmaps();
            AddUntilStep("no drawable selection", GetSelectedPanel, () => Is.Null);

            AddBeatmaps(3);
            WaitForDrawablePanels();

            CheckHasSelection();
            AddAssert("no drawable selection", GetSelectedPanel, () => Is.Null);

            AddStep("add previous selection", () => BeatmapSets.Add(((GroupedBeatmap)selection!).Beatmap.BeatmapSet!));

            AddAssert("selection matches original carousel selection", () => selection, () => Is.EqualTo(Carousel.CurrentGroupedBeatmap));
            AddUntilStep("drawable selection restored", () => GetSelectedPanel()?.Item?.Model, () => Is.EqualTo(selection));
            AddAssert("carousel item is visible", () => GetSelectedPanel()?.Item?.IsVisible, () => Is.True);

            ClickVisiblePanel<PanelGroupStarDifficulty>(0);
            AddUntilStep("carousel item not visible", GetSelectedPanel, () => Is.Null);

            ClickVisiblePanel<PanelGroupStarDifficulty>(0);
            AddUntilStep("carousel item is visible", () => GetSelectedPanel()?.Item?.IsVisible, () => Is.True);
        }

        [Test]
        public void TestGroupSelectionOnHeaderKeyboard()
        {
            SelectNextSet();
            WaitForBeatmapSelection(0, 0);

            SelectPrevPanel();

            ICarouselPanel? groupPanel = null;

            AddStep("get group panel", () => groupPanel = GetKeyboardSelectedPanel());

            AddAssert("keyboard selected panel is expanded", () => GetKeyboardSelectedPanel()?.Expanded.Value, () => Is.True);
            AddAssert("keyboard selected panel is group", GetKeyboardSelectedPanel, () => Is.EqualTo(groupPanel));

            SelectPrevSet();

            WaitForBeatmapSelection(0, 0);
            AddAssert("keyboard selected panel is contracted", () => GetKeyboardSelectedPanel()?.Expanded.Value, () => Is.False);
            AddAssert("keyboard selected panel is group", GetKeyboardSelectedPanel, () => Is.EqualTo(groupPanel));

            SelectPrevSet();

            WaitForBeatmapSelection(0, 0);
            // Expanding a group will move keyboard selection to the selected beatmap if contained.
            AddAssert("keyboard selected panel is expanded", () => groupPanel?.Expanded.Value, () => Is.True);
            AddAssert("keyboard selected panel is beatmap", () => GetKeyboardSelectedPanel()?.Item?.Model, Is.TypeOf<GroupedBeatmap>);
        }

        [Test]
        public void TestKeyboardGroupToggleCollapse_SelectionContained()
        {
            SelectNextSet();
            WaitForBeatmapSelection(0, 0);
            checkBeatmapIsKeyboardSelected();

            ToggleGroupCollapse();
            checkGroupKeyboardSelected(0);

            ToggleGroupCollapse();
            checkBeatmapIsKeyboardSelected();
        }

        [Test]
        public void TestKeyboardGroupToggleCollapse_SelectionNotContained()
        {
            SelectNextSet();
            WaitForBeatmapSelection(0, 0);
            checkBeatmapIsKeyboardSelected();

            SelectNextGroup();
            checkGroupKeyboardSelected(1);

            ToggleGroupCollapse();
            checkGroupKeyboardSelected(1);

            ToggleGroupCollapse();
            checkGroupKeyboardSelected(1);
        }

        [Test]
        public void TestKeyboardGroupTraversalSingleGroup()
        {
            RemoveAllBeatmaps();
            AddBeatmaps(1, 1);

            WaitForBeatmapSelection(0, 0);

            SelectNextGroup();
            checkBeatmapIsKeyboardSelected();

            SelectPrevGroup();
            checkBeatmapIsKeyboardSelected();
        }

        [Test]
        public void TestKeyboardGroupTraversal()
        {
            SelectNextSet();
            WaitForBeatmapSelection(0, 0);
            checkBeatmapIsKeyboardSelected();

            SelectNextGroup();
            WaitForBeatmapSelection(0, 0);
            WaitForExpandedGroup(1);
            checkGroupKeyboardSelected(1);

            SelectNextGroup();
            WaitForBeatmapSelection(0, 0);
            WaitForExpandedGroup(2);
            checkGroupKeyboardSelected(2);

            SelectNextGroup();
            WaitForBeatmapSelection(0, 0);
            WaitForExpandedGroup(0);
            checkBeatmapIsKeyboardSelected();

            SelectPrevGroup();
            WaitForBeatmapSelection(0, 0);
            WaitForExpandedGroup(2);
            checkGroupKeyboardSelected(2);
        }

        private void checkBeatmapIsKeyboardSelected() =>
            AddUntilStep("check keyboard selected group is beatmap", () => GetKeyboardSelectedPanel()?.Item?.Model, () => Is.EqualTo(Carousel.CurrentGroupedBeatmap));

        private void checkGroupKeyboardSelected(int index) => AddUntilStep($"check keyboard selected group is {index}", () => GetKeyboardSelectedPanel()?.Item?.Model, () =>
        {
            var groupingFilter = Carousel.Filters.OfType<BeatmapCarouselFilterGrouping>().Single();

            GroupDefinition g = groupingFilter.GroupItems.Keys.ElementAt(index);
            // offset by one because the group itself is included in the items list.
            CarouselItem item = groupingFilter.GroupItems[g].ElementAt(0);

            return Is.EqualTo(item.Model);
        });

        [Test]
        public void TestGroupSelectionOnHeaderMouse()
        {
            SelectNextSet();
            WaitForBeatmapSelection(0, 0);

            AddAssert("keyboard selected panel is beatmap", GetKeyboardSelectedPanel, Is.TypeOf<PanelBeatmapStandalone>);
            AddAssert("selected panel is beatmap", GetSelectedPanel, Is.TypeOf<PanelBeatmapStandalone>);

            ClickVisiblePanel<PanelGroupStarDifficulty>(0);
            AddAssert("keyboard selected panel is group", GetKeyboardSelectedPanel, Is.TypeOf<PanelGroupStarDifficulty>);
            AddAssert("keyboard selected panel is contracted", () => GetKeyboardSelectedPanel()?.Expanded.Value, () => Is.False);

            ClickVisiblePanel<PanelGroupStarDifficulty>(0);
            // Expanding a group will move keyboard selection to the selected beatmap if contained.
            AddAssert("keyboard selected panel is group", GetKeyboardSelectedPanel, Is.TypeOf<PanelBeatmapStandalone>);
            AddAssert("selected panel is still beatmap", GetSelectedPanel, Is.TypeOf<PanelBeatmapStandalone>);
        }

        [Test]
        public void TestKeyboardSelection()
        {
            SelectNextPanel();
            SelectNextPanel();
            SelectNextPanel();
            SelectNextPanel();
            CheckNoSelection();

            // open first group
            Select();
            CheckNoSelection();
            AddUntilStep("some beatmaps visible", () => Carousel.ChildrenOfType<PanelBeatmapStandalone>().Count(p => p.Alpha > 0), () => Is.GreaterThan(0));

            SelectNextPanel();
            Select();
            WaitForBeatmapSelection(0, 0);

            SelectNextSet();
            WaitForBeatmapSelection(0, 1);

            SelectNextSet();
            WaitForBeatmapSelection(0, 2);

            SelectPrevSet();
            WaitForBeatmapSelection(0, 1);

            SelectPrevSet();
            WaitForBeatmapSelection(0, 0);

            SelectPrevSet();
            WaitForBeatmapSelection(2, 9);
        }

        [Test]
        public void TestInputHandlingWithinGaps()
        {
            AddAssert("no beatmaps visible", () => !GetVisiblePanels<PanelBeatmapStandalone>().Any());

            // Clicks just above the first group panel should not actuate any action.
            ClickVisiblePanelWithOffset<PanelGroupStarDifficulty>(0, new Vector2(0, -(PanelGroupStarDifficulty.HEIGHT / 2 + 1)));

            AddAssert("no beatmaps visible", () => !GetVisiblePanels<PanelBeatmapStandalone>().Any());

            // add lenience to avoid floating-point inaccuracies at edge.
            ClickVisiblePanelWithOffset<PanelGroupStarDifficulty>(0, new Vector2(0, -(PanelGroup.HEIGHT / 2 - 1)));

            AddUntilStep("wait for beatmaps visible", () => GetVisiblePanels<PanelBeatmapStandalone>().Any());
            CheckNoSelection();

            // Beatmap panels expand their selection area to cover holes from spacing.
            ClickVisiblePanelWithOffset<PanelBeatmapStandalone>(0, new Vector2(0, -(CarouselItem.DEFAULT_HEIGHT / 2 + 1)));
            WaitForBeatmapSelection(0, 0);

            ClickVisiblePanelWithOffset<PanelBeatmapStandalone>(1, new Vector2(0, (CarouselItem.DEFAULT_HEIGHT / 2 + 1)));
            WaitForBeatmapSelection(0, 1);
        }

        [Test]
        public void TestBasicFiltering()
        {
            ApplyToFilterAndWaitForFilter("filter", c => c.SearchText = BeatmapSets[2].Metadata.Title);

            CheckDisplayedGroupsCount(3);
            CheckDisplayedBeatmapsCount(3);

            // Single result gets selected automatically
            WaitForBeatmapSelection(0, 0);

            SelectNextPanel();
            Select();
            WaitForBeatmapSelection(0, 0);

            for (int i = 0; i < 5; i++)
                SelectNextPanel();

            Select();
            SelectNextPanel();
            Select();

            WaitForBeatmapSelection(1, 0);

            ApplyToFilterAndWaitForFilter("remove filter", c => c.SearchText = string.Empty);

            CheckDisplayedGroupsCount(3);
            CheckDisplayedBeatmapsCount(30);
        }

        [Test]
        public void TestExpandedGroupStillExpandedAfterFilter()
        {
            SelectPrevSet();

            WaitForBeatmapSelection(2, 9);
            AddAssert("expanded group is last", () => (Carousel.ExpandedGroup as StarDifficultyGroupDefinition)?.Difficulty.Stars, () => Is.EqualTo(6));

            SelectNextPanel();
            Select();

            WaitForBeatmapSelection(2, 9);
            AddAssert("expanded group is first", () => (Carousel.ExpandedGroup as StarDifficultyGroupDefinition)?.Difficulty.Stars, () => Is.EqualTo(0));

            // doesn't actually filter anything away, but triggers a filter.
            ApplyToFilterAndWaitForFilter("filter", c => c.SearchText = "Some");

            AddAssert("expanded group is still first", () => (Carousel.ExpandedGroup as StarDifficultyGroupDefinition)?.Difficulty.Stars, () => Is.EqualTo(0));
        }

        [Test]
        public void TestExpandedGroupDoesNotExpandAgainOnRefilterIfManuallyCollapsed()
        {
            SelectPrevSet();

            WaitForBeatmapSelection(2, 9);
            AddAssert("expanded group is last", () => (Carousel.ExpandedGroup as StarDifficultyGroupDefinition)?.Difficulty.Stars, () => Is.EqualTo(6));

            SelectNextPanel();
            Select();

            WaitForBeatmapSelection(2, 9);
            AddAssert("expanded group is first", () => (Carousel.ExpandedGroup as StarDifficultyGroupDefinition)?.Difficulty.Stars, () => Is.EqualTo(0));

            ToggleGroupCollapse();

            // doesn't actually filter anything away, but triggers a filter.
            ApplyToFilterAndWaitForFilter("filter", c => c.SearchText = "Some");
            AddAssert("group didn't re-expand", () => (Carousel.ExpandedGroup as StarDifficultyGroupDefinition)?.Difficulty.Stars, () => Is.Null);

            ToggleGroupCollapse();

            ApplyToFilterAndWaitForFilter("filter", c => c.SearchText = "Som");
            AddAssert("expanded group is first", () => (Carousel.ExpandedGroup as StarDifficultyGroupDefinition)?.Difficulty.Stars, () => Is.EqualTo(0));
        }
    }
}
