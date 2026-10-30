namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// Moving the highlight round a tab's slot grid: slots fill rows left to
    /// right, <c>columns</c> to a row. Pure, so the menu's navigation is tested
    /// without input. Every slot, filled or empty, can be highlighted, so the
    /// grid reads the same whatever the player owns.
    /// </summary>
    public static class InventoryGrid
    {
        /// <summary>
        /// The slot reached from <paramref name="index"/> by <paramref name="dx"/>
        /// columns and <paramref name="dy"/> rows (+y is up the screen, so it
        /// moves to the previous row) in a grid of <paramref name="slots"/>.
        /// Left / right stop at the row's ends rather than wrapping to the next
        /// row; up / down stop at the first and last rows, and a move down into
        /// a short last row lands on its last slot. An out-of-range
        /// <paramref name="index"/> is clamped first. -1 only when there are no
        /// slots.
        /// </summary>
        public static int Move(int index, int dx, int dy, int slots, int columns)
        {
            if (slots <= 0) return -1;
            if (columns < 1) columns = 1;
            if (index < 0) index = 0;
            if (index >= slots) index = slots - 1;

            int row = index / columns, col = index % columns;
            int lastRow = (slots - 1) / columns;

            if (dx != 0)
            {
                int rowEnd = row == lastRow ? (slots - 1) % columns : columns - 1;
                col += dx > 0 ? 1 : -1;
                if (col < 0) col = 0;
                if (col > rowEnd) col = rowEnd;
            }
            if (dy != 0)
            {
                row += dy > 0 ? -1 : 1;
                if (row < 0) row = 0;
                if (row > lastRow) row = lastRow;
            }

            int result = row * columns + col;
            return result >= slots ? slots - 1 : result;
        }

        /// <summary>The tab <paramref name="step"/> tabs from <paramref name="current"/> of <paramref name="count"/>, wrapping.</summary>
        public static int StepTab(int current, int step, int count)
        {
            if (count <= 0) return 0;
            int s = step > 0 ? 1 : step < 0 ? -1 : 0;
            return ((current + s) % count + count) % count;
        }
    }
}
