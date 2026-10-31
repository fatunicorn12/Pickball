using System.Collections.Generic;

namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// Fluent builder for a <see cref="ShotPlan"/>, so the plan reads like the shot list's table
    /// rows: beats in order, then the cues. Cue times are "beat index + seconds" (beat 0 by
    /// default; <see cref="End"/> is the shot's end), so a cue tied to a line moves with it when the
    /// line's audio retimes the shot.
    /// </summary>
    public sealed class ShotPlanBuilder
    {
        readonly string _id;
        readonly int _scene;
        readonly string _framing;
        readonly float _lensStart, _lensEnd;
        readonly string _subject;
        readonly float _distanceH;
        readonly List<Beat> _beats = new List<Beat>();
        readonly List<ActionCue> _actions = new List<ActionCue>();
        readonly List<MarkCue> _marks = new List<MarkCue>();
        readonly List<CurveKey> _keys = new List<CurveKey>();

        /// <summary>Starts a shot.</summary>
        public ShotPlanBuilder(string id, int scene, float lensStart, float lensEnd, string subject,
            float placeholderDistanceH, string framing)
        {
            _id = id;
            _scene = scene;
            _lensStart = lensStart;
            _lensEnd = lensEnd;
            _subject = subject;
            _distanceH = placeholderDistanceH;
            _framing = framing;
        }

        /// <summary>The shot's end, as a beat index for cues.</summary>
        public int End => _beats.Count;

        /// <summary>Adds wordless action time.</summary>
        public ShotPlanBuilder Wordless(float seconds) { _beats.Add(Beat.Wordless(seconds)); return this; }

        /// <summary>Adds a hold.</summary>
        public ShotPlanBuilder Hold(float seconds) { _beats.Add(Beat.Hold(seconds)); return this; }

        /// <summary>Adds a line, optionally with a hold between its cards.</summary>
        public ShotPlanBuilder Line(string lineId, float midHold = 0f) { _beats.Add(Beat.Line(lineId, midHold)); return this; }

        /// <summary>Adds a static hold with a line inside it.</summary>
        public ShotPlanBuilder LineInHold(string lineId, float hold, float offset)
        {
            _beats.Add(Beat.LineInHold(lineId, hold, offset));
            return this;
        }

        /// <summary>A verb from beat <paramref name="beat"/> + <paramref name="at"/> s.</summary>
        public ShotPlanBuilder Act(string actor, string verb, int beat = 0, float at = 0f)
        {
            _actions.Add(new ActionCue(actor, verb, beat, at));
            return this;
        }

        /// <summary>A cut to a mark.</summary>
        public ShotPlanBuilder Place(string actor, PlanMark mark, int beat = 0, float at = 0f)
        {
            _marks.Add(new MarkCue(actor, mark, beat, at, 0f, 0f, MarkEase.Linear));
            return this;
        }

        /// <summary>A move to a mark over <paramref name="seconds"/>.</summary>
        public ShotPlanBuilder Move(string actor, PlanMark mark, float seconds, MarkEase ease = MarkEase.EaseInOut,
            int beat = 0, float at = 0f)
        {
            _marks.Add(new MarkCue(actor, mark, beat, at, seconds, 0f, ease));
            return this;
        }

        /// <summary>A linear walk to a mark at <paramref name="speed"/> m/s.</summary>
        public ShotPlanBuilder Walk(string actor, PlanMark mark, float speed, int beat = 0, float at = 0f)
        {
            _marks.Add(new MarkCue(actor, mark, beat, at, 0f, speed, MarkEase.Linear));
            return this;
        }

        /// <summary>A look/VFX key.</summary>
        public ShotPlanBuilder Key(string channel, float value, int beat = 0, float at = 0f)
        {
            _keys.Add(new CurveKey(channel, beat, at, value));
            return this;
        }

        /// <summary>The finished shot.</summary>
        public ShotPlan Build() => new ShotPlan(_id, _scene, _framing, _lensStart, _lensEnd, _subject, _distanceH,
            _beats.ToArray(), _actions.ToArray(), _marks.ToArray(), _keys.ToArray());
    }
}
