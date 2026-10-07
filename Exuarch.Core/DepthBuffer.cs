using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A 640 x 480 memory of depths, one 16 bit word per pixel of the framebuffer: how far away the nearest thing
    // drawn at that pixel is, smaller being nearer. clear sets every depth to 65535, as far as it goes. It has a
    // cursor like the framebuffer's: loadx and loady set it from the bus, output puts the depth at the cursor on the
    // bus, load writes the bus value there, and next moves the cursor right (wrapping to the next row), so a
    // rasterizer can read, compare and write one pixel after the other.
    public class DepthBuffer : ControlLineDevice, IBusDevice, IScreen, IWriteTracked
    {
        public const int Width = Framebuffer.Width;
        public const int Height = Framebuffer.Height;
        public const int Far = 0xFFFF;

        public readonly ushort[] Depths = new ushort[Width * Height];
        public int X { get; private set; }
        public int Y { get; private set; }
        public long WriteCount { get; private set; }
        public int LastWriteAddress { get; private set; } = -1;
        public string Kind { get { return "depth, near is bright"; } }
        public bool IsDepth { get { return true; } }
        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool loadX, loadY, output, load, next, clear;
        private int dirtyLeft, dirtyTop, dirtyRight = Width - 1, dirtyBottom = Height - 1;

        public DepthBuffer(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            Array.Fill(Depths, (ushort)Far);
            ControlLines
                .Add("loadx", () => loadX = true)
                .Add("loady", () => loadY = true)
                .Add("output", () => output = true)
                .Add("load", () => load = true)
                .Add("next", () => next = true)
                .Add("clear", () => clear = true);
        }

        public int ValueAt(int address) { return Depths[address]; }
        public int DepthAt(int x, int y) { return Depths[y * Width + x]; }

        public void Drive()
        {
            if (output)
            {
                bus.Data = Depths[Y * Width + X];
                output = false;
            }
        }
        public void Latch()
        {
            if (clear)
            {
                Array.Fill(Depths, (ushort)Far);
                X = Y = 0;
                MarkDirty(0, 0, Width - 1, Height - 1);
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
            if (load)
            {
                int address = Y * Width + X;
                Depths[address] = (ushort)bus.Data;
                LastWriteAddress = address;
                WriteCount++;
                MarkDirty(X, Y, X, Y);
                load = false;
            }
            // After load, so load and next in one tick write the pixel and move on.
            if (next)
            {
                if (++X == Width)
                {
                    X = 0;
                    Y = (Y + 1) % Height;
                }
                next = false;
            }
        }

        private void MarkDirty(int left, int top, int right, int bottom)
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

        // The raw depths; the Run view draws them in grey, near white and far black.
        public ushort[] Words { get { return Depths; } }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
    }
}
