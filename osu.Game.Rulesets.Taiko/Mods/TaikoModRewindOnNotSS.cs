// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.Taiko.Mods
{
    public class TaikoModRewindOnNotSS : TaikoModRewind
    {
        public override string Name => "Rewind on Not SS";
        public override string Acronym => "RS";
        public override LocalisableString Description => "Rewinds half a second before anything that isn't perfect.";

        // The drumroll check runs first: it keeps track of every tick.
        public override bool ShouldRewind(JudgementResult result) => DrumRollWasSkipped(result) || CostsSS(result);
    }
}
