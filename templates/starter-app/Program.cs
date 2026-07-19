using System;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var game = new StarterGame();
        game.Run();
    }
}
