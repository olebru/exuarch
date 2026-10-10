# Working with AI and the command line

A [package](exuarch:guide/packages) is one JSON file, so anything that edits text can change a machine: you in your own editor, or an AI coding agent such as Claude Code. What an agent can not do is look at the app to see whether its change works. The `exuarch` command line tool gives it the same checks, assembler and simulator as the app, so it can test every change before you import it.

## The exuarch tool

The tool needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). Install it once:

```sh
dotnet tool install -g exuarch
```

or run it without installing, as `dnx exuarch <command>`. It reads package files exported from here, and knows the built in machines by name:

- `exuarch examples` lists the built in machines and their numbered programs.
- `exuarch export BYOC-16 mine.json` saves a built in machine as a file, to start from.
- `exuarch validate mine.json` checks the machine, its microcode and every program, and lists every error and warning.
- `exuarch assemble mine.json --program 2` prints a program's listing: address, words and source.
- `exuarch run mine.json --program 2` runs a program until it halts, then prints the LCD, a text picture of the screen, and every register and counter.
- `exuarch run mine.json --file test.asm --memory ram:0x100:16` runs a program from a file and shows sixteen cells of `ram` afterwards.
- `exuarch docs microcode` prints a page of this handbook, or a device's reference page; `exuarch docs` lists them.

A run stops after 10,000,000 ticks unless the machine halts first; `--ticks` changes that. It can not press keys, so a program that reads the keypad sees none held.

## With Claude Code

The ExµArch plugin teaches Claude how packages work, and to check each change with the tool. Install it in Claude Code:

```
/plugin install exuarch --marketplace olebru/exuarch
```

Then export a machine, open a terminal in the folder with the file, and ask for what you want, for example:

> Add an instruction to mine.json that swaps registers A and B, and a program that shows it working on the LCD.

Claude reads the device reference before it uses a device, runs `exuarch validate` after each change, and proves the result with `exuarch run`. Other agents can use the same instructions: they are in `plugins/exuarch/skills/exuarch/SKILL.md` in the [source repository](https://github.com/olebru/exuarch).

## Back into the app

**Import…** in the header opens the edited file. A file with the name of a package you already have replaces it, after asking, so export anything you want to keep first. An imported machine can not do anything outside the simulator, whoever or whatever wrote it: [running other people's machines](exuarch:guide/safety) explains why.

## See also

- [Packages](exuarch:guide/packages): what is in the file an agent edits
- [Microcode](exuarch:guide/microcode)
- [Devices and control lines](exuarch:guide/devices-and-control-lines)
