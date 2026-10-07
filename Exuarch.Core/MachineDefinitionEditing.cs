using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // Edits that keep every reference in a definition consistent, used by the machine editor.
    public static class MachineDefinitionEditing
    {
        public static DeviceDefinition FindDevice(this MachineDefinition definition, string id)
        {
            return definition.Devices.FirstOrDefault(d => d.Id == id);
        }
        public static BusDefinition FindBus(this MachineDefinition definition, string id)
        {
            return definition.Buses.FirstOrDefault(b => b.Id == id);
        }

        // Returns prefix if unused, otherwise prefix2, prefix3, ...
        public static string NextFreeId(this MachineDefinition definition, string prefix)
        {
            bool Used(string id) => definition.FindDevice(id) != null || definition.FindBus(id) != null;
            if (!Used(prefix)) return prefix;
            for (int i = 2; ; i++)
            {
                if (!Used(prefix + i)) return prefix + i;
            }
        }

        public static void RenameDevice(this MachineDefinition definition, string oldId, string newId)
        {
            var device = definition.FindDevice(oldId) ?? throw new ArgumentException($"No device '{oldId}'.");
            if (oldId == newId) return;
            CheckNewId(definition, newId);
            device.Id = newId;
            foreach (var (other, connection) in ConnectionsTo(definition, oldId)) other.Connections[connection] = newId;
            ReassignRoles(definition, oldId, newId);
            foreach (var step in MicroSteps(definition))
            {
                for (int i = 0; i < step.Signals.Count; i++)
                {
                    if (Signal.TryParse(step.Signals[i], out var signal) && signal.Device == oldId) step.Signals[i] = $"{newId}.{signal.Line}";
                }
            }
        }

        // Every connection of another device to this one, as the connecting device and the connection's name.
        private static List<(DeviceDefinition Device, string Connection)> ConnectionsTo(MachineDefinition definition, string id)
        {
            return definition.Devices.SelectMany(d => d.Connections.Where(c => c.Value == id).Select(c => (d, c.Key))).ToList();
        }

        // Gives the device's roles (decoder status, halt clock and so on) to another device, or to none.
        private static void ReassignRoles(MachineDefinition definition, string id, string newId)
        {
            foreach (var role in DeviceRole.All.Where(r => r.DeviceIn(definition) == id)) role.Assign(definition, newId);
        }

        // Every place the microcode enables a control line of the device: instruction, step index and signal.
        public static List<(InstructionDefinition Instruction, int Step, string Signal)> SignalUsages(this MachineDefinition definition, string deviceId)
        {
            var usages = new List<(InstructionDefinition, int, string)>();
            var microcode = definition.Decoder?.Microcode;
            if (microcode == null) return usages;
            foreach (var instruction in microcode.AllInstructions)
            {
                for (int step = 0; step < instruction.Steps.Count; step++)
                {
                    foreach (var text in instruction.Steps[step].Signals)
                    {
                        if (Signal.TryParse(text, out var signal) && signal.Device == deviceId) usages.Add((instruction, step, text));
                    }
                }
            }
            return usages;
        }

        // Removes the device's signals from the microcode. Steps left empty stay, so step numbering is kept.
        public static int RemoveSignalsOf(this MachineDefinition definition, string deviceId)
        {
            int removed = 0;
            foreach (var step in MicroSteps(definition))
            {
                removed += step.Signals.RemoveAll(text => Signal.TryParse(text, out var signal) && signal.Device == deviceId);
            }
            return removed;
        }

        private static IEnumerable<MicroStep> MicroSteps(MachineDefinition definition)
        {
            var microcode = definition.Decoder?.Microcode;
            return microcode == null ? Enumerable.Empty<MicroStep>() : microcode.AllInstructions.SelectMany(i => i.Steps);
        }

        // Removes the device and every reference to it, except its microcode signals: those become errors
        // unless RemoveSignalsOf is called as well.
        public static void RemoveDevice(this MachineDefinition definition, string id)
        {
            definition.Devices.RemoveAll(d => d.Id == id);
            foreach (var (other, connection) in ConnectionsTo(definition, id)) other.Connections.Remove(connection);
            ReassignRoles(definition, id, null);
        }

        public static void RenameBus(this MachineDefinition definition, string oldId, string newId)
        {
            var bus = definition.FindBus(oldId) ?? throw new ArgumentException($"No bus '{oldId}'.");
            if (oldId == newId) return;
            CheckNewId(definition, newId);
            bus.Id = newId;
            foreach (var device in definition.Devices)
            {
                foreach (var port in device.Ports().Where(p => p.Value == oldId).ToList())
                {
                    device.SetPortBus(port.Key, newId);
                }
            }
        }

        public static void RemoveBus(this MachineDefinition definition, string id)
        {
            definition.Buses.RemoveAll(b => b.Id == id);
            foreach (var device in definition.Devices)
            {
                foreach (var port in device.Ports().Where(p => p.Value == id).ToList())
                {
                    device.SetPortBus(port.Key, null);
                }
            }
        }

        public const double LayoutCardWidth = SchematicLayout.CardWidth;
        public const double LayoutFirstBusY = SchematicLayout.FirstBusY;
        public const double LayoutBusSpacing = SchematicLayout.BusSpacing;

        // Gives buses and devices without a position one. A machine with nothing placed yet, or any machine with force,
        // is laid out as a whole (see SchematicPlacement). Otherwise only what is missing is placed: a bus below the
        // last one, a device in a free spot next to its bus, and the decoder at the end of the top row.
        public static void EnsureLayout(this MachineDefinition definition, bool force = false)
        {
            bool nothingPlaced = definition.Devices.All(d => d.Layout == null) && definition.Decoder?.Layout == null;
            if (force || nothingPlaced)
            {
                SchematicPlacement.LayOut(definition);
                return;
            }
            foreach (var bus in definition.Buses.Where(b => b.Layout == null))
            {
                var lowest = definition.Buses.Where(b => b.Layout != null).Select(b => b.Layout.Y)
                    .Concat(definition.Devices.Where(d => d.Layout != null).Select(d => d.Layout.Y + SchematicLayout.CardMinHeight))
                    .DefaultIfEmpty(LayoutFirstBusY - LayoutBusSpacing).Max();
                bus.Layout = new Position { X = 0, Y = lowest + SchematicPlacement.ToBus + SchematicLayout.CardMinHeight + SchematicPlacement.ToBus };
            }
            foreach (var device in definition.Devices.Where(d => d.Layout == null).ToList())
            {
                device.Layout = SchematicPlacement.FreeSpot(definition, device);
            }
            // The decoder is drawn as a card too, to the right of the devices in the top row.
            if (definition.Decoder != null && definition.Decoder.Layout == null)
            {
                var placed = definition.Devices.Where(d => d.Layout != null).ToList();
                var top = placed.Count == 0 ? SchematicPlacement.Top : placed.Min(d => d.Layout.Y);
                var right = placed.Where(d => d.Layout.Y < top + 100).Select(d => d.Layout.X + LayoutCardWidth).DefaultIfEmpty(0).Max();
                definition.Decoder.Layout = new Position { X = right + SchematicPlacement.ColumnSpacing - LayoutCardWidth, Y = top };
            }
        }

        public static MachineDefinition Clone(this MachineDefinition definition)
        {
            return MachineDefinition.FromJson(definition.ToJson());
        }

        private static void CheckNewId(MachineDefinition definition, string newId)
        {
            if (string.IsNullOrWhiteSpace(newId)) throw new ArgumentException("ID can not be empty.");
            if (newId.Any(char.IsWhiteSpace)) throw new ArgumentException("ID can not contain spaces.");
            if (newId.Contains('.')) throw new ArgumentException("ID can not contain '.', which separates the device from the line in a signal.");
            if (definition.FindDevice(newId) != null || definition.FindBus(newId) != null)
            {
                throw new ArgumentException($"'{newId}' is already used.");
            }
        }
    }
}
