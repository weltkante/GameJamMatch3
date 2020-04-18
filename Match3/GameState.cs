using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.Direct2D1;
using Windows.ApplicationModel.Activation;
using Windows.Media.Miracast;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Match3
{
    public sealed class GameState
    {
        private GameCell[] mCells;
        private int[] mGroups;
        private DateTime mStartTime;
        private int mWidth;
        private int mHeight;
        private int mHighlightCount;
        private int mScore;
        private int mDropScore;
        private int mAnimationLength;
        private DateTime? mGameOver;

        public GameState(int width, int height)
        {
            mWidth = width;
            mHeight = height;
            mCells = new GameCell[width * height];
            mGroups = new int[width * height];
        }

        public int Width => mWidth;
        public int Height => mHeight;
        public int HighlightCount => mHighlightCount;
        public int Score => mScore;
        public DateTime StartTime => mStartTime;
        public bool IsInAnimation => mAnimationLength > 0;
        public int AnimationLength => mAnimationLength;
        public bool IsGameOver => mGameOver.HasValue;
        public DateTime? GameOverTime => mGameOver;

        public bool IsValid(int x, int y) => 0 <= x && x < mWidth && 0 <= y && y < mHeight;

        public ref GameCell this[int x, int y]
        {
            get
            {
                Utils.Assert(IsValid(x, y));
                return ref mCells[x + y * mWidth];
            }
        }

        public void StartGame(int seed)
        {
            mScore = 0;
            mDropScore = 0;
            mStartTime = DateTime.UtcNow;
            mAnimationLength = 0;
            mGameOver = null;

            var rng = new Random(seed);

            for (int iy = 0; iy < mHeight; iy++)
            {
                for (int ix = 0; ix < mWidth; ix++)
                {
                    this[ix, iy] = new GameCell((byte)(rng.Next(4) + 1));
                }
            }
        }

        public void ClearAnimation()
        {
            if (mAnimationLength > 0)
            {
                mAnimationLength = 0;

                for (int iy = 0; iy < mHeight; iy++)
                    for (int ix = 0; ix < mWidth; ix++)
                        this[ix, iy].DropCount = 0;
            }
        }

        public void ClearHighlight()
        {
            if (mHighlightCount > 0)
            {
                mHighlightCount = 0;

                for (int iy = 0; iy < mHeight; iy++)
                    for (int ix = 0; ix < mWidth; ix++)
                        this[ix, iy].Highlight = false;
            }
        }

        public void Highlight(int x, int y)
        {
            if (!(0 <= x && x < mWidth && 0 <= y && y < mHeight))
            {
                ClearHighlight();
                return;
            }

            if (this[x, y].IsEmpty)
                return; // nothing to highlight

            if (this[x, y].Highlight)
                return; // same highlight, nothing to do

            ClearHighlight();

            var value = this[x, y].Value;
            var visited = new HashSet<(int, int)>(mWidth * mHeight);
            var pending = new Stack<(int, int)>(mWidth * mHeight);

            visited.Add((x, y));
            pending.Push((x, y));

            while (pending.Count > 0)
            {
                (x, y) = pending.Pop();
                this[x, y].Highlight = true;
                mHighlightCount++;
                Expand(x, y);
            }

            void Expand(int ix, int iy)
            {
                Append(ix + 1, iy);
                Append(ix - 1, iy);
                Append(ix, iy + 1);
                Append(ix, iy - 1);
            }

            void Append(int ix, int iy)
            {
                if (0 <= ix && ix < mWidth && 0 <= iy && iy < mHeight && visited.Add((ix, iy)) && this[ix, iy].Value == value)
                    pending.Push((ix, iy));
            }
        }

        public bool FillBlocks(ref int count)
        {
            if (IsGameOver)
                return false;

            var rng = new Random();

            bool droppedAnything = false;
            bool checkedForEmptyFields = false;

            int[] maxHeight = new int[mWidth];

            while (count > 0)
            {
                var ix = rng.Next(mWidth);
                if (this[ix, 0].IsEmpty)
                {
                    byte iy = 0;
                    while (iy + 1 < mHeight && this[ix, iy + 1].IsEmpty)
                        iy++;

                    maxHeight[ix] = Math.Max(maxHeight[ix], iy + 1);
                    this[ix, iy] = new GameCell((byte)(rng.Next(4) + 1));
                    this[ix, iy].DropCount = (byte)maxHeight[ix];
                    mAnimationLength = Math.Max(mAnimationLength, maxHeight[ix]);
                    count--;
                    checkedForEmptyFields = false;
                    droppedAnything = true;
                }
                else
                {
                    // check if there are any empty fields left
                    if (!checkedForEmptyFields)
                    {
                        for (ix = 0; ix < mWidth; ix++)
                        {
                            if (this[ix, 0].IsEmpty)
                            {
                                checkedForEmptyFields = true;
                                break;
                            }
                        }

                        if (!checkedForEmptyFields)
                            break;
                    }
                }
            }

            return droppedAnything;
        }

        public bool ResolveBlocks()
        {
            if (mHighlightCount == 0 || IsInAnimation || IsGameOver)
                return false;

            mScore += GetScore(mHighlightCount);
            mHighlightCount = 0;

            for (int iy = 0; iy < mHeight; iy++)
                for (int ix = 0; ix < mWidth; ix++)
                    if (this[ix, iy].Highlight)
                        this[ix, iy].Clear();

            for (int ix = 0; ix < mWidth; ix++)
            {
                byte dropCount = 0;

                for (int iy = mHeight - 1; iy >= 0; iy--)
                {
                    if (this[ix, iy].IsEmpty)
                    {
                        dropCount++;
                    }
                    else if (dropCount > 0)
                    {
                        ref var src = ref this[ix, iy];
                        ref var dst = ref this[ix, iy + dropCount];

                        dst = src;
                        dst.DropCount = dropCount;
                        src.Clear();
                        mAnimationLength = Math.Max(mAnimationLength, dropCount);
                    }
                }
            }

            const int kDropGate = 500;
            const int kDropCost = 50;

            while (mScore - mDropScore >= kDropGate)
            {
                int count, count0;
                count = count0 = kDropGate / kDropCost;

                if (!FillBlocks(ref count))
                    break;

                mDropScore += (count0 - count) * kDropCost;
            }

            if (CheckGameOver())
                mGameOver = DateTime.UtcNow;

            return true;
        }

        private static int GetScore(int count)
        {
            Utils.BreakIf(count < 3);
            return (int)(25 * Math.Pow(2, count - 3));
        }

        public bool CheckGameOver()
        {
            for (int i = 0; i < mCells.Length; i++)
            {
                mCells[i].Group = 0;
                mGroups[i] = 0;
            }

            byte usedGroups = 0;

            for (int iy = 0; iy < mHeight; iy++)
            {
                for (int ix = 0; ix < mWidth; ix++)
                {
                    ref var cell = ref this[ix, iy];

                    if (cell.IsEmpty)
                        continue;

                    Check(ref cell, ix - 1, iy);
                    Check(ref cell, ix + 1, iy);
                    Check(ref cell, ix, iy - 1);
                    Check(ref cell, ix, iy + 1);

                    if (cell.Group == 0)
                    {
                        mGroups[usedGroups] = 1;
                        cell.Group = ++usedGroups;
                    }
                }
            }

            for (int i = 0; i < usedGroups; i++)
                if (mGroups[i] >= 3)
                    return false;

            return true;

            void Check(ref GameCell cellA, int xb, int yb)
            {
                if (!(0 <= xb && xb < mWidth && 0 <= yb && yb < mHeight))
                    return;

                ref var cellB = ref this[xb, yb];
                if (cellB.IsEmpty)
                    return;

                if (cellA.Value != cellB.Value)
                    return;

                Merge(ref cellA, ref cellB);
            }

            void Merge(ref GameCell cellA, ref GameCell cellB)
            {
                Utils.Assert(!cellA.IsEmpty && !cellB.IsEmpty && cellA.Value == cellB.Value);

                if (cellA.Group > 0 && cellB.Group > 0)
                {
                    if (cellA.Group != cellB.Group)
                    {
                        mGroups[cellA.Group - 1] += mGroups[cellB.Group - 1];
                        mGroups[cellB.Group - 1] = 0;
                        cellB.Group = cellA.Group;
                    }
                }
                else if (cellA.Group > 0)
                {
                    cellB.Group = cellA.Group;
                    mGroups[cellA.Group - 1]++;
                }
                else if (cellB.Group > 0)
                {
                    cellA.Group = cellB.Group;
                    mGroups[cellB.Group - 1]++;
                }
                else
                {
                    mGroups[usedGroups] = 2;
                    cellA.Group = cellB.Group = ++usedGroups;
                }
            }
        }
    }

    public struct GameCell
    {
        public byte Value;
        public byte Flags;
        public byte DropCount;
        public byte Group;

        public GameCell(byte value)
        {
            Value = value;
            Flags = 0;
            DropCount = 0;
            Group = 0;
        }

        public bool IsEmpty => Value == 0;

        public bool Highlight
        {
            get => (Flags & 1) != 0;
            set
            {
                if (value)
                    Flags |= 1;
                else
                    Flags &= unchecked((byte)(~1));
            }
        }

        public void Clear()
        {
            Value = 0;
            Flags = 0;
            DropCount = 0;
            Group = 0;
        }
    }
}
