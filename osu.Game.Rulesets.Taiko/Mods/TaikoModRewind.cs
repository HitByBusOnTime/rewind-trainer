// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Taiko.Objects;

namespace osu.Game.Rulesets.Taiko.Mods
{
    public abstract class TaikoModRewind : ModRewind<TaikoHitObject, TaikoAction>
    {
        [SettingSource("Drumroll ticks to hit", "A drumroll counts as a miss if you hit less than this percentage of its ticks. 0 turns this off.")]
        public BindableInt DrumRollTicksToHit { get; } = new BindableInt(50)
        {
            MinValue = 0,
            MaxValue = 100,
            Precision = 10,
        };

        private readonly Dictionary<DrumRollTick, bool> tickHits = new Dictionary<DrumRollTick, bool>();
        private readonly HashSet<DrumRoll> decided = new HashSet<DrumRoll>();

        /// <summary>
        /// The game counts drumroll ticks as a bonus, so skipping a whole drumroll is never a miss on its own.
        /// This decides once every tick of a drumroll has been judged. A rewind never lands inside a drumroll,
        /// so each new pass starts from its first tick.
        /// </summary>
        protected bool DrumRollWasSkipped(JudgementResult result)
        {
            if (DrumRollTicksToHit.Value == 0 || result.HitObject is not DrumRollTick tick)
                return false;

            var ticks = tick.Parent.NestedHitObjects.OfType<DrumRollTick>().ToList();

            if (ticks.Count == 0)
                return false;

            if (tick == ticks[0])
            {
                foreach (var t in ticks)
                    tickHits.Remove(t);
                decided.Remove(tick.Parent);
            }

            tickHits[tick] = result.IsHit;

            if (decided.Contains(tick.Parent) || ticks.Any(t => !tickHits.ContainsKey(t)))
                return false;

            decided.Add(tick.Parent);

            int hits = ticks.Count(t => tickHits[t]);
            return hits * 100 < DrumRollTicksToHit.Value * ticks.Count;
        }
    }
}
