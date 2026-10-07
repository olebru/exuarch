using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A 640 x 480 picture the Run view can show: the framebuffer's colours, or a depth buffer drawn in grey.
    public interface IScreen : IBusDevice
    {
        int X { get; }
        int Y { get; }
        // What the view says about the picture, such as "RGB565".
        string Kind { get; }
        // True when the words are depths, to be drawn in grey, rather than RGB565 colours.
        bool IsDepth { get; }
        (int X, int Y, int Width, int Height)? TakeDirtyRegion();
        // The whole picture's 16 bit words, row by row, top left first: the browser turns them into pixels.
        ushort[] Words { get; }
    }

    // A 640 x 480 colour display. Each pixel is a 16 bit RGB565 word: 5 bits red, 6 bits green, 5 bits blue.
    // loadx and loady set the cursor from the bus; plot writes the bus value at the cursor and moves one pixel
    // right, wrapping to the start of the next row (and from the last row to the first); skip moves the same way
    // without writing; clear blanks the screen and homes the cursor.
    public class Framebuffer : ControlLineDevice, IBusDevice, IScreen, IWriteTracked, IObservableState
    {
        public const int Width = 640;
        public const int Height = 480;

        // The picture plot, skip and clear work on, row by row, top left first. On a double buffered screen this
        // is the back buffer, and Shown the front one.
        public ushort[] Pixels { get; protected set; } = new ushort[Width * Height];
        // The picture on the screen.
        public virtual ushort[] Shown { get { return Pixels; } }
        public int X { get; private set; }
        public int Y { get; private set; }
        public long WriteCount { get; private set; }
        public int LastWriteAddress { get; private set; } = -1;
        public int LastWriteValue { get; private set; }
        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool loadX, loadY, plot, skip, clear;
        public virtual string Kind { get { return "RGB565"; } }
        public bool IsDepth { get { return false; } }
        public ushort[] Words { get { return Shown; } }
        private int dirtyLeft = Width, dirtyTop = Height, dirtyRight = -1, dirtyBottom = -1;

        public Framebuffer(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            ControlLines
                .Add("loadx", () => loadX = true)
                .Add("loady", () => loadY = true)
                .Add("plot", () => plot = true)
                .Add("skip", () => skip = true)
                .Add("clear", () => clear = true);
        }

        public int ValueAt(int address) { return Pixels[address]; }

        public void Drive()
        {
        }
        public virtual void Latch()
        {
            if (clear)
            {
                Array.Clear(Pixels);
                X = Y = 0;
                Drew(0, 0, Width - 1, Height - 1);
                clear = false;
            }
            if (loadX)
            {
                X = bus.Data % Width;
                loadX = false;
            }
            if (loadY)
            {
                Y = bus.Data % Height;
                loadY = false;
            }
            if (plot)
            {
                int address = Y * Width + X;
                Pixels[address] = (ushort)bus.Data;
                LastWriteAddress = address;
                LastWriteValue = Pixels[address];
                WriteCount++;
                Drew(X, Y, X, Y);
                Advance();
                plot = false;
            }
            if (skip)
            {
                Advance();
                skip = false;
            }
        }

        private void Advance()
        {
            if (++X == Width)
            {
                X = 0;
                Y = (Y + 1) % Height;
            }
        }

        // Pixels changed in the buffer being drawn. On a single buffered screen that is what is shown.
        protected virtual void Drew(int left, int top, int right, int bottom)
        {
            MarkDirty(left, top, right, bottom);
        }

        protected void MarkDirty(int left, int top, int right, int bottom)
        {
            dirtyLeft = Math.Min(dirtyLeft, left);
            dirtyTop = Math.Min(dirtyTop, top);
            dirtyRight = Math.Max(dirtyRight, right);
            dirtyBottom = Math.Max(dirtyBottom, bottom);
        }

        // The rectangle changed since the last call (x, y, width, height), or null when nothing changed. For a
        // view that redraws only what changed.
        public (int X, int Y, int Width, int Height)? TakeDirtyRegion()
        {
            if (dirtyRight < 0) return null;
            var region = (dirtyLeft, dirtyTop, dirtyRight - dirtyLeft + 1, dirtyBottom - dirtyTop + 1);
            dirtyLeft = Width; dirtyTop = Height; dirtyRight = -1; dirtyBottom = -1;
            return region;
        }

        // A region of the shown picture as RGBA bytes, 4 per pixel, row by row, ready for a canvas.
        public byte[] ToRgba(int x, int y, int width, int height)
        {
            var bytes = new byte[width * height * 4];
            int i = 0;
            for (int row = y; row < y + height; row++)
            {
                for (int column = x; column < x + width; column++)
                {
                    var (r, g, b) = ToRgb(Shown[row * Width + column]);
                    bytes[i++] = r;
                    bytes[i++] = g;
                    bytes[i++] = b;
                    bytes[i++] = 255;
                }
            }
            return bytes;
        }

        // The raw RGB565 words of a region of the shown picture, two little endian bytes per pixel, copied row by row. Much cheaper than
        // ToRgba where .NET is interpreted (WebAssembly): the browser expands the colours itself.
        public byte[] ToRgb565Bytes(int x, int y, int width, int height)
        {
            var bytes = new byte[width * height * 2];
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(Shown, ((y + row) * Width + x) * 2, bytes, row * width * 2, width * 2);
            }
            return bytes;
        }

        // RGB565 to 8 bit channels, repeating the top bits so full intensity maps to 255.
        public static (byte R, byte G, byte B) ToRgb(int rgb565)
        {
            int r = (rgb565 >> 11) & 0x1F, g = (rgb565 >> 5) & 0x3F, b = rgb565 & 0x1F;
            return ((byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)), (byte)((b << 3) | (b >> 2)));
        }
        public static int FromRgb(int r, int g, int b)
        {
            return ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return false; }
        public virtual void Observe(WatchValue watch)
        {
            watch(deviceID + ".x", () => X);
            watch(deviceID + ".y", () => Y);
        }
    }

    // A framebuffer with two pictures: the front buffer is on the screen while plot, skip and clear work on the back
    // buffer, so a program can draw a whole frame out of sight. swap exchanges the two, in the latch half, after any
    // plot in the same tick; the new back buffer holds the frame before last, until it is cleared or drawn over.
    // The cursor stays where it is.
    public class DoubleFramebuffer : Framebuffer
    {
        private ushort[] front = new ushort[Width * Height];
        private bool swap;
        public override ushort[] Shown { get { return front; } }
        // Which buffer is in front, 0 or 1; it changes with every swap.
        public int FrontBuffer { get; private set; }
        public long Swaps { get; private set; }
        public override string Kind { get { return $"RGB565 · double buffered, buffer {FrontBuffer} shown"; } }

        public DoubleFramebuffer(string DeviceName, string DeviceID, Bus bus) : base(DeviceName, DeviceID, bus)
        {
            ControlLines.Add("swap", () => swap = true);
        }

        public override void Latch()
        {
            base.Latch();
            if (swap)
            {
                (front, Pixels) = (Pixels, front);
                FrontBuffer ^= 1;
                Swaps++;
                MarkDirty(0, 0, Width - 1, Height - 1);
                swap = false;
            }
        }

        // Drawing in the back buffer changes nothing on the screen.
        protected override void Drew(int left, int top, int right, int bottom)
        {
        }

        public override void Observe(WatchValue watch)
        {
            base.Observe(watch);
            watch(ID() + ".front", () => FrontBuffer);
        }
    }
}
