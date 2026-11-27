// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class ConvertedStarRatingsTest
    {
        private static BeatmapInfo osuMap(string hash) => new BeatmapInfo(new OsuRuleset().RulesetInfo) { MD5Hash = hash, StarRating = 4.2 };

        [Test]
        public void TestOnlyConvertsHaveOne()
        {
            var store = new ConvertedStarRatings(null);
            var map = osuMap("a");
            var mania = new ManiaRuleset().RulesetInfo;

            Assert.That(store.Get(map, mania), Is.Null, "not worked out yet");

            store.Set(map, mania, 2.5);
            Assert.That(store.Get(map, mania), Is.EqualTo(2.5));

            // In its own mode a map's stored rating is the right one.
            Assert.That(store.Get(map, new OsuRuleset().RulesetInfo), Is.Null);
            Assert.That(store.Get(map, null), Is.Null);
        }

        [Test]
        public void TestTheyAreKeptBetweenLaunches()
        {
            var mania = new ManiaRuleset().RulesetInfo;

            using (var storage = new TemporaryNativeStorage("converted-stars"))
            {
                var first = new ConvertedStarRatings(storage);
                first.Set(osuMap("a"), mania, 2.5);
                first.Set(osuMap("b"), mania, 1.25);
                first.Save();

                var next = new ConvertedStarRatings(storage);
                Assert.That(next.Get(osuMap("a"), mania), Is.EqualTo(2.5));
                Assert.That(next.Get(osuMap("b"), mania), Is.EqualTo(1.25));
                Assert.That(next.Get(osuMap("c"), mania), Is.Null);
            }
        }
    }
}
