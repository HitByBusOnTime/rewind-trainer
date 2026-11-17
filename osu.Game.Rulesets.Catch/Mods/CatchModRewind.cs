// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Catch.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Catch.Mods
{
    public abstract class CatchModRewind : ModRewind<CatchHitObject, CatchAction>, IUpdatableByPlayfield
    {
        /// <summary>
        /// Where the catcher was as time went forwards, so a rewind can put it back. Otherwise it stays where the miss was,
        /// and can't move during the get-ready pause, so the next fruit may be too far away to reach.
        /// </summary>
        private readonly List<(double time, float x)> catcherPositions = new List<(double time, float x)>();

        private double lastTime = double.NegativeInfinity;

        public void Update(Playfield playfield)
        {
            double time = playfield.Time.Current;

            // Going back in time forgets where the catcher went after; only moving forwards records anything.
            while (catcherPositions.Count > 0 && catcherPositions[^1].time > time)
                catcherPositions.RemoveAt(catcherPositions.Count - 1);

            if (time > lastTime)
                catcherPositions.Add((time, ((CatchPlayfield)playfield).CatcherArea.Catcher.X));

            lastTime = time;
        }

        protected override void OnRewindSettled(DrawableRuleset<CatchHitObject> drawableRuleset)
        {
            double time = drawableRuleset.FrameStableClock.CurrentTime;

            for (int i = catcherPositions.Count - 1; i >= 0; i--)
            {
                if (catcherPositions[i].time <= time)
                {
                    ((CatchPlayfield)drawableRuleset.Playfield).CatcherArea.SetCatcherPosition(catcherPositions[i].x);
                    return;
                }
            }
        }

        // Catching only depends on where the catcher is, so no key press can catch anything while time stands still.
        // Ignoring move keys only meant a key pressed during a rewind and held didn't move the catcher afterwards.
        protected override bool IgnoreDuringRewind(CatchAction action) => false;
    }
}
