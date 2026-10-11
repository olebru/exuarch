using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public interface IScreen : IBusDevice
    {
        int X { get; }
        int Y { get; }
        string Kind { get; }
        bool IsDepth { get; }
        (int X, int Y, int Width, int Height)? TakeDirtyRegion();
        ushort[] Words { get; }
    }

    public class Framebuffer : ControlLineDevice, IBusDevice, IScreen, IWriteTracked, IObservableState, IPassiveDevice
    {
        public const int Width = 640;
        public const int Height = 480;

        public ushort[] Pixels { get; protected set; } = new ushort[Width * Height];
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

        public (int X, int Y, int Width, int Height)? TakeDirtyRegion()
        {
            if (dirtyRight < 0) return null;
            var region = (dirtyLeft, dirtyTop, dirtyRight - dirtyLeft + 1, dirtyBottom - dirtyTop + 1);
            dirtyLeft = Width; dirtyTop = Height; dirtyRight = -1; dirtyBottom = -1;
            return region;
        }

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

        public byte[] ToRgb565Bytes(int x, int y, int width, int height)
        {
            var bytes = new byte[width * height * 2];
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(Shown, ((y + row) * Width + x) * 2, bytes, row * width * 2, width * 2);
            }
            return bytes;
        }

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

    public class DoubleFramebuffer : Framebuffer
    {
        private ushort[] front = new ushort[Width * Height];
        private bool swap;
        public override ushort[] Shown { get { return front; } }
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
