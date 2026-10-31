using System;
using System.Collections.Generic;
using Pickleball.Cutscene.Script;

namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// Places a scene's shots, beats, lines and cards in time. Pure arithmetic over the plan and the
    /// line specs, so "assigning a clip shifts every later beat and nothing earlier" is a unit test.
    /// <list type="bullet">
    /// <item>Wordless beats and holds take their seconds.</item>
    /// <item>A line takes its speech (the clip's length, or the placeholder rule) plus the 0.3 s gap.
    /// A mid-line hold without audio is added between the cards; with audio it is assumed to be in
    /// the clip (ElevenLabs renders the <c>[pause]</c>), so the line is exactly the clip plus the gap.</item>
    /// <item>A line inside a hold starts at the hold's offset and the hold lasts at least as long as
    /// the line's slot.</item>
    /// <item>Cards share the spoken time in proportion to their characters.</item>
    /// </list>
    /// A missing clip is never an error: the placeholder rule applies.
    /// </summary>
    public static class TimelineLayout
    {
        /// <summary>Lays out one scene.</summary>
        /// <exception cref="ArgumentException">A line beat names an id <paramref name="lines"/> doesn't have.</exception>
        public static SceneLayout Layout(int scene, IReadOnlyList<ShotPlan> shots, IReadOnlyDictionary<string, LineSpec> lines)
        {
            if (shots == null) throw new ArgumentNullException(nameof(shots));
            if (lines == null) throw new ArgumentNullException(nameof(lines));

            var shotSpans = new List<ShotSpan>(shots.Count);
            var lineSpans = new List<LineSpan>();
            double t = 0;
            foreach (var shot in shots)
            {
                double shotStart = t;
                var beatStarts = new double[shot.Beats.Length];
                for (int i = 0; i < shot.Beats.Length; i++)
                {
                    var beat = shot.Beats[i];
                    beatStarts[i] = t;
                    switch (beat.Kind)
                    {
                        case BeatKind.Wordless:
                        case BeatKind.Hold:
                            t += beat.Seconds;
                            break;
                        case BeatKind.Line:
                        {
                            var span = PlaceLine(Spec(lines, beat.LineId, shot.Id), shot.Id, t, beat.MidHold);
                            lineSpans.Add(span);
                            t = span.End;
                            break;
                        }
                        case BeatKind.LineInHold:
                        {
                            var span = PlaceLine(Spec(lines, beat.LineId, shot.Id), shot.Id, t + beat.Offset, 0f);
                            lineSpans.Add(span);
                            t = Math.Max(t + beat.Seconds, span.End);
                            break;
                        }
                    }
                }
                shotSpans.Add(new ShotSpan(shot, shotStart, t, beatStarts));
            }
            double duration = t;

            var actions = LayoutActions(shotSpans, duration);
            var marks = new List<MarkSpan>();
            var keys = new List<CurveKeySpan>();
            int order = 0;
            foreach (var span in shotSpans)
            {
                foreach (var cue in span.Plan.Marks)
                    marks.Add(new MarkSpan(cue, Clamp(span.TimeOf(cue.Beat, cue.At), duration), order++));
                foreach (var key in span.Plan.Keys)
                    keys.Add(new CurveKeySpan(key.Channel, Clamp(span.TimeOf(key.Beat, key.At), duration), key.Value));
            }
            marks.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Order.CompareTo(b.Order));
            StableSortKeys(keys);

            return new SceneLayout(scene, duration, shotSpans, lineSpans, actions, marks, keys);
        }

        /// <summary>
        /// Places one line starting at <paramref name="start"/>. Public so the card timing rule is
        /// tested directly.
        /// </summary>
        public static LineSpan PlaceLine(LineSpec line, string shotId, double start, float midHold)
        {
            float speech = LineTiming.SpeechSeconds(line.DisplayChars, line.AudioSeconds);
            int cardCount = Math.Max(1, line.Cards.Length);
            int holdAfter = midHold > 0f
                ? (line.MidHoldAfterCard >= 0 ? line.MidHoldAfterCard : (cardCount > 1 ? 0 : cardCount - 1))
                : -1;

            // With audio the hold is inside the clip; without, it is added to the line.
            double spoken = line.HasAudio ? Math.Max(0.0, speech - (holdAfter >= 0 ? midHold : 0f)) : speech;
            double slotSpeech = line.HasAudio ? speech : speech + (holdAfter >= 0 ? midHold : 0f);

            int totalChars = 0;
            for (int i = 0; i < line.Cards.Length; i++) totalChars += line.Cards[i].Length;

            var cards = new CardSpan[cardCount];
            double c = start;
            var starts = new double[cardCount];
            for (int i = 0; i < cardCount; i++)
            {
                starts[i] = c;
                double share = line.Cards.Length == 0 || totalChars == 0
                    ? 1.0 / cardCount
                    : (double)line.Cards[i].Length / totalChars;
                c += spoken * share;
                if (i == holdAfter) c += midHold;
            }
            double speechEnd = start + slotSpeech;
            for (int i = 0; i < cardCount; i++)
            {
                double end = i + 1 < cardCount ? starts[i + 1] : speechEnd;
                string text = line.Cards.Length > i ? line.Cards[i] : string.Empty;
                cards[i] = new CardSpan(i, text, starts[i], end);
            }
            return new LineSpan(line, shotId, start, speechEnd, speechEnd + LineTiming.Gap, cards);
        }

        static LineSpec Spec(IReadOnlyDictionary<string, LineSpec> lines, string id, string shotId)
        {
            if (id != null && lines.TryGetValue(id, out var spec) && spec != null) return spec;
            throw new ArgumentException($"Shot {shotId} plays line {id}, which the script does not have. " +
                                        "Re-import the script or fix the timing plan.");
        }

        static List<ActionSpan> LayoutActions(List<ShotSpan> shots, double duration)
        {
            var raw = new List<(string actor, string verb, string shot, double time, int order)>();
            int order = 0;
            foreach (var span in shots)
                foreach (var cue in span.Plan.Actions)
                    raw.Add((cue.Actor, cue.Verb, span.Id, Clamp(span.TimeOf(cue.Beat, cue.At), duration), order++));
            raw.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.actor, b.actor);
                if (c != 0) return c;
                c = a.time.CompareTo(b.time);
                return c != 0 ? c : a.order.CompareTo(b.order);
            });

            var result = new List<ActionSpan>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var r = raw[i];
                double end = i + 1 < raw.Count && raw[i + 1].actor == r.actor ? raw[i + 1].time : duration;
                if (end - r.time <= 1e-6) continue;   // replaced at the same instant
                result.Add(new ActionSpan(r.actor, r.verb, r.shot, r.time, end));
            }
            return result;
        }

        static void StableSortKeys(List<CurveKeySpan> keys)
        {
            // Insertion sort: tiny lists, and equal times must keep plan order.
            for (int i = 1; i < keys.Count; i++)
            {
                var k = keys[i];
                int j = i - 1;
                while (j >= 0 && keys[j].Time > k.Time)
                {
                    keys[j + 1] = keys[j];
                    j--;
                }
                keys[j + 1] = k;
            }
        }

        static double Clamp(double t, double duration) => t < 0 ? 0 : t > duration ? duration : t;
    }
}
