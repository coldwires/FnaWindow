using System.Text;

namespace FnaWindow;

/// <summary>
/// The editor is space-based: Tab and block-indent both insert spaces, and columns map 1:1 to the
/// fixed character grid. Hard tabs are the one thing that breaks that (they would render as a single
/// blank cell), so any text entering the buffer from outside - a loaded file or a paste - has its
/// tabs expanded to spaces at proper tab stops here. Width matches the 4-space indent.
/// </summary>
public static class Tabs
{
    public const int Width = 4;

    /// <summary>
    /// Expand hard tabs to spaces using tab stops, resetting the column at each newline. When the
    /// text is inserted mid-line, pass the display column of its first character as
    /// <paramref name="startCol"/> so leading tabs still land on the right stop.
    /// </summary>
    public static string Expand(string text, int startCol = 0)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\t') < 0) return text;

        var sb = new StringBuilder(text.Length);
        int col = startCol;
        foreach (char c in text)
        {
            switch (c)
            {
                case '\t':
                    int n = Width - (col % Width);
                    sb.Append(' ', n);
                    col += n;
                    break;
                case '\n':
                    sb.Append(c);
                    col = 0;
                    break;
                case '\r':
                    sb.Append(c); // normalized away later; column unchanged
                    break;
                default:
                    sb.Append(c);
                    col++;
                    break;
            }
        }
        return sb.ToString();
    }
}
