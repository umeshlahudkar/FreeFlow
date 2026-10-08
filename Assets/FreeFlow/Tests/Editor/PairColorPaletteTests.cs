using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FreeFlow.Enums;

/// <summary>
/// Guards the one property that makes pair colours readable: any two entries in the palette can
/// land on the same board, so the palette's WORST pair is the quality bar for every level.
///
/// This is not hypothetical. Until 2026-09-13 the palette carried 18 colours, and Yellow #FFCC00
/// sat dE 3.6 from Amber #FFC208 -- the same colour to the eye -- shipping together in 99 levels;
/// 631 of 700 levels held some pair under dE 25. Nothing caught it because "distinct" was only
/// ever enforced as distinct ENUM VALUES (LevelGenerator.PickDistinctColors), never as distinct
/// appearance.
/// </summary>
public class PairColorPaletteTests
{
    // CIEDE2000. Rough reading: <10 is the same colour at a glance, <18 is confusable on a busy
    // board, 25+ is comfortably distinct. The curated 12 sit at ~30 -- this floor is set below
    // that so a deliberate small retune does not trip it, but any genuinely confusable pair does.
    private const double MinimumSeparation = 25.0;

    // Dots are drawn on the near-white board; a colour that sinks into it is unreadable however
    // far it is from the other dots.
    private const double MinimumBoardContrast = 25.0;
    private static readonly Color BoardBackground = new Color(0.937f, 0.945f, 0.968f);

    private static PairColorDataSO LoadPalette()
    {
        PairColorDataSO so = Resources.Load<PairColorDataSO>("PairColorData");
        Assert.IsNotNull(so, "Resources/PairColorData.asset is missing.");
        return so;
    }

    [Test]
    public void EveryPaletteColourIsPerceptuallyDistinctFromEveryOther()
    {
        PairColorData[] entries = LoadPalette().pairColorDatas;
        List<string> failures = new List<string>();

        for (int i = 0; i < entries.Length; i++)
        {
            for (int j = i + 1; j < entries.Length; j++)
            {
                double d = CieDe2000(entries[i].color, entries[j].color);
                if (d < MinimumSeparation)
                {
                    failures.Add(string.Format("{0} {1} vs {2} {3}: dE {4:0.0}",
                        entries[i].pairColorType, ToHex(entries[i].color),
                        entries[j].pairColorType, ToHex(entries[j].color), d));
                }
            }
        }

        Assert.IsEmpty(failures, "Palette colours too similar to tell apart on one board (min dE "
            + MinimumSeparation + "):\n  " + string.Join("\n  ", failures.ToArray()));
    }

    [Test]
    public void EveryPaletteColourReadsAgainstTheBoard()
    {
        foreach (PairColorData entry in LoadPalette().pairColorDatas)
        {
            double d = CieDe2000(entry.color, BoardBackground);
            Assert.GreaterOrEqual(d, MinimumBoardContrast,
                entry.pairColorType + " " + ToHex(entry.color) + " does not stand out from the board.");
        }
    }

    [Test]
    public void EveryColourTypeHasExactlyOnePaletteEntry()
    {
        PairColorData[] entries = LoadPalette().pairColorDatas;
        HashSet<PairColorType> seen = new HashSet<PairColorType>();

        foreach (PairColorData entry in entries)
        {
            Assert.AreNotEqual(PairColorType.None, entry.pairColorType, "None is not a drawable colour.");
            Assert.IsTrue(seen.Add(entry.pairColorType), "Duplicate palette entry for " + entry.pairColorType);
        }

        foreach (PairColorType type in System.Enum.GetValues(typeof(PairColorType)))
        {
            if (type == PairColorType.None) { continue; }
            Assert.IsTrue(seen.Contains(type),
                type + " is declared in PairColorType but has no colour in PairColorData.asset.");
        }
    }

    private static string ToHex(Color c)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    // ---- CIEDE2000 ----------------------------------------------------------------------
    // Plain RGB distance says Yellow/Amber are far apart and Blue/Slate are close; both are
    // wrong about what the eye does, which is why this uses the perceptual metric instead.

