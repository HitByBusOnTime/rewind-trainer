// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
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
