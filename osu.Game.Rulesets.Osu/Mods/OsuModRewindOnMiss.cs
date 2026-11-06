// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModRewindOnMiss : OsuModRewind
    {
        public override string Name => "Rewind on Miss";
        public override string Acronym => "RW";
        public override LocalisableString Description => "Rewinds 1 second before a miss.";

        // Not only combo breaks: dropping the end of a slider is a miss which leaves the combo alone.
        // Bonus spins and the slider's own display-only result aren't things that can be missed.
        public override bool ShouldRewind(JudgementResult result)
            => !result.IsHit && !result.Judgement.MaxResult.IsBonus() && result.Judgement.MaxResult != HitResult.IgnoreHit;
    }
}
