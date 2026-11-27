// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Screens.Select;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class YourMapBadgeTest
    {
        [Test]
        public void TestKinds()
        {
            var set = new BeatmapSetInfo { OnlineID = 5 };
            var official = new BeatmapInfo { OnlineID = 10, BeatmapSet = set };
            var mine = new BeatmapInfo { OnlineID = -1, BeatmapSet = set };
            var alsoMine = new BeatmapInfo { OnlineID = 0, BeatmapSet = set };
            set.Beatmaps.Add(official);
            set.Beatmaps.Add(mine);
            set.Beatmaps.Add(alsoMine);

            Assert.That(YourMapBadge.KindOf(mine), Is.EqualTo(YourMapBadge.BadgeKind.YourMap));
            Assert.That(YourMapBadge.KindOf(alsoMine), Is.EqualTo(YourMapBadge.BadgeKind.YourMap));
            Assert.That(YourMapBadge.KindOf(official), Is.EqualTo(YourMapBadge.BadgeKind.HasYourMap));
            Assert.That(YourMapBadge.KindOf(set), Is.EqualTo(YourMapBadge.BadgeKind.HasYourMap));

            var plainSet = new BeatmapSetInfo { OnlineID = 6 };
            var plain = new BeatmapInfo { OnlineID = 11, BeatmapSet = plainSet };
            plainSet.Beatmaps.Add(plain);
            Assert.That(YourMapBadge.KindOf(plain), Is.EqualTo(YourMapBadge.BadgeKind.None));
            Assert.That(YourMapBadge.KindOf(plainSet), Is.EqualTo(YourMapBadge.BadgeKind.None));

            // The game's own sample map isn't the player's.
            var builtIn = new BeatmapSetInfo { OnlineID = -1, Protected = true };
            var sample = new BeatmapInfo { OnlineID = 0, BeatmapSet = builtIn };
            builtIn.Beatmaps.Add(sample);
            Assert.That(YourMapBadge.KindOf(sample), Is.EqualTo(YourMapBadge.BadgeKind.None));
        }
    }
}
