using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Web.Components
{
    public sealed class UiLevel
    {
        public const string EssentialsName = "essentials";
        public const string EverythingName = "everything";

        public static readonly IReadOnlySet<string> EssentialDeviceTypes = new HashSet<string>
        {
            "register", "statusRegister", "instructionRegister", "clock", "alu", "ram", "rom", "display", "keypad", "interruptController", "timer",
        };

        public bool ShowsEverything { get; private set; } = true;
        public string Name { get { return ShowsEverything ? EverythingName : EssentialsName; } }
        public event Action Changed;

        public void Load(string stored)
        {
            ShowsEverything = stored != EssentialsName;
        }

        public void Set(bool showsEverything)
        {
            if (ShowsEverything == showsEverything) return;
            ShowsEverything = showsEverything;
            Changed?.Invoke();
        }

        public bool ShowsDeviceType(string type, IEnumerable<string> typesInMachine)
        {
            return ShowsEverything || EssentialDeviceTypes.Contains(type) || typesInMachine.Contains(type);
        }
    }
}
