# exuarch

Check, assemble and run [ExµArch](https://www.exuarch.com) machine packages from the command line. ExµArch is a computer architecture playground in the browser: you design a machine from devices and buses, write the microcode that defines its instruction set, and run programs on it one clock tick at a time. **Export** in the app saves a machine, its microcode and its programs as one JSON file. This tool reads those files and simulates them exactly as the app does, which makes it a good companion for editing packages by hand or with an AI agent.

```sh
dotnet tool install -g exuarch    # or run it once with: dnx exuarch <command>
```

## Commands

```
exuarch examples                                   the built in packages and their numbered programs
exuarch export <built in package> [file.json]      a built in package as JSON, to start from
exuarch validate <package>                         every error in the machine, its microcode and its programs
exuarch assemble <package> [--program <name or number> | --file <program.asm>]
exuarch run <package> [--program ... | --file ...] [--ticks <n>] [--memory <device>:<start>[:<count>]]
exuarch docs [<guide> | <device type>] [--search <words>]
```

`<package>` is a package file or the name of a built in package, such as `BYOC-16`. `run` stops when the machine halts or after `--ticks` ticks (10,000,000 by default), then prints every LCD, a text picture of every screen with something on it, every register and counter, and any memory you ask for. Exit codes: 0 when all is well, 1 when the package or program has errors, 2 when the command line is wrong.

For Claude Code there is a plugin that teaches Claude to work on packages with this tool: `/plugin install exuarch --marketplace olebru/exuarch`. The source is at [github.com/olebru/exuarch](https://github.com/olebru/exuarch).
