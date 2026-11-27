// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// Shows which folder of the song list is open (like "2 Stars" when grouped by difficulty), with the folders above and below it.
    /// Clicking one opens it, the same as Shift+Left/Right, so a big folder doesn't have to be scrolled through to reach the next.
    /// </summary>
    public partial class GroupNavigator : CompositeDrawable
    {
        public const float HEIGHT = 36;

        private const double refresh_interval = 250;

        private readonly BeatmapCarousel carousel;

        private RoundedButton previous = null!;
        private RoundedButton next = null!;
        private OsuSpriteText current = null!;

        private double lastRefresh = double.MinValue;

        public LocalisableString CurrentText => current.Text;
        public LocalisableString PreviousText => previous.Text;
        public LocalisableString NextText => next.Text;

        public GroupNavigator(BeatmapCarousel carousel)
        {
            this.carousel = carousel;

            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.7f,
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(3),
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Relative, 0.34f),
                        new Dimension(),
                        new Dimension(GridSizeMode.Relative, 0.34f),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            previous = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 1,
                                Action = () => carousel.TraverseGroups(-1),
                            },
                            current = new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                            },
                            next = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 1,
                                Action = () => carousel.TraverseGroups(1),
                            },
                        }
                    }
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (Time.Current - lastRefresh < refresh_interval)
                return;

            lastRefresh = Time.Current;
            Refresh();
        }

        public void Refresh()
        {
            IReadOnlyList<GroupDefinition> groups = carousel.GetGroups();

            // Nothing to jump between without grouping, or with a single folder.
            if (groups.Count < 2)
            {
                this.FadeOut(200);
                return;
            }

            this.FadeIn(200);

            GroupDefinition? open = carousel.OpenGroup;
            int index = -1;

            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Equals(open))
                {
                    index = i;
                    break;
                }
            }

            current.Text = index >= 0 ? groups[index].Title : "No folder open";
            setButton(previous, index > 0 ? groups[index - 1] : null, "↑ ");
            setButton(next, index >= 0 && index + 1 < groups.Count ? groups[index + 1] : index < 0 ? groups[0] : null, "↓ ");
        }

        private static void setButton(RoundedButton button, GroupDefinition? group, string arrow)
        {
            button.Text = group == null ? string.Empty : LocalisableString.Interpolate($"{arrow}{group.Title}");
            button.Enabled.Value = group != null;
            button.Alpha = group == null ? 0 : 1;
        }
    }
}
