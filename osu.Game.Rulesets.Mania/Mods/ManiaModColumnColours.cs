// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Mania.Mods
{
    /// <summary>
    /// Gives every column of a stage its own fixed colour, so it's easy to see which key a note is for.
    /// </summary>
    public class ManiaModColumnColours : Mod, IApplicableToDrawableRuleset<ManiaHitObject>, IApplicableToDrawableHitObject
    {
        public override string Name => "Column Colours";
        public override string Acronym => "CC";
        public override IconUsage? Icon => FontAwesome.Solid.Palette;
        public override ModType Type => ModType.Fun;
        public override LocalisableString Description => "Every key gets its own colour, the same at any key count.";

        /// <summary>
        /// A colour for each key of the game's standard layout, left to right, so a key (and the finger on it) has the same colour
        /// at every key count: F is green on 4K, 5K and 7K alike. Colours used to be spread over the keys a map had, which gave
        /// the same finger a different colour on each key count.
        /// </summary>
        public static readonly IReadOnlyDictionary<InputKey, Color4> KEY_COLOURS = new Dictionary<InputKey, Color4>
        {
            [InputKey.A] = Color4Extensions.FromHex("ff3b3b"), // red
            [InputKey.S] = Color4Extensions.FromHex("ff8a1f"), // orange
            [InputKey.D] = Color4Extensions.FromHex("ffd60a"), // yellow
            [InputKey.F] = Color4Extensions.FromHex("2ecc71"), // green
            [InputKey.V] = Color4Extensions.FromHex("e0e0e0"), // light grey, 10 keys only
            [InputKey.Space] = Color4Extensions.FromHex("ffffff"), // white, the middle key
            [InputKey.N] = Color4Extensions.FromHex("9e9e9e"), // grey, 10 keys only
            [InputKey.J] = Color4Extensions.FromHex("1ed3e0"), // cyan
            [InputKey.K] = Color4Extensions.FromHex("3b82f6"), // blue
            [InputKey.L] = Color4Extensions.FromHex("b65cff"), // purple
            [InputKey.Semicolon] = Color4Extensions.FromHex("ff4fb8"), // pink
        };

        /// <summary>
        /// The game's default keys for a stage with <paramref name="columns"/> keys: the innermost left-hand keys, Space in the middle
        /// when the count is odd, then the right-hand keys. The same rule the game uses to make its default key bindings.
        /// </summary>
        public static InputKey[] DefaultKeysFor(int columns)
        {
            InputKey[] left = columns == 10 ? new[] { InputKey.A, InputKey.S, InputKey.D, InputKey.F, InputKey.V } : new[] { InputKey.A, InputKey.S, InputKey.D, InputKey.F };
            InputKey[] right = columns == 10 ? new[] { InputKey.N, InputKey.J, InputKey.K, InputKey.L, InputKey.Semicolon } : new[] { InputKey.J, InputKey.K, InputKey.L, InputKey.Semicolon };
            int side = Math.Clamp(columns / 2, 0, left.Length);

            var keys = new List<InputKey>(left.Skip(left.Length - side));
            if (columns % 2 == 1)
                keys.Add(InputKey.Space);
            keys.AddRange(right.Take(side));

            return keys.ToArray();
        }

        /// <summary>
        /// The colour for a column of a stage with <paramref name="columns"/> keys: the colour of the key it's played with.
        /// </summary>
        public static Color4 ColourFor(int column, int columns)
        {
            var keys = DefaultKeysFor(columns);
            return column >= 0 && column < keys.Length ? KEY_COLOURS[keys[column]] : Color4.White;
        }

        private readonly Dictionary<int, Color4> columnColours = new Dictionary<int, Color4>();

        /// <summary>
        /// How see-through a classic long note's body is, so its head still stands out.
        /// </summary>
        private const float body_opacity = 0.8f;

        private WhitePixelSource? whitePixelSource;

        public void ApplyToDrawableRuleset(DrawableRuleset<ManiaHitObject> drawableRuleset)
        {
            // Gives a plain white picture to draw classic long-note bodies with.
            drawableRuleset.Overlays.Add(whitePixelSource = new WhitePixelSource());

            int first = 0;

            foreach (var stage in ((ManiaBeatmap)drawableRuleset.Beatmap).Stages)
            {
                for (int i = 0; i < stage.Columns; i++)
                    columnColours[first + i] = ColourFor(i, stage.Columns);

                first += stage.Columns;
            }

            // Default skins colour notes, the column and its key light from the column's accent colour.
            // A skin sets that when it loads, so keep putting this one back.
            foreach (var column in ((ManiaPlayfield)drawableRuleset.Playfield).Stages.SelectMany(s => s.Columns))
            {
                if (!columnColours.TryGetValue(column.Index, out var colour))
                    continue;

                column.AccentColour.BindValueChanged(c =>
                {
                    if (c.NewValue != colour)
                        column.AccentColour.Value = colour;
                }, true);
            }
        }

        // Classic-style skins, and maps that bring their own skin, draw notes from pictures that ignore the accent colour.
        // Tinting a picture mixes the colour with it, so the same colour looked different from skin to skin and from
        // column to column (a skin can use a different picture for the middle key). So the picture is hidden behind a
        // plain bar of the exact colour instead, and a long note's body is drawn plain. Pieces are made again when the
        // skin changes, which also updates the note's state, so this runs then too.
        public void ApplyToDrawableHitObject(DrawableHitObject drawable)
            => drawable.ApplyCustomUpdateState += (d, _) => colourClassicPieces(d);

        private void colourClassicPieces(DrawableHitObject drawable)
        {
            if (drawable.HitObject is not ManiaHitObject maniaObject || !columnColours.TryGetValue(maniaObject.Column, out var colour))
                return;

            drawExactColours(drawable, colour);
        }

        private void drawExactColours(DrawableHitObject drawable, Color4 colour)
        {
            foreach (var piece in drawable.ChildrenOfType<LegacyNotePiece>().ToList())
            {
                // The piece holds the skin's picture in a container that sizes the note.
                var holder = piece.ChildrenOfType<Container>().FirstOrDefault();

                if (holder == null)
                    continue;

                var bar = holder.Children.OfType<ColourBar>().FirstOrDefault();

                if (bar == null)
                {
                    foreach (var picture in holder.Children.ToList())
                    {
                        // Still there, so the note keeps the skin's height.
                        picture.AlwaysPresent = true;
                        picture.Alpha = 0;
                    }

                    holder.Add(bar = new ColourBar { RelativeSizeAxes = Axes.Both });
                }

                bar.Colour = colour;
            }

            Texture? white = whitePixelSource?.WhitePixel;

            foreach (var body in drawable.ChildrenOfType<LegacyBodyPiece>().ToList())
            {
                body.Colour = colour.Opacity(body_opacity);

                if (white == null)
                    continue;

                // An animation keeps setting its sprite's picture, so give it a single plain frame instead.
                var animations = body.ChildrenOfType<TextureAnimation>().ToList();

                foreach (var animation in animations)
                {
                    if (animation.FrameCount == 1 && animation.CurrentFrame == white)
                        continue;

                    animation.ClearFrames();
                    animation.AddFrame(white);
                }

                foreach (var sprite in body.ChildrenOfType<Sprite>().Where(s => !animations.Any(a => isInside(s, a))))
                    sprite.Texture = white;
            }
        }

        private static bool isInside(Drawable drawable, Drawable ancestor)
        {
            for (var d = drawable.Parent; d != null; d = d.Parent)
            {
                if (d == ancestor)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// A plain bar of a column's colour, drawn over a classic note in place of the skin's picture.
        /// </summary>
        public partial class ColourBar : Box
        {
        }

        private partial class WhitePixelSource : Drawable
        {
            [Resolved]
            private IRenderer renderer { get; set; } = null!;

            public Texture? WhitePixel => IsLoaded ? renderer.WhitePixel : null;
        }
    }
}
