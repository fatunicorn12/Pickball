using System.Collections.Generic;

namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// One scene with every beat placed in time: what <see cref="TimelineLayout"/> returns and the
    /// timeline builder turns into clips. All times are seconds from the scene's start.
    /// </summary>
    public sealed class SceneLayout
    {
        /// <summary>Creates a layout.</summary>
        public SceneLayout(int scene, double duration, IReadOnlyList<ShotSpan> shots, IReadOnlyList<LineSpan> lines,
            IReadOnlyList<ActionSpan> actions, IReadOnlyList<MarkSpan> marks, IReadOnlyList<CurveKeySpan> keys)
        {
            Scene = scene;
            Duration = duration;
            Shots = shots;
            Lines = lines;
            Actions = actions;
            Marks = marks;
            Keys = keys;
        }

        /// <summary>Scene number.</summary>
        public int Scene { get; }

        /// <summary>Scene length: the end of the last shot.</summary>
        public double Duration { get; }

        /// <summary>Shots in order.</summary>
        public IReadOnlyList<ShotSpan> Shots { get; }

        /// <summary>Lines in order.</summary>
        public IReadOnlyList<LineSpan> Lines { get; }

        /// <summary>Verbs, sorted by actor then time.</summary>
        public IReadOnlyList<ActionSpan> Actions { get; }

        /// <summary>Blocking cues, sorted by time then plan order.</summary>
        public IReadOnlyList<MarkSpan> Marks { get; }

        /// <summary>Look keys, sorted by time.</summary>
        public IReadOnlyList<CurveKeySpan> Keys { get; }

        /// <summary>The line span with this id, or null.</summary>
        public LineSpan FindLine(string id)
        {
            for (int i = 0; i < Lines.Count; i++)
                if (Lines[i].Id == id) return Lines[i];
            return null;
        }

        /// <summary>The shot span with this id, or null.</summary>
        public ShotSpan FindShot(string id)
        {
            for (int i = 0; i < Shots.Count; i++)
                if (Shots[i].Id == id) return Shots[i];
            return null;
        }
    }
}
