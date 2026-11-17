// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;

namespace osu.Game.Tests.NonVisual
{
    /// <summary>
    /// Measures where rewinds land on real maps: a miss is simulated on every object, and the warning
    /// before the next object and the distance back are reported. Maps come from REWIND_PROBE_MAPS (paths separated by ';').
    /// </summary>
    [TestFixture]
    [Explicit("Measures real maps from the REWIND_PROBE_MAPS environment variable.")]
    public class RewindLandingProbe
    {
        private static Ruleset rulesetFor(int mode) => mode switch
        {
            1 => new TaikoRuleset(),
            2 => new CatchRuleset(),
            3 => new ManiaRuleset(),
            _ => new OsuRuleset(),
        };

        private static double percentile(List<double> sorted, double p) => sorted.Count == 0 ? double.NaN : sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(p * sorted.Count))];

        [Test]
        public void Measure()
        {
            string? maps = Environment.GetEnvironmentVariable("REWIND_PROBE_MAPS");

            if (string.IsNullOrEmpty(maps))
                Assert.Ignore("REWIND_PROBE_MAPS isn't set.");

            foreach (string path in maps.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                Beatmap decoded;

                using (var stream = File.OpenRead(path))
                using (var reader = new LineBufferedReader(stream))
                    decoded = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);

                var ruleset = rulesetFor(decoded.BeatmapInfo.Ruleset.OnlineID);
                var playable = new FlatWorkingBeatmap(decoded).GetPlayableBeatmap(ruleset.RulesetInfo);
                var objects = playable.HitObjects.OrderBy(o => o.StartTime).ToList();
                double[] starts = objects.Select(o => o.StartTime).ToArray();

                var warnings = new List<double>();
                var distances = new List<double>();

                foreach (var missed in objects)
                {
                    double landing = ModRewind.GetRewindTarget(objects, missed.StartTime, 500);

                    // The next object which starts at or after the landing.
                    int next = Array.BinarySearch(starts, landing);
                    if (next < 0) next = ~next;

                    warnings.Add(next < starts.Length ? starts[next] - landing : double.PositiveInfinity);
                    distances.Add(missed.StartTime - landing);
                }

                warnings.Sort();
                distances.Sort();

                int under(double ms) => warnings.Count(w => w < ms);
                string pct(int n) => $"{n} ({100.0 * n / warnings.Count:N1}%)";

                TestContext.Progress.WriteLine(
                    $"PROBE {ruleset.ShortName,-6} {decoded.Metadata.Title} [{decoded.BeatmapInfo.DifficultyName}] n={objects.Count}: "
                    + $"warning <100ms {pct(under(100))}, <200ms {pct(under(200))}, <300ms {pct(under(300))} | "
                    + $"back median {percentile(distances, 0.5):N0}ms, 95% {percentile(distances, 0.95):N0}ms");
            }
        }
    }
}
