---
name: exuarch
description: Design, edit, debug and explain ExuArch (ExµArch) machine packages, the .json files exported from exuarch.com that hold a CPU's hardware, its microcode and its assembly programs. Use when the user wants to change or build a machine, add or fix an instruction or a device, write or debug a program for one of these machines, or understand why a package does not work. Checks every change with the exuarch command line tool.
---

# ExuArch machine packages

An ExuArch package is one JSON file holding a whole computer: its hardware (`machine.buses`, `machine.devices`), its microcode (`machine.decoder.microcode`: a `fetch` routine and `instructions`, each a list of steps of control signals such as `pc.output`), and its programs (`programs[].source`, assembly for that machine's own instruction set). People design these at https://www.exuarch.com, **Export** them to a file, edit them, and **Import…** them back.

You can not see the app, so never guess. The `exuarch` tool checks, assembles and runs packages exactly like the app does, and serves its handbook. Use it after every change.

## Getting the tool

Check for it with `exuarch --version`. If it is missing:

- `dotnet tool install -g exuarch` installs it, which needs the .NET 10 SDK. Ask the user before installing anything.
- `dnx exuarch <command> ...` runs it once without installing, also with the .NET 10 SDK.
- Inside a clone of the olebru/exuarch repository: `dotnet run --project Exuarch.Cli -- <command> ...`.

## Commands

| Command | What it does |
|---|---|
| `exuarch examples` | Lists the built in packages and their numbered programs |
| `exuarch export <name> [file.json]` | Writes a built in package as JSON; never overwrites a file |
| `exuarch validate <package>` | Checks the machine, its microcode and every program; lists all errors and warnings |
| `exuarch assemble <package> [--program <name or number> \| --file <x.asm>]` | Prints the listing: address, words, source |
| `exuarch run <package> [--program ... \| --file ...] [--ticks <n>] [--memory <device>:<start>[:<count>]]` | Runs until the machine halts or `--ticks` (10,000,000 by default) run out, then prints every LCD, a text picture of every screen that has something on it, every register and counter, and the memory asked for |
| `exuarch docs` | Lists the handbook guides and every device type |
| `exuarch docs <guide or device type>` | Prints one page, as Markdown |
| `exuarch docs --search <words>` | Searches the handbook |

`<package>` is a file path or a built in package name such as `BYOC-16`. Exit code 0 means all is well, 1 means the package or program has errors, 2 means the command line was wrong. Numbers can be written in hex (`0x100`). For a memory behind an MMU, name the bank: `--memory mmu/1:0:16`.

## How to work

1. **Start from something that works.** Edit the user's exported file, or `exuarch export` the built in package closest to what they want (`exuarch examples` shows them). Run `exuarch validate` on it first, so you know which problems are yours.
2. **Read before you write.** Before using a device type, run `exuarch docs <type>` for its control lines, ports, connections and parameters. Never invent a type, a control line, a port or a parameter. For the ideas, read the guides: `devices-and-control-lines`, `microcode`, `fetch-and-the-instruction-register`, `flags-and-conditions`, `operands`, `assembly`, `memory-and-banks`, `packages`, and `opcode-fields` for machines whose microcode sets `opcodeBits`. Look at how the example packages do the same thing.
3. **Change one thing at a time** and run `exuarch validate` after each change. Fix every error before going on, and read every warning.
4. **Prove it runs.** Write a short test program that uses what you changed, run it with `exuarch run --file`, and check the result in the printed registers, the LCD, the screen picture or a `--memory` dump. A program that should halt but prints `Still running` is a bug: the line saying where it was is the place to start.
5. **Hand it back.** Tell the user what changed and how you checked it, and that **Import…** in the Machines drawer at exuarch.com loads the file. Importing a file named after a package they already have replaces it, after asking.

## Things to keep in mind

- Keep everything the user did not ask you to change as it is, including names, `layout` positions and formatting. Give new devices and buses a `layout` near the parts they connect to, like their neighbours.
- Any property the format does not know is an error, so `validate` catches misspellings. The JSON may contain comments and trailing commas.
- `run` can not press keys: a program that reads the keypad sees none held. Programs that loop for ever, such as games and animations, always end at the tick limit; judge them by what they have drawn or printed by then.
- When an instruction's behaviour is wrong, look at its steps and their `when` conditions in `machine.decoder.microcode`, then compare with the same instruction in an example package.
