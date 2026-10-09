# Packages

A package is everything that makes up one computer in ExµArch: its hardware, its microcode, its programs and a note about it, kept together in a single JSON file. The built in examples are packages, and so is every machine you make. Knowing what is in the file helps when you want to share a machine, keep a copy of your work, or edit something by hand that the editors do not reach.

## What a package holds

A package file is one JSON object:

```json
{
  "name": "My machine",
  "description": "One or two sentences about it.",
  "readme": "# My machine\n\nA longer note, in Markdown.",
  "machine": { },
  "programs": [
    { "name": "Hello", "description": "Prints a greeting", "source": "; Hello\n..." }
  ]
}
```

- `name` identifies the package. It is also the name of the file Export writes.
- `description` is the short text shown on the example cards and as the package's tooltip.
- `readme` is the machine's note, in Markdown. It is optional.
- `machine` is the machine itself. It is required.
- `programs` lists the programs, each with a `name`, an optional `description` and its assembly `source`.

The JSON may contain comments and trailing commas. Any property the format does not know is an error, so a misspelt name is reported rather than silently ignored.

## The machine

The `machine` object is what the **JSON** view shows and edits (under *This machine* in the getting started drawer), with the microcode inline:

- `name`: the machine's name.
- `buses`: each bus's `id` and its position on the canvas.
- `devices`: one entry per device:
  - `id` and `type`, the device type, such as `register` or `ram`;
  - an optional display `name`;
  - `bus` for a device with one bus port, or `buses` mapping port names to buses for a device with several, such as a `dualPortRegister`;
  - `connections` to other devices, such as an ALU's `a`, `b` and `status`;
  - `parameters`, such as a ram's `size`;
  - its `layout` on the canvas.
- `decoder`:
  - `status`, the status register the flag conditions read;
  - `instructionRegister`, the micro step counter;
  - optionally `interrupts`, an interrupt controller;
  - `microcode`, holding its `name`, the `fetch` routine and the `instructions`.
- `halt`: the clock whose `disable` line halts the machine.
- `programMemory`: the ram the program is loaded into.

Each instruction has a `mnemonic`, a `description` and `operands`, the operand count. It can also have `operandTypes`, each `value` or `address`, and `steps`. Each step has its `signals`, written `device.line`, an optional `when` with flag conditions such as `{ "Z": true }`, and an optional `comment`.

Every change you make in the hardware design and microcode editors is a change to this object. The JSON view is simply the same thing as text. See [devices and control lines](exuarch:guide/devices-and-control-lines) and [microcode](exuarch:guide/microcode).

## Kept in the browser

Every change is saved in the browser half a second after you make it, and the next visit opens the machine you left, with the program that was in the editor. Nothing leaves your computer: the browser keeps it for this site only, and a private window keeps nothing.

The browser keeps two kinds of package:

- **Your own machines**, from **New…** or **Import…**. They are kept until you delete them from **Machines** in the getting started drawer.
- **Examples you have changed.** A built in package is only kept while it differs from the way it ships; **Reset** throws your changes away and brings it back as it ships, after asking. The package button in the header says "edited" when there is something to reset.

## New, Import and Export

- **New…** starts a machine of your own from the minimal CPU, from an empty bus, or as a copy of the machine that is open. A name another package already uses gets a number added.
- **Export** saves the open package as `<name>.json`, with the machine as it is now, its programs and its note. If the program in the editor is not one of them, it is added as "My program". **Machines** in the drawer can export any package without opening it.
- **Import…** opens a package file and keeps it. A file with the name of a package you already have replaces it, after asking; one named after a built in package becomes your changes to it. A file without a name takes the file's name.

Files are how you move machines between browsers or share them: the browser's copy is only in that browser. Before you import one from someone else, [running other people's machines](exuarch:guide/safety) explains what a package can and can not do.

In the [Program](exuarch:tab/Program) tab, **＋ New program** adds an empty program to the package. Programs you add, and the programs of your own machines, keep your edits as you type. A built in example program stays as it shipped: when you change it, the text in the editor is kept with the package, but the program itself is not changed, and its title says "(edited)".

## The note

The `readme` is shown under **This machine** in the Getting started drawer. For your own machines, Edit opens it with a live preview. It is ordinary Markdown with raw HTML turned off. Web links open in a new tab.

A note can also link into the app with `exuarch:` links:

| Link | Goes to |
| --- | --- |
| `exuarch:device/<id>` | that device in the hardware design |
| `exuarch:instruction/<mnemonic>` | that instruction in the microcode editor |
| `exuarch:program/<name>` | that program, loaded in the Program tab |
| `exuarch:tab/<tab>` | Hardware design, Microcode, Program, JSON or Run |
| `exuarch:package/<name>` | a built in package, loaded in place of this one |
| `exuarch:guide/<page>` | a page of this handbook |
| `exuarch:reference/<type>` | the reference page of a device type |

When the target has spaces, write the link target in angle brackets: `<exuarch:program/Fibonacci on the LCD>`. Links to other schemes, such as `javascript:`, are turned into dead links.

## The built in packages

The examples are packages too. They are stored as folders in the app, with a `package.json`, a `machine.json`, one `.asm` file per program and a `README.md`, and they load as the same kind of package. Each takes a different direction:

- [TINY-16](exuarch:package/TINY-16): one register and seven instructions, to start with.
- [BYOC-16](exuarch:package/BYOC-16): an accumulator machine on one bus.
- [STACK-16](exuarch:package/STACK-16): a stack machine with a data stack and a return stack.
- [RISC-16](exuarch:package/RISC-16): load/store with a register file, three register instructions and a link register.
- [CISC-16](exuarch:package/CISC-16): addressing modes on every instruction, memory to memory, and stack frames.
- [HARVARD-16](exuarch:package/HARVARD-16): three buses, with separate program and data memory.
- [MOVE-16](exuarch:package/MOVE-16): only moves between ports.
- [COPRO-16](exuarch:package/COPRO-16): a CPU and a blitter.
- [IRQ-16](exuarch:package/IRQ-16): interrupts.
- [ROM-16](exuarch:package/ROM-16): a ROM with its contents, and address registers that step by themselves.
- [GPU-16](exuarch:package/GPU-16): a small 3D pipeline with a rasterizer, a depth buffer and a multiply-accumulate unit.
- [FLIP-16](exuarch:package/FLIP-16): GPU-16 with a double buffered screen.

To build on one, open it and use **New…** with a copy of the current machine.

## See also

- [Getting started](exuarch:guide/getting-started)
- [Devices and control lines](exuarch:guide/devices-and-control-lines)
- [Microcode](exuarch:guide/microcode)
- [Assembly](exuarch:guide/assembly)
- [Running other people's machines](exuarch:guide/safety)
