using System;

namespace FnaWindow.Demo;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var game = new DemoGame();
        game.Run();
    }
}
