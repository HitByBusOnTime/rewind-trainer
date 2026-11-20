// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Mania.Tests.Mods
{
    public partial class TestSceneManiaModColumnColours : ModTestScene
    {
        protected override Ruleset CreatePlayerRuleset() => new ManiaRuleset();

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Test]
        public void TestEveryKeyCountGetsDistinctColours()
        {
            for (int keys = 1; keys <= ManiaRuleset.MAX_STAGE_KEYS; keys++)
            {
                var colours = Enumerable.Range(0, keys).Select(i => ManiaModColumnColours.ColourFor(i, keys)).ToList();
                Assert.That(colours.Distinct().Count(), Is.EqualTo(keys), $"{keys} keys");
            }
        }

        [Test]
        public void TestKeysMatchTheGamesDefaultBindings()
        {
            for (int keys = 1; keys <= ManiaRuleset.MAX_STAGE_KEYS; keys++)
            {
                var expected = new ManiaRuleset().GetDefaultKeyBindings(keys).Where(b => b.KeyCombination.Keys.First() != InputKey.None)
                                                 .Select(b => b.KeyCombination.Keys.First()).ToArray();
                Assert.That(ManiaModColumnColours.DefaultKeysFor(keys), Is.EqualTo(expected), $"{keys} keys");
            }
        }

        [Test]
        public void TestAKeyKeepsItsColourAtEveryKeyCount()
        {
            Color4 colourOf(InputKey key) => ManiaModColumnColours.KEY_COLOURS[key];

            // F, the left index finger, on 4K (D F J K), 5K (D F Space J K), 7K (S D F Space J K L) and 8K (A S D F J K L ;).
            Assert.That(ManiaModColumnColours.ColourFor(1, 4), Is.EqualTo(colourOf(InputKey.F)));
            Assert.That(ManiaModColumnColours.ColourFor(1, 5), Is.EqualTo(colourOf(InputKey.F)));
            Assert.That(ManiaModColumnColours.ColourFor(2, 7), Is.EqualTo(colourOf(InputKey.F)));
            Assert.That(ManiaModColumnColours.ColourFor(3, 8), Is.EqualTo(colourOf(InputKey.F)));

            // The whole of 7K, and the middle key being the same on 5K and 7K.
            Assert.That(Enumerable.Range(0, 7).Select(i => ManiaModColumnColours.ColourFor(i, 7)),
                Is.EqualTo(new[] { InputKey.S, InputKey.D, InputKey.F, InputKey.Space, InputKey.J, InputKey.K, InputKey.L }.Select(colourOf)));
            Assert.That(ManiaModColumnColours.ColourFor(2, 5), Is.EqualTo(ManiaModColumnColours.ColourFor(3, 7)));
        }

        private const int keys = 7;

        private static Beatmap sevenKeyMap() => new Beatmap
        {
            Difficulty = new BeatmapDifficulty { CircleSize = keys },
            HitObjects = Enumerable.Range(0, keys).Select(i => (HitObject)new Note { StartTime = 2000 + i * 100, Column = i }).ToList(),
        };

        private void useSkin(bool classic) => AddStep(classic ? "classic skin" : "default skin", () =>
        {
            if (classic)
                skins.CurrentSkinInfo.Value = skins.DefaultClassicSkin.SkinInfo;
            else
                skins.CurrentSkinInfo.SetDefault();
        });

        private IEnumerable<DrawableNote> visibleNotes() => Player.DrawableRuleset.Playfield.ChildrenOfType<DrawableNote>().Where(n => n.IsAlive && n.HitObject != null);

        [Test]
        public void TestDefaultSkinColoursColumnsAndNotes()
        {
            useSkin(false);

            CreateModTest(new ModTestData
            {
                Mod = new ManiaModColumnColours(),
                Autoplay = true,
                CreateBeatmap = sevenKeyMap,
                PassCondition = () =>
                {
                    var columns = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).Stages.Single().Columns;
                    var notes = visibleNotes().ToList();

                    return columns.Length == keys
                           && columns.All(c => c.AccentColour.Value == ManiaModColumnColours.ColourFor(c.Index, keys))
                           && notes.Count == keys
                           && notes.All(n => n.AccentColour.Value == ManiaModColumnColours.ColourFor(n.HitObject.Column, keys));
                },
            });
        }

        /// <summary>
        /// Whether a classic note piece shows exactly this colour: its own picture hidden, a bar of the colour in its place, and nothing tinting it.
        /// </summary>
        private static bool showsExactly(LegacyNotePiece piece, Color4 colour)
        {
            var bar = piece.ChildrenOfType<ManiaModColumnColours.ColourBar>().SingleOrDefault();

            return bar != null
                   && bar.Colour.Equals((ColourInfo)colour)
                   && piece.Colour.Equals((ColourInfo)Color4.White)
                   && bar.Parent is Container holder && holder.Children.Where(c => c != bar).All(c => c.Alpha == 0);
        }

        [Test]
        public void TestClassicSkinNotesAreTheExactColour()
        {
            useSkin(true);

            CreateModTest(new ModTestData
            {
                Mod = new ManiaModColumnColours(),
                Autoplay = true,
                CreateBeatmap = sevenKeyMap,
                // Steps written below run before this is checked, including switching the skin back, so check in a step instead.
                PassCondition = () => true,
            });

            // The classic skin draws the middle key with a different picture, which tinting used to mix into its colour.
            AddUntilStep("every note shows its column's exact colour", () =>
            {
                var notes = visibleNotes().ToList();

                return notes.Count == keys
                       && notes.All(n =>
                       {
                           var pieces = n.ChildrenOfType<LegacyNotePiece>().ToList();
                           return pieces.Count > 0 && pieces.All(p => showsExactly(p, ManiaModColumnColours.ColourFor(n.HitObject.Column, keys)));
                       });
            });

            AddStep("back to the default skin", () => skins.CurrentSkinInfo.SetDefault());
        }

        [Test]
        public void TestClassicSkinLongNotesAreTheExactColour()
        {
            useSkin(true);

            CreateModTest(new ModTestData
            {
                Mod = new ManiaModColumnColours(),
                Autoplay = true,
                CreateBeatmap = () => new Beatmap
                {
                    Difficulty = new BeatmapDifficulty { CircleSize = keys },
                    HitObjects = new List<HitObject> { new HoldNote { StartTime = 2000, Duration = 1500, Column = 3 } },
                },
                // Steps written below run before this is checked, including switching the skin back, so check in a step instead.
                PassCondition = () => true,
            });

            AddUntilStep("the head, tail and body show their key's exact colour", () =>
            {
                var holdNote = Player.DrawableRuleset.Playfield.ChildrenOfType<DrawableHoldNote>().FirstOrDefault(h => h.IsAlive && h.HitObject != null);

                if (holdNote == null)
                    return false;

                // How many keys a map with only this note gets is up to the game, so go by the stage it made.
                var stage = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).Stages.Single();
                Color4 expected = ManiaModColumnColours.ColourFor(3, stage.Columns.Length);
                var pieces = holdNote.ChildrenOfType<LegacyNotePiece>().ToList();
                var body = holdNote.ChildrenOfType<LegacyBodyPiece>().SingleOrDefault();
                var bodySprites = body?.ChildrenOfType<Sprite>().ToList();
                var white = pieces.SelectMany(p => p.ChildrenOfType<ManiaModColumnColours.ColourBar>()).FirstOrDefault()?.Texture;

                return pieces.Count >= 2 && pieces.All(p => showsExactly(p, expected))
                       && body != null && body.Colour.Equals((ColourInfo)expected.Opacity(0.8f))
                       && white != null && bodySprites!.Count > 0 && bodySprites.All(s => s.Texture == white);
            });

            AddStep("back to the default skin", () => skins.CurrentSkinInfo.SetDefault());
        }

        [Test]
        public void TestTwoStagesEachUseTheColours()
        {
            useSkin(false);

            CreateModTest(new ModTestData
            {
                Mods = new Mod[] { new ManiaModColumnColours(), new ManiaModDualStages() },
                Autoplay = true,
                CreateBeatmap = () => new Beatmap
                {
                    Difficulty = new BeatmapDifficulty { CircleSize = 4 },
                    HitObjects = new List<HitObject> { new Note { StartTime = 2000 } },
                },
                PassCondition = () =>
                {
                    var stages = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).Stages;

                    return stages.Count == 2
                           && stages.All(s => s.Columns.Select((c, i) => c.AccentColour.Value == ManiaModColumnColours.ColourFor(i, s.Columns.Length)).All(ok => ok));
                },
            });
        }
    }
}
