using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// The pause restores what was there, not 1, and can never record its own
    /// 0 as the value to restore. The fixture puts the globals back afterwards.
    /// </summary>
    public class TimeScalePauseTests : DialogueTestFixture
    {
        [Test]
        public void Hold_StopsTime_Release_RestoresThePreviousScale_NotOne()
        {
            Time.timeScale = 0.5f;
            var pause = new TimeScalePause();

            pause.Hold(pauseAudio: false);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(pause.IsHeld, Is.True);

            pause.Release();
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            Assert.That(pause.IsHeld, Is.False);
        }

        [Test]
        public void Hold_Twice_KeepsTheFirstRecordedScale()
        {
            Time.timeScale = 0.75f;
            var pause = new TimeScalePause();

            pause.Hold(false);
            pause.Hold(false);          // would record 0 if it were not guarded
            pause.Release();

            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
        }

        [Test]
        public void Release_WithoutHold_ChangesNothing()
        {
            Time.timeScale = 0.3f;
            new TimeScalePause().Release();
            Assert.That(Time.timeScale, Is.EqualTo(0.3f));
        }

        [Test]
        public void Audio_IsPausedAndRestoredToItsPreviousState()
        {
            AudioListener.pause = false;
            var pause = new TimeScalePause();

            pause.Hold(pauseAudio: true);
            Assert.That(AudioListener.pause, Is.True);
            pause.Release();
            Assert.That(AudioListener.pause, Is.False);

            AudioListener.pause = true;     // already paused by someone else
            pause.Hold(true);
            pause.Release();
            Assert.That(AudioListener.pause, Is.True, "restores the previous state, not 'unpaused'");
        }

        [Test]
        public void Audio_LeftAloneWhenNotAsked()
        {
            AudioListener.pause = false;
            var pause = new TimeScalePause();
            pause.Hold(pauseAudio: false);
            Assert.That(AudioListener.pause, Is.False);
            pause.Release();
        }
    }
}
