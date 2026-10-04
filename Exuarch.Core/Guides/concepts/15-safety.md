# Running other people's machines

Sharing is half the fun: a machine someone else designed, a program that pushes a machine further than you thought it could go. Before you import a package from a forum, a chat or an email, it is fair to ask what it could do to your computer. The short answer is nothing: a package is data, and the only computer that runs it is the one inside the simulator. This page explains why, what a package can still do, and where the care that is left is yours.

## A package is data, not code

A [package](exuarch:guide/packages) is one JSON file: a machine definition, microcode, assembly programs and a note. None of it is code your computer runs. The simulator reads the machine definition and builds the machine out of its own devices, then steps through the microcode one tick at a time. The CPU that runs an imported program is the simulated one, made of registers and buses, not the processor in your computer.

That is the same idea as a *sandbox*, the way browsers, virtual machines and emulators keep untrusted code away from the real machine: the program can only touch what the sandbox gives it. Here that is very little:

- **Only the built in devices.** A machine is put together from ExµArch's own [devices](exuarch:guide/devices-and-control-lines), with their sizes and settings checked when the package is read. A package can not bring a device of its own, and no memory can be larger than 65536 cells.
- **Nothing outside the simulator.** No device reads or writes your files, reaches the network or talks to the browser. What comes in is what you give the machine: the keys you press on its keypad while the Run view has it, and the time, for the [`rtc`](exuarch:reference/rtc). What goes out is its screens, LCD and lights, drawn inside the page.
- **Nothing runs by itself.** Importing a package opens it; its programs run only when you press **Run**, **Tick** or **Instruction**.

## What an imported machine can do

The worst a machine or program can do is waste your time:

- **Run forever, or very slowly.** A loop with no end is a normal program here. **Pause**, or the space bar, stops any run. Reloading the page always gets you back.
- **Show you things.** Its screens can show any picture, including text pretending to be a message from ExµArch. It is only pixels on a simulated screen.
- **Take your keypad keys.** While its keypad has the keyboard, the arrow keys and space go to the machine; **Escape** gives them back.
- **Replace a package you have.** An import with the name of a package you already have replaces it, after asking. One with the name of a built in machine becomes your changes to it, and **Reset** brings the original back. Export anything you care about before you import over it.

## The note, and the editor's hints

The note that comes with a package is Markdown, shown with some limits, whoever wrote it:

- **No HTML.** HTML in a note is shown as text, so a note can not add scripts, forms or anything else to the page.
- **Only plain links.** A link can go to a web page (`http`, `https` or `mailto`), to a place in the note, or into ExµArch (`exuarch:`). Anything else is not a link. Web links open in a new tab.
- **Images are links.** An image in a note is shown as a link to it, not loaded. Loading it would tell the server it is on that you opened the package, and when.

The assembly editor shows each instruction's description from the machine when you point at it. Links in those hints can go to the web or into ExµArch, and can not make the editor do anything else.

Behind all of this, the page has a *Content Security Policy*: a list the browser enforces of where scripts may come from. Only the site's own scripts and its anonymous statistics may run, so even a mistake in how ExµArch shows a package could not run code that came with it.

## Where the care is yours

The simulator can keep a package in its box. It can not stop a person from asking you to step out of it:

- **ExµArch packages are `.json` files.** **Import…** reads nothing else, and a machine never needs anything more. Nothing about ExµArch needs you to install a program, add a browser extension, run a script or paste something into the browser's developer tools. Anyone who asks you to is asking for something else.
- **Links in a note go wherever their author chose.** Treat them as you would any link from a stranger, and be wary of pages that ask you to sign in or download something.
- **Statistics never carry your machines.** The anonymous usage statistics name the built in machines you use; a machine of your own, imported or made here, is only ever counted as "own". Its name, its programs and its note stay in your browser.

## See also

- [Packages](exuarch:guide/packages): what is in the file, and how Import and Export work
- [Devices and control lines](exuarch:guide/devices-and-control-lines)
- [ExµArch and real hardware](exuarch:guide/real-hardware)
