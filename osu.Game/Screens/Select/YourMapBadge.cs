// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// Marks the official maps the player has made a version of their own from (a difficulty that isn't from the website,
    /// like a copy they edited, added to the same set). The player's own difficulties themselves aren't marked.
    /// </summary>
    public partial class YourMapBadge : CircularContainer
    {
        public enum BadgeKind
        {
            None,

            /// <summary>
            /// An official difficulty, or a set, with a difficulty of the player's own alongside it.
            /// </summary>
            HasYourMap,
        }

        /// <summary>
        /// Whether a difficulty is the player's own: it isn't one from the website. The game's own sample maps aren't counted.
        /// </summary>
        public static bool IsYourOwn(BeatmapInfo beatmap) => beatmap.OnlineID <= 0 && beatmap.BeatmapSet?.Protected != true;

        public static BadgeKind KindOf(BeatmapInfo beatmap)
        {
            if (IsYourOwn(beatmap))
                return BadgeKind.None;

            return beatmap.BeatmapSet?.Beatmaps.Any(IsYourOwn) == true ? BadgeKind.HasYourMap : BadgeKind.None;
        }

        public static BadgeKind KindOf(BeatmapSetInfo beatmapSet)
            => beatmapSet.Beatmaps.Any(IsYourOwn) ? BadgeKind.HasYourMap : BadgeKind.None;

        private Box background = null!;
        private OsuSpriteText text = null!;

        private BadgeKind kind;

        public BadgeKind Kind
        {
            get => kind;
            set
            {
                kind = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        public YourMapBadge()
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both },
                text = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: OsuFont.Style.Caption2.Size, weight: FontWeight.Bold),
                    Margin = new MarginPadding { Horizontal = 6, Vertical = 1 },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateDisplay();
        }

        private void updateDisplay()
        {
            switch (kind)
            {
                case BadgeKind.HasYourMap:
                    background.Colour = Color4Extensions.FromHex("ffd60a");
                    text.Colour = Color4.Black;
                    text.Text = "HAS YOUR MAP";
                    Alpha = 1;
                    break;

                default:
                    Alpha = 0;
                    break;
            }
        }
    }
}
