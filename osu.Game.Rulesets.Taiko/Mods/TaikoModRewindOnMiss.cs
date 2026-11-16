// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.Taiko.Mods
{
    public class TaikoModRewindOnMiss : TaikoModRewind
    {
        public override string Name => "Rewind on Miss";
        public override string Acronym => "RW";
        public override LocalisableString Description => "Rewinds half a second before a miss.";

        // The drumroll check runs first: it keeps track of every tick.
        public override bool ShouldRewind(JudgementResult result) => DrumRollWasSkipped(result) || IsMiss(result);
    }
}
