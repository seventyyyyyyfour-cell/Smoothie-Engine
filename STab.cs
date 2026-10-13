using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace SmoothieEngine;

public static class STab
{
    public static void Draw(Rectangle bounds)
    {
        Raylib.DrawRectangleRec(bounds, new Color(38, 38, 38, 255));
        Raylib.DrawText("Scripting tab is under development.", (int)bounds.X + 10, (int)bounds.Y + 10, 20, Color.White);
    }
}