using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Devices
{
    // How a card shows a kind of device: what it says about the device's state, whether that takes two lines rather
    // than one, and the word its strip of bits shows, if it has one.
    public sealed record DeviceView(Func<DeviceState, RenderFragment> Summary, bool Stacked, Func<IBusDevice, int?> Word);

    public static class DeviceViews
    {
        private static readonly DeviceView Nothing = new DeviceView(_ => _ => { }, false, _ => null);

        // By the device's class; a class without one of its own is shown as the nearest class it derives from.
        private static readonly Dictionary<Type, DeviceView> ByType = new Dictionary<Type, DeviceView>
        {
            [typeof(StatusRegister)] = View<StatusRegister>((status, _) => DeviceState.Flags(status)),
            [typeof(InstructionRegister)] = View<InstructionRegister>((counter, state) => DeviceState.MicroStep(counter, state.Machine), word: counter => counter.Data),
            [typeof(Register)] = View<Register>((register, _) => DeviceState.Word(register.Data), word: register => register.Data),
            [typeof(DualPortRegister)] = View<DualPortRegister>((register, _) => DeviceState.Word(register.Data), word: register => register.Data),
            [typeof(RegisterFile)] = View<RegisterFile>((file, _) => DeviceState.Registers(file), stacked: true),
            [typeof(MemoryModule)] = View<MemoryModule>((memory, _) => DeviceState.Memory(memory), stacked: true),
            [typeof(MMU)] = View<MMU>((mmu, _) => DeviceState.Banks(mmu), stacked: true),
            [typeof(Clock)] = View<Clock>((clock, _) => DeviceState.Ticks(clock), stacked: true),
            [typeof(CharacterDisplay)] = View<CharacterDisplay>((lcd, _) => DeviceState.Lcd(lcd), stacked: true),
            [typeof(Framebuffer)] = View<Framebuffer>((screen, _) => DeviceState.Screen(screen), stacked: true),
            [typeof(InterruptController)] = View<InterruptController>((controller, _) => DeviceState.Interrupts(controller), stacked: true),
            [typeof(TickTimer)] = View<TickTimer>((timer, _) => DeviceState.Timer(timer), stacked: true),
            [typeof(RealTimeClock)] = View<RealTimeClock>((rtc, _) => DeviceState.RealTime(rtc), stacked: true),
            [typeof(Blitter)] = View<Blitter>((blitter, _) => DeviceState.Blits(blitter), stacked: true),
            [typeof(Rasterizer)] = View<Rasterizer>((rasterizer, _) => DeviceState.Triangles(rasterizer), stacked: true),
            [typeof(DepthBuffer)] = View<DepthBuffer>((depth, _) => DeviceState.Depths(depth), stacked: true),
            [typeof(MultiplyAccumulate)] = View<MultiplyAccumulate>((mac, _) => DeviceState.Accumulator(mac), stacked: true),
            [typeof(Keypad)] = View<Keypad>((keypad, _) => DeviceState.Keys(keypad)),
            [typeof(ALU)] = View<ALU>((_, state) => DeviceState.Operation(state.Signals.FirstOrDefault(), state.AluResult), stacked: true),
        };

        public static DeviceView For(IBusDevice device)
        {
            for (var type = device.GetType(); type != null; type = type.BaseType)
            {
                if (ByType.TryGetValue(type, out var view)) return view;
            }
            return Nothing;
        }

        private static DeviceView View<T>(Func<T, DeviceState, RenderFragment> summary, bool stacked = false, Func<T, int> word = null) where T : IBusDevice
        {
            Func<IBusDevice, int?> bits = word == null ? Nothing.Word : device => word((T)device);
            return new DeviceView(state => summary((T)state.Built, state), stacked, bits);
        }
    }
}
