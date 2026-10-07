using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // What the definition rules share while they check one machine definition.
    internal sealed class DefinitionContext
    {
        public DefinitionContext(MachineDefinition definition, DeviceRegistry registry)
        {
            Definition = definition;
            Registry = registry;
        }
        public MachineDefinition Definition { get; }
        public DeviceRegistry Registry { get; }
        public List<string> Errors { get; } = new List<string>();
        // The IDs defined so far.
        public HashSet<string> BusIds { get; } = new HashSet<string>();
        public HashSet<string> DeviceIds { get; } = new HashSet<string>();
    }

    internal interface IDefinitionRule
    {
        void Check(DefinitionContext context);
    }
    // A check of one device. info is its type's description, null when the type is unknown. Returns false when the
    // device's other checks would make no sense.
    internal interface IDeviceRule
    {
        bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info);
    }

    // Checks a machine definition on its own, before any device is built. The rules run in the order their problems
    // are listed.
    internal static class DefinitionRules
    {
        private static readonly IDefinitionRule[] Rules =
        {
            new BusIdsAreUnique(),
            new EachDevice(d => true, new HasId(), new IdHasNoDot(), new IdIsUnique(), new TypeIsKnown(), new ParametersAreKnown(),
                new PortsAreKnown(), new ConnectionNamesAreKnown(), new DefaultPortIsSetOnce(), new PortsUseKnownBuses()),
            new EachDevice(d => d.Id != null, new ConnectionsUseKnownDevices(), new MasteredDevicesShareTheBus()),
            new DecoderIsSet(),
            new RolesAreKnownDevices(),
        };

        public static void Validate(MachineDefinition definition, DeviceRegistry registry)
        {
            var context = new DefinitionContext(definition, registry);
            foreach (var rule in Rules) rule.Check(context);
            if (context.Errors.Count > 0) throw new MachineDefinitionException(context.Errors);
        }

        // "it has none" or "it has a, b", for a message about something a device type does not have.
        public static string Has(IReadOnlyCollection<string> names, string none = "it has none")
        {
            return names.Count == 0 ? none : $"it has {string.Join(", ", names)}";
        }
    }

    internal sealed class BusIdsAreUnique : IDefinitionRule
    {
        public void Check(DefinitionContext context)
        {
            foreach (var bus in context.Definition.Buses)
            {
                if (string.IsNullOrWhiteSpace(bus.Id)) context.Errors.Add("Every bus needs an \"id\".");
                else if (!context.BusIds.Add(bus.Id)) context.Errors.Add($"Bus '{bus.Id}' is defined more than once.");
            }
        }
    }

    // Runs device rules on each device the filter takes, in order, until one says to stop.
    internal sealed class EachDevice : IDefinitionRule
    {
        private readonly Func<DeviceDefinition, bool> filter;
        private readonly IDeviceRule[] rules;
        public EachDevice(Func<DeviceDefinition, bool> filter, params IDeviceRule[] rules)
        {
            this.filter = filter;
            this.rules = rules;
        }
        public void Check(DefinitionContext context)
        {
            foreach (var device in context.Definition.Devices.Where(filter))
            {
                var info = context.Registry.IsRegistered(device.Type) ? context.Registry.Info(device.Type) : null;
                foreach (var rule in rules)
                {
                    if (!rule.Check(context, device, info)) break;
                }
            }
        }
    }

    internal sealed class HasId : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (!string.IsNullOrWhiteSpace(device.Id)) return true;
            context.Errors.Add("Every device needs an \"id\".");
            return false;
        }
    }

    internal sealed class IdHasNoDot : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (device.Id.Contains('.')) context.Errors.Add($"Device '{device.Id}': an id can not contain '.', which separates the device from the line in a signal.");
            return true;
        }
    }

    internal sealed class IdIsUnique : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (!context.DeviceIds.Add(device.Id)) context.Errors.Add($"Device '{device.Id}' is defined more than once.");
            return true;
        }
    }

    internal sealed class TypeIsKnown : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (info == null) context.Errors.Add($"Device '{device.Id}': unknown type '{device.Type}', known types are {string.Join(", ", context.Registry.Types)}.");
            return true;
        }
    }

    internal sealed class ParametersAreKnown : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (info == null) return true;
            var known = info.Parameters.Select(p => p.Name).ToList();
            foreach (var name in device.Parameters.Keys.Where(k => !known.Contains(k)))
            {
                context.Errors.Add($"Device '{device.Id}': a {device.Type} has no parameter '{name}', {DefinitionRules.Has(known)}.");
            }
            return true;
        }
    }

    // Ports and connections are checked the same way, so a misspelt one is not ignored; a type registered without
    // describing itself (no control lines) can not be checked.
    internal sealed class PortsAreKnown : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (!IsDescribed(info)) return true;
            foreach (var port in device.Ports().Select(p => p.Key).Where(p => !info.Ports.Contains(p)))
            {
                context.Errors.Add($"Device '{device.Id}': a {device.Type} has no bus port '{port}', {DefinitionRules.Has(info.Ports, "it is on no bus")}.");
            }
            return true;
        }
        public static bool IsDescribed(DeviceTypeInfo info) { return info?.ControlLines.Count > 0; }
    }

    internal sealed class ConnectionNamesAreKnown : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (!PortsAreKnown.IsDescribed(info)) return true;
            var connections = info.Connections.Select(c => c.Name).ToList();
            foreach (var name in device.Connections.Keys.Where(k => !connections.Contains(k)))
            {
                context.Errors.Add($"Device '{device.Id}': a {device.Type} has no connection '{name}', {DefinitionRules.Has(connections)}.");
            }
            return true;
        }
    }

    internal sealed class DefaultPortIsSetOnce : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            if (device.Bus != null && device.Buses.ContainsKey(DeviceBuildContext.DefaultPort))
            {
                context.Errors.Add($"Device '{device.Id}': port '{DeviceBuildContext.DefaultPort}' is set by both \"bus\" and \"buses\".");
            }
            return true;
        }
    }

    internal sealed class PortsUseKnownBuses : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            foreach (var port in device.Ports().Where(p => !context.BusIds.Contains(p.Value)))
            {
                context.Errors.Add($"Device '{device.Id}': port '{port.Key}' connects to unknown bus '{port.Value}'.");
            }
            return true;
        }
    }

    internal sealed class ConnectionsUseKnownDevices : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            foreach (var connection in device.Connections.Where(c => !context.DeviceIds.Contains(c.Value)))
            {
                context.Errors.Add($"Device '{device.Id}': connection '{connection.Key}' refers to unknown device '{connection.Value}'.");
            }
            return true;
        }
    }

    // A bus master drives a connected device's lines while it puts the value on one of its own buses (ConnectionInfo.
    // OnPort), so the device has to be on that bus, or it would take whatever is on its own.
    internal sealed class MasteredDevicesShareTheBus : IDeviceRule
    {
        public bool Check(DefinitionContext context, DeviceDefinition device, DeviceTypeInfo info)
        {
            foreach (var connection in info?.Connections.Where(c => c.OnPort != null) ?? Enumerable.Empty<ConnectionInfo>())
            {
                if (device.Connections.TryGetValue(connection.Name, out var targetId)) Check(context, device, connection.Name, connection.OnPort, targetId);
            }
            return true;
        }

        private static void Check(DefinitionContext context, DeviceDefinition device, string connection, string port, string targetId)
        {
            var target = context.Definition.FindDevice(targetId);
            var bus = device.GetPortBus(port);
            var targetBus = target?.Ports().Select(p => p.Value).FirstOrDefault();
            if (target != null && bus != null && targetBus != bus)
            {
                context.Errors.Add($"Device '{device.Id}': connection '{connection}' is '{targetId}', which is on bus '{targetBus ?? "none"}', but it has to be on the {port} bus, '{bus}'.");
            }
        }
    }

    internal sealed class DecoderIsSet : IDefinitionRule
    {
        public void Check(DefinitionContext context)
        {
            if (context.Definition.Decoder == null) context.Errors.Add("\"decoder\" with \"status\" and \"instructionRegister\" is required.");
        }
    }

    // Each device role names a device that exists, and a required one is set. Without a decoder, its roles are
    // covered by DecoderIsSet.
    internal sealed class RolesAreKnownDevices : IDefinitionRule
    {
        public void Check(DefinitionContext context)
        {
            foreach (var role in DeviceRole.All.Where(r => !r.OnDecoder || context.Definition.Decoder != null)) Check(context, role);
        }

        private static void Check(DefinitionContext context, DeviceRole role)
        {
            var id = role.DeviceIn(context.Definition);
            if (id == null && role.Required) context.Errors.Add($"\"{role.Setting}\" is required.");
            else if (id != null && !context.DeviceIds.Contains(id)) context.Errors.Add($"\"{role.Setting}\" refers to unknown device '{id}'.");
        }
    }
}
