

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
        Teal = 12

        // Twelve is a PERCEPTUAL ceiling, not an art-budget one, and adding a 13th is very
        // unlikely to be the right fix for anything.
        //
        // A level draws its colours from this enum, so any two entries can end up on the same
        // board -- which makes the palette's WORST pair the real quality bar. There were 18
        // entries here until 2026-09-13, and the extra six pushed that bar through the floor:
        // Yellow #FFCC00 and Amber #FFC208 measured CIEDE2000 dE 3.6 (indistinguishable; dE 10
        // is roughly "same colour at a glance") and shipped together in 99 levels. 631 of 700
        // levels held some pair under dE 25.
        //
        // The cause is geometric. Colours that stay vivid AND readable on the near-white board
        // occupy a limited volume of Lab space, and packing more of them in shrinks the gap
        // between the closest two: an optimiser given all 18 could only reach a worst pair of
        // dE ~24, while 12 reach dE ~31 -- comfortably distinct. Since no level ever needed
        // more than 12 colours (measured across all 700: max 12, and 78% use 8 or fewer), the
        // extra six bought nothing and cost the whole palette its separation.
        //
        // Because these 12 are mutually >= dE 30, ANY subset of them is safe, which is why
        // generation can still pick colours freely without a separation check of its own.
        // PairColorPaletteTests guards the invariant -- a new entry that crowds the palette
        // fails there rather than in a player's eyes.
    }
}

