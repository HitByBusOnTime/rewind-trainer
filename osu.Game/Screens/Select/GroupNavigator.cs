// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// Small labels over the song list when it's grouped (like "2 stars" when grouped by difficulty): the folder above at the top
    /// with the open folder under it, and the folder below at the bottom. Clicking the one above or below opens it, the same as
    /// Shift+Left/Right, so a big folder doesn't have to be scrolled through to reach the next.
    /// </summary>
    public partial class GroupNavigator : CompositeDrawable
    {
        private const double refresh_interval = 250;

        private readonly BeatmapCarousel carousel;

        public FolderLabel Current { get; private set; } = null!;
        public FolderLabel Previous { get; private set; } = null!;
        public FolderLabel Next { get; private set; } = null!;

        private double lastRefresh = double.MinValue;

        public LocalisableString CurrentText => Current.Text;
        public LocalisableString PreviousText => Previous.Text;
        public LocalisableString NextText => Next.Text;

        public GroupNavigator(BeatmapCarousel carousel)
        {
            this.carousel = carousel;

            // Covers the list so the labels can sit at its top and bottom; only the labels themselves take clicks.
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            // Starts hidden until it knows there are folders, and a hidden drawable isn't updated otherwise, so it would never show.
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 2),
                    Margin = new MarginPadding { Top = 4 },
                    Children = new Drawable[]
                    {
                        Previous = new FolderLabel(() => carousel.TraverseGroups(-1))
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                        },
                        Current = new FolderLabel(null)
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                        },
                    }
                },
                Next = new FolderLabel(() => carousel.TraverseGroups(1))
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Margin = new MarginPadding { Bottom = 6 },
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

            Current.Show(index >= 0 ? groups[index].Title : "No folder open");
            Previous.Show(index > 0 ? groups[index - 1] : null, "↑ ");
            Next.Show(index >= 0 && index + 1 < groups.Count ? groups[index + 1] : index < 0 ? groups[0] : null, "↓ ");
        }

        /// <summary>
        /// A small rounded label with a folder's name, which opens it when clicked if it has somewhere to go.
        /// </summary>
        public partial class FolderLabel : OsuClickableContainer
        {
            private readonly Action? open;

            private Box background = null!;
            private OsuSpriteText text = null!;

            public LocalisableString Text => text.Text;

            public FolderLabel(Action? open)
            {
                this.open = open;

                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 9;
                Action = () => this.open?.Invoke();
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = open == null ? 0.75f : 0.55f,
                    },
                    text = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: open == null ? 16 : 13, weight: open == null ? FontWeight.Bold : FontWeight.SemiBold),
                        Margin = new MarginPadding { Horizontal = 9, Vertical = 1 },
                    },
                };
            }

            public void Show(LocalisableString title)
            {
                text.Text = title;
                Enabled.Value = false;
                Alpha = 1;
            }

            public void Show(GroupDefinition? group, string arrow)
            {
                text.Text = group == null ? string.Empty : LocalisableString.Interpolate($"{arrow}{group.Title}");
                Enabled.Value = group != null;
                Alpha = group == null ? 0 : 1;
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (open != null)
                    background.FadeTo(0.85f, 100);

                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (open != null)
                    background.FadeTo(0.55f, 100);

                base.OnHoverLost(e);
            }
        }
    }
}
