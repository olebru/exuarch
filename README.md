# ExµArch

Explore microarchitectures: a computer architecture playground in the browser. Design a machine from devices and buses, write the microcode that drives it and the instruction set that microcode defines, then watch programs run on it one clock tick at a time.

## Use it online

ExµArch runs at **[www.exuarch.com](https://www.exuarch.com)**. There is nothing to install and no account: open the page and start with the getting started drawer, which leads to the tutorials, the handbook and the example machines.

- It is made for a computer with a keyboard and a mouse. A phone is shown a short introduction instead (`phone.html`), which suggests opening it on a computer and sending yourself the link, and links to the video series and the short videos of the machines on the [ExµArch YouTube channel](https://www.youtube.com/channel/UCKONGZLTGv8iY2x710OkjRQ/shorts); it never downloads the app unless you choose **Open ExµArch here**, which is remembered for that tab.
- It runs entirely in your browser. Machines, microcode and programs are saved in the browser's own storage, so they are there next time on the same browser and device. They never leave it, unless you connect a GitHub repository of your own: then every save also writes the machine there.
- Visits are counted anonymously with [Umami](https://umami.is): no cookies, nothing stored in your browser and no personal data, so there is no consent banner. Umami distinguishes visits by a hash of the address and the browser, with a salt it rotates (monthly by default), so two visits only count as the same visitor while the network, the browser and that period stay the same; ExµArch sends no id of its own. Besides the visitor's country, it counts how far a visit gets: running an example, reading a tutorial, editing hardware or microcode, running a program you wrote, building a machine of your own. It also counts where people get stuck (errors left standing for a while, not what they say), how long a visit is active and how fast the simulator runs, which start screen option is picked, when a machine is created, imported or exported, and on the introduction for phones, whether the visitor sends themselves the link, goes to watch the videos or opens the app anyway. A machine of your own is only counted as "own", never by name. Browsers that send Do Not Track are not counted.
- To move a machine to another browser or share it, use **Export** to save it as a file and **Import** to open it.
- After the first visit the site works offline, and it can be installed as an app from the browser's menu.
- The site runs the latest release; the footer shows which one.

## What you can do

- **Hardware design:** place registers, an ALU, memory, an MMU, displays, a keypad, timers, an interrupt controller, a blitter, a triangle rasterizer and more on any number of buses, and wire them together. The decoder sits on the canvas too.
- **Microcode:** write each instruction as steps of control lines (`device.line`), with steps that run only for certain flags.
- **Programs:** write assembly for the instruction set you made, with completion, hover help and formatting.
- **Run:** step tick by tick or run at full speed, with the buses, every device, memory, the decoder ROM and a trace on screen.
- **Nineteen example machines**, from TINY-16 with one register, a classic accumulator CPU (BYOC-16) and the breadboard classic (BEN-16) to a machine run from a ROM (ROM-16), a stack machine, a RISC with a register file, the same RISC with its operands packed into the word (FIELD-16) and with a multiport register file (PORT-16), with a hardware multiplier drawing the Mandelbrot set (DSP-16) and with a real DSP's Mandelbrot engine (BLAZE-16), a CISC with eight addressing modes, a Harvard machine, a transport triggered one, a CPU with a coprocessor, one with interrupts and a small 3D GPU, with and without a double buffered screen, and one rebuilt to draw its spinning cube as fast as it can. And one that does not exist anywhere else: WORM-16, which has no jumps, and whose program crawls round a ring memory as a queue of instructions that each compute whether they live on.
- **A handbook** in the app: tutorials that build a machine step by step, concept pages and a reference page for every device type.
- **Your work is kept in the browser.** Save keeps your changes and Save as… keeps a copy under a new name; unsaved changes survive a reload. Export saves a machine as a file and Import opens one.
- **Saving to GitHub, if you want it:** connect a repository of your own with a fine-grained token limited to it. Its machines are added to the browser, every save also writes `machines/<name>.json` there, and **Open from GitHub…** opens machines saved from another browser. The handbook page *Saving to GitHub* goes through it.
- **Community machines:** the gallery's **Community** tab lists machines other people made, from [exuarch-machines](https://github.com/olebru/exuarch-machines). To share one of yours, export it and open a pull request there; the handbook page *Sharing a machine with the community* goes through it.

## Work on machines with AI, or from the command line

An exported machine is one JSON file, so an AI coding agent can edit its hardware, microcode and programs. Two things make that work:

- **The `exuarch` tool** checks, assembles and runs a package exactly as the app does, and prints the handbook. It is a .NET tool on NuGet and needs the .NET 10 SDK: `dotnet tool install -g exuarch`, or `dnx exuarch <command>` to run it without installing.

  ```sh
  exuarch examples                          # the built in packages and their programs
  exuarch export BYOC-16 mine.json          # start from one of them
  exuarch validate mine.json                # every error in the machine, the microcode and the programs
  exuarch run mine.json --program 2         # run it; prints the LCD, the screen, the registers
  exuarch docs register                     # a handbook page
  ```

- **The Claude Code plugin** contains a skill for Claude on how packages work and on checking every change with the tool:

  ```
  /plugin install exuarch --marketplace olebru/exuarch
  ```

  On older Claude Code versions, run `/plugin marketplace add olebru/exuarch` and then `/plugin install exuarch@exuarch`. Other agents can use the skill on its own, `plugins/exuarch/skills/exuarch/SKILL.md`.

Edit the file, then **Import…** it in the app to see it run.

## Building and running

You need the .NET 10 SDK (see `global.json`).

```sh
dotnet run --project Exuarch.Web     # the app, on the URL it prints
dotnet test Exuarch.Core.Tests       # the tests
dotnet test Exuarch.Web.Tests
dotnet test Exuarch.Cli.Tests
```

Release builds, `dotnet publish Exuarch.Web -c Release`, compile the app ahead of time to WebAssembly, so the simulator runs about four times as fast as it would in .NET's interpreter. That takes the WebAssembly build tools, once: `dotnet workload install wasm-tools`. `dotnet run` builds Debug and does without them.

## The code

- `Exuarch.Core`: the simulator. Devices and their metadata (`DeviceRegistry.cs`), the machine and its two-phase clock (`Machine.cs`, `Clocking.cs`), the decoder ROM, the microcode validator, the assembler and editor support, the built in packages (`Packages/`) and the handbook pages (`Guides/`).
- `Exuarch.Web`: the Blazor WebAssembly app.
- `Exuarch.Cli`: the `exuarch` command line tool, packed as a .NET tool and pushed to NuGet with each release.
- `plugins/exuarch` and `.claude-plugin/marketplace.json`: the Claude Code plugin and the marketplace it installs from. release-please keeps the plugin's version in step with the app's.
- `Exuarch.Core.Tests`: the tests, including checks that every handbook link leads somewhere, that each tutorial's machine is built and runs as the text says, and that every example program assembles and runs. `Exuarch.Web.Tests` tests the app's view models, workspace and editors, and `Exuarch.Cli.Tests` the command line tool.

## Commits, releases and deployment

Commits follow [Conventional Commits](https://www.conventionalcommits.org): `feat: add a pipelined machine`, `fix(run): keep the clock panel still`, `docs: …`, `refactor: …`, `test: …`, `ci: …`, `chore: …`, with `!` or a `BREAKING CHANGE:` footer for a breaking change. Pull requests are squash merged with their title as the commit message, so the title is what counts, and a check on every pull request verifies that it is conventional.

[release-please](https://github.com/googleapis/release-please) (`.github/workflows/release.yml`) reads those commits on `main` and keeps a release pull request open with the next version and the new entries in `CHANGELOG.md`: a `feat` raises the minor version, a `fix` the patch, a breaking change the major. Merging the release pull request tags the release, `v1.2.3`, and creates the GitHub release.

Production, [www.exuarch.com](https://www.exuarch.com) on Azure Static Web Apps, is only deployed from a release tag (`.github/workflows/deploy.yml`): straight after release-please tags one, when a `v*` tag is pushed, or by hand for an existing tag. The app's footer shows the release it runs. Each pull request still gets a preview site of its own (`.github/workflows/pr.yml`), linked from the pull request and removed when it closes.

The workflows pin every action to a commit, and [Dependabot](https://docs.github.com/code-security/dependabot) (`.github/dependabot.yml`) opens a pull request each week to move the pins, and the NuGet packages, to their latest releases.

## History

- **Before 2019: the .NET Framework original.** The simulator started as a .NET Framework program.
- **2019: [BYOCCore](https://github.com/olebru/BYOCCore).** The first public commit, on 3 September 2019, put it on GitHub as an "8Bit MCU Designer": registers, an ALU, RAM, ROM, an MMU, a decoder ROM and an assembler, wired together in a class called `LowLevelPileOfPartsActingAsAMCU`. Ten days later it moved to .NET Core 2.2, and in September 2021 to .NET 5. That repository is now a public archive.
- **2021: BYOC.** This repository started on 6 September 2021, with the BYOCCore engine, a console UI, and a first Blazor WebAssembly UI deployed to Azure Static Web Apps. It ran a single machine and showed its registers and memory. Then it was left untouched for five years.
- **2026: ExµArch.** At the end of September 2026 it came back. It moved to .NET 10, and the machine became 16 bits throughout and is described as data, buses and microcode included. It gained a drag and drop hardware editor, a microcode editor, an assembly editor, the run view, packages and the handbook. Then came the example machines, from a one register CPU to a small 3D GPU. On 29 September the project was renamed ExµArch, and the site moved to [www.exuarch.com](https://www.exuarch.com).
