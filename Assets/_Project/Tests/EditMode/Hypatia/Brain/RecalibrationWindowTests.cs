using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;

namespace Pickleball.Tests.Hypatia.Brain
{
    /// <summary>
    /// The punish window after each volley. It runs on her clock, so a slow on
    /// her lengthens it and a full freeze holds it open; and each edge fires
    /// exactly once, because the HUD glow and her hover follow those edges.
    /// </summary>
    public class RecalibrationWindowTests
    {
        RecalibrationWindow _window;
        int _opened, _closed;
        float _openedWith;

        [SetUp]
        public void SetUp()
        {
            _window = new RecalibrationWindow();
            _opened = _closed = 0;
            _openedWith = -1f;
            _window.Opened += d => { _opened++; _openedWith = d; };
            _window.Closed += () => _closed++;
        }

        [Test]
        public void Opens_ThenClosesAfterItsDurationOnScaledTime()
        {
            Assert.IsTrue(_window.Open(2.5f));
            Assert.IsTrue(_window.IsOpen);
            Assert.That(_opened, Is.EqualTo(1));
            Assert.That(_openedWith, Is.EqualTo(2.5f));

            for (int i = 0; i < 24; i++) _window.Tick(0.1f);   // 2.4 s
            Assert.IsTrue(_window.IsOpen);
            Assert.That(_window.Remaining, Is.EqualTo(0.1f).Within(1e-4f));

            _window.Tick(0.11f);   // a hair over, so float rounding cannot leave it open
            Assert.IsFalse(_window.IsOpen);
            Assert.That(_closed, Is.EqualTo(1));
        }

        [Test]
        public void HalfSpeedSlow_DoublesTheRealTimeLength()
        {
            const float realDt = 0.1f, slow = 0.5f;
            _window.Open(1f);
            int frames = 0;
            while (_window.IsOpen && frames < 1000)
            {
                _window.Tick(realDt * slow);
                frames++;
            }
            Assert.That(frames * realDt, Is.EqualTo(2f).Within(0.11f));
        }

        [Test]
        public void ZeroScale_FreezesItExactly()
        {
            _window.Open(1f);
            _window.Tick(0.4f);
            for (int i = 0; i < 1000; i++) _window.Tick(0f);
            Assert.IsTrue(_window.IsOpen);
            Assert.That(_window.Remaining, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(_closed, Is.EqualTo(0));
        }

        [Test]
        public void NaNDt_DoesNothing()
        {
            _window.Open(1f);
            _window.Tick(float.NaN);
            Assert.That(_window.Remaining, Is.EqualTo(1f));
        }

        [Test]
        public void EventsFireOnceEach_EvenWhenTickedAndClosedPastTheEnd()
        {
            _window.Open(0.5f);
            _window.Tick(1f);
            _window.Tick(1f);
            _window.Close();
            Assert.That(_opened, Is.EqualTo(1));
            Assert.That(_closed, Is.EqualTo(1));
        }

        [Test]
        public void OpenWhileOpen_IsRefusedWithoutAnEvent()
        {
            _window.Open(1f);
            Assert.IsFalse(_window.Open(5f));
            Assert.That(_opened, Is.EqualTo(1));
            Assert.That(_window.Duration, Is.EqualTo(1f));
        }

        [Test]
        public void Close_EndsItEarly_AndCloseWhenClosedIsSilent()
        {
            _window.Close();
            Assert.That(_closed, Is.EqualTo(0));

            _window.Open(3f);
            _window.Close();
            Assert.IsFalse(_window.IsOpen);
            Assert.That(_window.Remaining, Is.EqualTo(0f));
            Assert.That(_closed, Is.EqualTo(1));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void ZeroOrBadDuration_OpensAndClosesAtOnce(float duration)
        {
            Assert.IsTrue(_window.Open(duration));
            Assert.IsFalse(_window.IsOpen);
            Assert.That(_opened, Is.EqualTo(1));
            Assert.That(_openedWith, Is.EqualTo(0f));
            Assert.That(_closed, Is.EqualTo(1));
        }

        [Test]
        public void CanReopenAfterClosing()
        {
            _window.Open(0.2f);
            _window.Tick(0.3f);
            Assert.IsTrue(_window.Open(0.2f));
            Assert.That(_opened, Is.EqualTo(2));
        }
    }
}
