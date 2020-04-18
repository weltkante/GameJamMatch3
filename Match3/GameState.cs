using System;
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

        public GameState(int width, int height)
        {
            mWidth = width;
            mHeight = height;
            mCells = new GameCell[width * height];
        }

        public int Width => mWidth;
        public int Height => mHeight;

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
                    this[ix, iy].Value = rng.Next(4) + 1;
        }
    }

    public struct GameCell
    {
        public int Value;
        public bool IsEmpty => Value == 0;
    }
}
