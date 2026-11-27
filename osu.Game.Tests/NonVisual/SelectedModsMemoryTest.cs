// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class SelectedModsMemoryTest
    {
        private static Ruleset? rulesetFor(string shortName) => shortName switch
        {
            "mania" => new ManiaRuleset(),
            "osu" => new OsuRuleset(),
            _ => null,
        };

        [Test]
        public void TestModsAndTheirSettingsComeBack()
        {
            using (var storage = new TemporaryNativeStorage("selected-mods-memory"))
            {
                SelectedModsMemory.Save(storage, "mania", new Mod[]
                {
                    new ManiaModRewindOnMiss { ChordLimitMilliseconds = { Value = 75 }, RewindOnStrayPresses = { Value = false } },
                    new ManiaModColumnColours(),
                    new ManiaModAutoplay(),
                });

                var restored = SelectedModsMemory.Load(storage, new ManiaRuleset(), rulesetFor);

                Assert.That(restored.Select(m => m.Acronym), Is.EquivalentTo(new[] { "RW", "CC" }), "Autoplay isn't played by the player, so it's left out");

                var rewind = restored.OfType<ManiaModRewindOnMiss>().Single();
                Assert.That(rewind.ChordLimitMilliseconds.Value, Is.EqualTo(75));
                Assert.That(rewind.RewindOnStrayPresses.Value, Is.False);
            }
        }

        [Test]
        public void TestModsFollowAChangeOfMode()
        {
            using (var storage = new TemporaryNativeStorage("selected-mods-memory"))
            {
                SelectedModsMemory.Save(storage, "mania", new Mod[] { new ManiaModRewindOnMiss { RewindSeconds = { Value = 1 } }, new ManiaModColumnColours() });

                var restored = SelectedModsMemory.Load(storage, new OsuRuleset(), rulesetFor);

                // Column Colours is mania only; Rewind on Miss exists everywhere and keeps its shared settings.
                Assert.That(restored.Single(), Is.InstanceOf<OsuModRewindOnMiss>());
                Assert.That(((ModRewind)restored.Single()).RewindSeconds.Value, Is.EqualTo(1));
            }
        }

        [Test]
        public void TestNothingSavedGivesNothing()
        {
            using (var storage = new TemporaryNativeStorage("selected-mods-memory"))
                Assert.That(SelectedModsMemory.Load(storage, new ManiaRuleset(), rulesetFor), Is.Empty);
        }
    }
}
