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
        public override LocalisableString Description => "Every column gets its own colour: the 1st is always red, the 2nd orange, and so on.";

        /// <summary>
        /// A colour for each column number, left to right, the same at every key count: the 1st column is red on 4K, 7K and 10K alike.
        /// Rainbow order first, so the usual key counts get the most different colours. More columns than this start over.
        /// </summary>
        public static readonly Color4[] COLUMN_COLOURS =
        {
            Color4Extensions.FromHex("ff3b3b"), // 1 red
            Color4Extensions.FromHex("ff8a1f"), // 2 orange
            Color4Extensions.FromHex("ffd60a"), // 3 yellow
            Color4Extensions.FromHex("2ecc71"), // 4 green
            Color4Extensions.FromHex("1ed3e0"), // 5 cyan
            Color4Extensions.FromHex("3b82f6"), // 6 blue
            Color4Extensions.FromHex("b65cff"), // 7 purple
            Color4Extensions.FromHex("ff4fb8"), // 8 pink
            Color4Extensions.FromHex("ffffff"), // 9 white
            Color4Extensions.FromHex("9e9e9e"), // 10 grey
        };

        /// <summary>
        /// The colour for a column: by its number alone, whatever the key count.
        /// </summary>
        public static Color4 ColourFor(int column, int columns) => COLUMN_COLOURS[((column % COLUMN_COLOURS.Length) + COLUMN_COLOURS.Length) % COLUMN_COLOURS.Length];

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
