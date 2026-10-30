using NUnit.Framework;
using Pickleball.Sandbox.Inventory;
using UnityEngine;

namespace Pickleball.Tests.Inventory
{
    /// <summary>
    /// Menu navigation: moving round the slot grid (<see cref="InventoryGrid"/>)
    /// and turning a held direction into steps (<see cref="DirectionRepeat"/>).
    /// </summary>
    public class InventoryNavigationTests
    {
        // 8 slots, 5 columns:  0 1 2 3 4
        //                      5 6 7
        const int Slots = 8, Columns = 5;

        [Test]
        public void LeftRight_StopAtTheRowEnds()
        {
            Assert.That(InventoryGrid.Move(0, -1, 0, Slots, Columns), Is.EqualTo(0));
            Assert.That(InventoryGrid.Move(4, +1, 0, Slots, Columns), Is.EqualTo(4), "no wrap to the next row");
            Assert.That(InventoryGrid.Move(7, +1, 0, Slots, Columns), Is.EqualTo(7), "short last row");
            Assert.That(InventoryGrid.Move(2, +1, 0, Slots, Columns), Is.EqualTo(3));
        }

        [Test]
        public void UpDown_MoveByARow_AndStop()
        {
            Assert.That(InventoryGrid.Move(1, 0, -1, Slots, Columns), Is.EqualTo(6), "down");
            Assert.That(InventoryGrid.Move(6, 0, +1, Slots, Columns), Is.EqualTo(1), "up");
            Assert.That(InventoryGrid.Move(1, 0, +1, Slots, Columns), Is.EqualTo(1), "top row stays");
            Assert.That(InventoryGrid.Move(6, 0, -1, Slots, Columns), Is.EqualTo(6), "bottom row stays");
        }

        [Test]
        public void DownIntoAShortRow_LandsOnItsLastSlot()
            => Assert.That(InventoryGrid.Move(4, 0, -1, Slots, Columns), Is.EqualTo(7));

        [Test]
        public void Grid_Edges()
        {
            Assert.That(InventoryGrid.Move(0, 1, 0, 0, Columns), Is.EqualTo(-1), "no slots");
            Assert.That(InventoryGrid.Move(99, 0, 0, Slots, Columns), Is.EqualTo(7), "clamped");
            Assert.That(InventoryGrid.Move(0, 0, -1, 3, 1), Is.EqualTo(1), "one column");
        }

        [Test]
        public void StepTab_Wraps()
        {
            Assert.That(InventoryGrid.StepTab(2, +1, 3), Is.EqualTo(0));
            Assert.That(InventoryGrid.StepTab(0, -1, 3), Is.EqualTo(2));
            Assert.That(InventoryGrid.StepTab(1, 0, 3), Is.EqualTo(1));
        }

        [Test]
        public void Repeat_StepsOnPush_ThenAfterDelay_ThenEveryInterval()
        {
            var repeat = new DirectionRepeat();
            var right = new Vector2(1f, 0f);
            const float delay = 0.35f, interval = 0.1f, threshold = 0.5f;
            Assert.That(repeat.Tick(right, 0.016f, threshold, delay, interval), Is.EqualTo(new Vector2Int(1, 0)), "push");
            Assert.That(repeat.Tick(right, 0.3f, threshold, delay, interval), Is.EqualTo(Vector2Int.zero), "inside delay");
            Assert.That(repeat.Tick(right, 0.06f, threshold, delay, interval), Is.EqualTo(new Vector2Int(1, 0)), "delay over");
            Assert.That(repeat.Tick(right, 0.05f, threshold, delay, interval), Is.EqualTo(Vector2Int.zero));
            Assert.That(repeat.Tick(right, 0.06f, threshold, delay, interval), Is.EqualTo(new Vector2Int(1, 0)), "interval");
        }

        [Test]
        public void Repeat_ReleaseAndRepush_StepsAgainAtOnce()
        {
            var repeat = new DirectionRepeat();
            var up = new Vector2(0f, 1f);
            repeat.Tick(up, 0.01f, 0.5f, 0.35f, 0.1f);
            Assert.That(repeat.Tick(Vector2.zero, 0.01f, 0.5f, 0.35f, 0.1f), Is.EqualTo(Vector2Int.zero));
            Assert.That(repeat.Tick(up, 0.01f, 0.5f, 0.35f, 0.1f), Is.EqualTo(new Vector2Int(0, 1)));
        }

        [Test]
        public void Repeat_StickBelowThreshold_OrDiagonal()
        {
            var repeat = new DirectionRepeat();
            Assert.That(repeat.Tick(new Vector2(0.3f, 0.2f), 0.01f, 0.5f, 0.35f, 0.1f), Is.EqualTo(Vector2Int.zero), "drift");
            Assert.That(repeat.Tick(new Vector2(-0.6f, 0.8f), 0.01f, 0.5f, 0.35f, 0.1f), Is.EqualTo(new Vector2Int(0, 1)),
                        "the stronger axis wins");
        }
    }
}