    private static void ToLab(Color c, out double L, out double a, out double b)
    {
        double r = Linear(c.r), g = Linear(c.g), bl = Linear(c.b);
        double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * bl) / 0.95047;
        double y = (0.2126729 * r + 0.7151522 * g + 0.0721750 * bl);
        double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * bl) / 1.08883;
        double fx = F(x), fy = F(y), fz = F(z);
        L = 116 * fy - 16;
        a = 500 * (fx - fy);
        b = 200 * (fy - fz);
    }

    private static double Linear(double c)
    {
        return c <= 0.04045 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double F(double t)
    {
        return t > 216.0 / 24389.0 ? System.Math.Pow(t, 1.0 / 3.0) : (841.0 / 108.0) * t + 4.0 / 29.0;
    }

    private static double CieDe2000(Color c1, Color c2)
    {
        ToLab(c1, out double L1, out double a1, out double b1);
        ToLab(c2, out double L2, out double a2, out double b2);

        double avgL = (L1 + L2) / 2;
        double C1 = System.Math.Sqrt(a1 * a1 + b1 * b1);
        double C2 = System.Math.Sqrt(a2 * a2 + b2 * b2);
        double avgC = (C1 + C2) / 2;
        double g = 0.5 * (1 - System.Math.Sqrt(System.Math.Pow(avgC, 7) / (System.Math.Pow(avgC, 7) + System.Math.Pow(25, 7))));
        double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
        double C1p = System.Math.Sqrt(a1p * a1p + b1 * b1);
        double C2p = System.Math.Sqrt(a2p * a2p + b2 * b2);
        double avgCp = (C1p + C2p) / 2;

        double h1p = System.Math.Atan2(b1, a1p) * 180 / System.Math.PI; if (h1p < 0) { h1p += 360; }
        double h2p = System.Math.Atan2(b2, a2p) * 180 / System.Math.PI; if (h2p < 0) { h2p += 360; }

        double dLp = L2 - L1;
        double dCp = C2p - C1p;

        double dhp;
        if (C1p * C2p == 0) { dhp = 0; }
        else if (System.Math.Abs(h2p - h1p) <= 180) { dhp = h2p - h1p; }
        else if (h2p - h1p > 180) { dhp = h2p - h1p - 360; }
        else { dhp = h2p - h1p + 360; }
        double dHp = 2 * System.Math.Sqrt(C1p * C2p) * System.Math.Sin(dhp * System.Math.PI / 360);

        double avghp;
        if (C1p * C2p == 0) { avghp = h1p + h2p; }
        else if (System.Math.Abs(h1p - h2p) <= 180) { avghp = (h1p + h2p) / 2; }
        else if (h1p + h2p < 360) { avghp = (h1p + h2p + 360) / 2; }
        else { avghp = (h1p + h2p - 360) / 2; }

        double t = 1
            - 0.17 * System.Math.Cos((avghp - 30) * System.Math.PI / 180)
            + 0.24 * System.Math.Cos(2 * avghp * System.Math.PI / 180)
            + 0.32 * System.Math.Cos((3 * avghp + 6) * System.Math.PI / 180)
            - 0.20 * System.Math.Cos((4 * avghp - 63) * System.Math.PI / 180);

        double dTheta = 30 * System.Math.Exp(-System.Math.Pow((avghp - 275) / 25, 2));
        double Rc = 2 * System.Math.Sqrt(System.Math.Pow(avgCp, 7) / (System.Math.Pow(avgCp, 7) + System.Math.Pow(25, 7)));
        double Sl = 1 + (0.015 * System.Math.Pow(avgL - 50, 2)) / System.Math.Sqrt(20 + System.Math.Pow(avgL - 50, 2));
        double Sc = 1 + 0.045 * avgCp;
        double Sh = 1 + 0.015 * avgCp * t;
        double Rt = -System.Math.Sin(2 * dTheta * System.Math.PI / 180) * Rc;

        return System.Math.Sqrt(
            System.Math.Pow(dLp / Sl, 2) +
            System.Math.Pow(dCp / Sc, 2) +
            System.Math.Pow(dHp / Sh, 2) +
            Rt * (dCp / Sc) * (dHp / Sh));
    }
}
