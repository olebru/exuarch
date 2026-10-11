using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace Exuarch.Web.Components
{
    internal static partial class ScreenInterop
    {
        [JSImport("globalThis.exuarchScreen.draw")]
        public static partial void Draw(string canvasId, int x, int y, int width, int height,
            [JSMarshalAs<JSType.MemoryView>] Span<byte> rows, bool depth);

        [JSImport("globalThis.exuarchScreen.nextTurn")]
        public static partial Task NextTurn();
    }
}
