// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
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
        public override LocalisableString Description => "Every key's column gets its own colour.";

        /// <summary>
        /// Ten clearly different colours in rainbow order, enough for the most keys a stage can have.
        /// </summary>
        public static readonly Color4[] PALETTE =
        {
            Color4Extensions.FromHex("ff3b3b"), // red
            Color4Extensions.FromHex("ff8a1f"), // orange
            Color4Extensions.FromHex("ffd60a"), // yellow
            Color4Extensions.FromHex("a6e22e"), // lime
            Color4Extensions.FromHex("2ecc71"), // green
            Color4Extensions.FromHex("1ed3e0"), // cyan
            Color4Extensions.FromHex("3b82f6"), // blue
            Color4Extensions.FromHex("6d5bff"), // indigo
            Color4Extensions.FromHex("b65cff"), // purple
            Color4Extensions.FromHex("ff4fb8"), // pink
        };

        /// <summary>
        /// The colour for a column of a stage with <paramref name="columns"/> keys. Fewer keys take colours spread
        /// across the whole palette, so neighbouring keys stay far apart. Always the same for the same key count.
        /// </summary>
        public static Color4 ColourFor(int column, int columns)
        {
            if (columns <= 1)
                return PALETTE[0];

            int index = (int)Math.Round(column * (PALETTE.Length - 1) / (double)(columns - 1), MidpointRounding.AwayFromZero);
            return PALETTE[Math.Clamp(index, 0, PALETTE.Length - 1)];
        }

        private readonly Dictionary<int, Color4> columnColours = new Dictionary<int, Color4>();

        public void ApplyToDrawableRuleset(DrawableRuleset<ManiaHitObject> drawableRuleset)
        {
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

        // Classic-style skins draw notes from images and ignore the accent colour, so tint those images instead.
        // Their pieces are made again when the skin changes, which also updates the note's state, so tint then too.
        public void ApplyToDrawableHitObject(DrawableHitObject drawable)
            => drawable.ApplyCustomUpdateState += (d, _) => tintClassicPieces(d);

        private void tintClassicPieces(DrawableHitObject drawable)
        {
            if (drawable.HitObject is not ManiaHitObject maniaObject || !columnColours.TryGetValue(maniaObject.Column, out var colour))
                return;

            foreach (var piece in drawable.ChildrenOfType<Drawable>().Where(p => p is LegacyNotePiece or LegacyBodyPiece))
                piece.Colour = colour;
        }
    }
}
