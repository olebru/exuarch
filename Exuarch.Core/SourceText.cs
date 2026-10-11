using System;
namespace Exuarch.Core
{
    internal static class SourceText
    {
        public static string[] SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
    }
}
