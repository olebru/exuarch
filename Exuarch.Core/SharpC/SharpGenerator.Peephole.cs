using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private static bool IsInstruction(SharpOutLine x) => x != null && x.Kind == "ins";

        private static bool IsLayout(SharpOutLine x) => x.Kind == "comment" || x.Kind == "blank";

        private void Peephole()
        {
            while (PeepholeStep()) { }
            DropUnusedLabels();
        }

        private bool PeepholeStep()
        {
            for (int i = 0; i < output.Count; i++)
            {
                var a = output[i];
                if (!IsInstruction(a)) continue;
                if (a.Mnemonic == "JMP" && DropJumpToNext(i, a)) return true;
                if (CombineWithNext(i, a)) return true;
            }
            return false;
        }

        private bool DropJumpToNext(int i, SharpOutLine a)
        {
            for (int j = i + 1; j < output.Count && (output[j].Kind == "label" || IsLayout(output[j])); j++)
            {
                if (output[j].Kind != "label" || output[j].Name != a.Operand) continue;
                output.RemoveAt(i);
                return true;
            }
            return false;
        }

        private bool CombineWithNext(int i, SharpOutLine a)
        {
            int j = i + 1;
            while (j < output.Count && IsLayout(output[j])) j++;
            var b = j < output.Count ? output[j] : null;
            if (!IsInstruction(b)) return false;
            if (IsReload(a, b) || (a.Mnemonic == "TST" && b.Mnemonic == "TST"))
            {
                output.RemoveAt(j);
                return true;
            }
            if (a.Mnemonic != "PUSH" || b.Mnemonic != "POP") return false;
            output.RemoveAt(j);
            a.Mnemonic = "TAB";
            a.Text = "        TAB";
            return true;
        }

        private static bool IsReload(SharpOutLine a, SharpOutLine b)
        {
            if (a.Operand != b.Operand) return false;
            return (a.Mnemonic == "STL" && b.Mnemonic == "LDL") || (a.Mnemonic == "ST" && b.Mnemonic == "LD");
        }

        private void DropUnusedLabels()
        {
            var used = new HashSet<string>(output.Where(x => x.Kind == "ins" && !string.IsNullOrEmpty(x.Operand)).Select(x => x.Operand));
            foreach (var x in output.Where(x => x.Kind == "runtime" || x.Kind == "data"))
            {
                foreach (Match m in Regex.Matches(x.Text, "__[A-Za-z0-9_]+")) used.Add(m.Value);
            }
            output.RemoveAll(x => x.Kind == "label" && IsDroppable(x.Name, used));
        }

        private static bool IsDroppable(string name, HashSet<string> used)
        {
            if (!name.StartsWith("__") || used.Contains(name)) return false;
            return name.StartsWith("__ret_") || (name != "__init" && name != "__heap_start");
        }
    }
}
