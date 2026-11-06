// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModRewindOnNotSS : OsuModRewind
    {
        public override string Name => "Rewind on Not SS";
        public override string Acronym => "RS";
        public override LocalisableString Description => "Rewinds half a second before anything that isn't perfect.";

        // The same rule as the Perfect mod ("SS or quit"), so this triggers on exactly what would cost an SS.
        public override bool ShouldRewind(JudgementResult result)
            => (isRelevant(result.Judgement.MinResult) || isRelevant(result.Judgement.MaxResult) || isRelevant(result.Type))
               && result.Type != result.Judgement.MaxResult;

        private static bool isRelevant(HitResult result) => result.AffectsAccuracy() || result.AffectsCombo();
    }
}
