
namespace FreeFlow.Enums
{
    public enum GridSize
    {
        GridSize_4X4 = 4,
        GridSize_5X5 = 5,
        GridSize_6X6 = 6,
        GridSize_7X7 = 7,
        GridSize_8X8 = 8,

        // The value IS the side length, so nothing derives a size from the enum's ordering and
        // adding entries cannot disturb the levels already authored at 4-8.
        GridSize_9X9 = 9,
        GridSize_10X10 = 10,
        GridSize_11X11 = 11,
        GridSize_12X12 = 12
    }
}

