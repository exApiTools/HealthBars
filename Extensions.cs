using System;
using ExileCore;
using SharpDX;

namespace HealthBars;

public static class Extensions
{
    public static Color FromHex(uint hex)
    {
        return Color.FromAbgr((hex << 8) + 0xff);
    }

    public static string FormatHp(this long hp)
    {
        if (Math.Abs(hp) >= 100_000_000)
        {
            return ((double)hp / 1_000_000).ToString("0M");
        }

        if (Math.Abs(hp) >= 10_000_000)
        {
            return ((double)hp / 1_000_000).ToString("0.0M");
        }

        if (Math.Abs(hp) >= 1_000_000)
        {
            return ((double)hp / 1_000_000).ToString("0.00M");
        }

        if (Math.Abs(hp) >= 100_000)
        {
            return ((double)hp / 1_000).ToString("0K");
        }

        if (Math.Abs(hp) >= 10_000)
        {
            return ((double)hp / 1_000).ToString("0.0K");
        }

        if (Math.Abs(hp) >= 1_000)
        {
            return ((double)hp / 1_000).ToString("0.00K");
        }

        return hp.ToString("0");
    }

    public static string FormatHp(this int hp)
    {
        return ((long)hp).FormatHp();
    }

    public static string FormatHp(this double hp)
    {
        return ((long)hp).FormatHp();
    }
}