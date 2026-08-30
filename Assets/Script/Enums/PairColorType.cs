

namespace FreeFlow.Enums
{
    public enum PairColorType
    {
        None = 0,
        Red = 1,
        Blue = 2,
        Yellow = 3,
        Green = 4,
        Orange = 5,
        Cyan = 6,
        Indigo = 7,
        Pink = 8,
        Purple = 9,
        Brown = 10,
        Lime = 11,
        Teal = 12,

        // 13-18 exist for the Classic campaign's larger boards. A board with no blocked cells and
        // no mechanics gets ALL of its constraint from pair count, and uniqueness on a full grid
        // needs roughly one pair per five cells (measured: 7x7 converges at 10 colours, 8x8 at 11).
        // Twelve therefore caps a mechanic-free board at about 8x8, and 9x9 needs ~16. These are
        // data-only -- a colour is a PairColorType plus a Color, with no sprite or material behind
        // it -- so the ceiling was never an art limit, only an unexamined one.
        Magenta = 13,
        Amber = 14,
        Mint = 15,
        Rose = 16,
        Slate = 17,
        Olive = 18
    }
}

