// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Rulesets;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// Star ratings of maps played as converts in another mode (an osu! map in mania, say), which the map itself doesn't store:
    /// it only has the rating for its own mode. Saved to a file, since working them out takes a while.
    /// </summary>
    public class ConvertedStarRatings
    {
        public const string FILENAME = "converted-stars.json";

        private readonly Storage? storage;
        private readonly ConcurrentDictionary<string, double> stars = new ConcurrentDictionary<string, double>();
        private readonly object saveLock = new object();

        /// <param name="storage">Where to keep them between launches, or null to keep them in memory only.</param>
        public ConvertedStarRatings(Storage? storage)
        {
            this.storage = storage;

            if (storage == null || !storage.Exists(FILENAME))
                return;

            try
            {
                using (var stream = storage.GetStream(FILENAME))
                using (var reader = new StreamReader(stream))
                {
                    var saved = JsonConvert.DeserializeObject<Dictionary<string, double>>(reader.ReadToEnd());

                    if (saved != null)
                    {
                        foreach (var (k, v) in saved)
                            stars[k] = v;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Couldn't read the saved converted star ratings");
            }
        }

        private static string key(BeatmapInfo beatmap, string rulesetShortName) => $"{beatmap.MD5Hash}:{rulesetShortName}";

        /// <summary>
        /// The map's star rating in <paramref name="ruleset"/> if it's a convert there and has been worked out, otherwise null.
        /// </summary>
        public double? Get(BeatmapInfo beatmap, RulesetInfo? ruleset)
        {
            if (ruleset == null || beatmap.Ruleset.ShortName == ruleset.ShortName)
                return null;

            return stars.TryGetValue(key(beatmap, ruleset.ShortName), out double value) ? value : null;
        }

        public void Set(BeatmapInfo beatmap, RulesetInfo ruleset, double value) => stars[key(beatmap, ruleset.ShortName)] = value;

        public void Save()
        {
            if (storage == null)
                return;

            lock (saveLock)
            {
                try
                {
                    using (var stream = storage.CreateFileSafely(FILENAME))
                    using (var writer = new StreamWriter(stream))
                        writer.Write(JsonConvert.SerializeObject(new Dictionary<string, double>(stars)));
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Couldn't save the converted star ratings");
                }
            }
        }
    }
}
