﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;

namespace Match3
{
    public sealed class GameState
    {
        private GameCell[] mCells;
        private int mWidth;
        private int mHeight;
        private int mHighlightCount;

        public GameState(int width, int height)
        {
            mWidth = width;
            mHeight = height;
            mCells = new GameCell[width * height];
        }

        public int Width => mWidth;
        public int Height => mHeight;
        public int HighlightCount => mHighlightCount;

        public ref GameCell this[int x, int y]
        {
            get
            {
                Utils.Assert(0 <= x && x < mWidth);
                Utils.Assert(0 <= y && y < mHeight);
                return ref mCells[x + y * mWidth];
            }
        }

        public void Randomize(int seed)
        {
            var rng = new Random(seed);

            for (int iy = 0; iy < mHeight; iy++)
                for (int ix = 0; ix < mWidth; ix++)
                    this[ix, iy].Value = (byte)(rng.Next(4) + 1);
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
    }

    public struct GameCell
    {
        public byte Value;
        public byte Flags;
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
    }
}
