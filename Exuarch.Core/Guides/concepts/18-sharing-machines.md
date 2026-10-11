# Sharing a machine with the community

Machines other people made are in [exuarch-machines](https://github.com/olebru/exuarch-machines), a public repository on GitHub. Every machine listed there is shown on the **Community** tab of the gallery, where anyone can open its details and **Add to my machines**. To share a machine of your own, you add its [package](exuarch:guide/packages) file to that repository with a pull request. You need a GitHub account.

## Get the machine ready

1. Write its note. Open **This machine** in the getting started drawer and say what the machine is, what is special about it, and what to try first. [The note](exuarch:guide/packages) can link to its devices, instructions and programs.
2. Give it programs that show what it does, and a name of its own. A name of a built in machine, such as TINY-16, is rejected.
3. Press **Export** in the header. You get `<name>.json`, the whole package: the hardware, the microcode, the programs and the note.
4. If you like, take a picture for the gallery tile: a PNG of 320 by 240 pixels, at most 1 MB.

## Open the pull request

1. Fork [exuarch-machines](https://github.com/olebru/exuarch-machines) on GitHub.
2. Put the file in `machines/`, named in lower case without spaces, such as `machines/zed8.json`, and the picture next to it, such as `machines/zed8.png`.
3. Add an entry for the machine to `index.json`:

```json
{
  "machines": [
    {
      "name": "ZED-8",
      "author": "your name",
      "tagline": "An eight bit machine in a day",
      "description": "What it is and what to try.",
      "tags": ["8 bit", "lcd"],
      "minVersion": "1.42.0",
      "file": "machines/zed8.json",
      "image": "machines/zed8.png"
    }
  ]
}
```

4. Open a pull request.

- `name` must be the `name` in the package file, and no other machine in the collection may use it.
- `author` is shown on the machine's tile.
- `tagline` is the few words next to the name, at most 50 characters.
- `description` is shown when someone opens the machine's details.
- `tags` are words search finds the machine by. Tag it `graphics` if it draws on the screen.
- `minVersion` is the oldest ExµArch the machine works in: use the version shown at the bottom of the page you built it in. An older ExµArch shows the machine but will not load it.
- `file` and `image` are paths inside the repository. `image` is optional.

A check runs on every pull request. It fails when `index.json` is not valid JSON, when a file is missing, when a package file is not an exported package or has another name than its entry, or when a file is too large: 512 KB for the index, 4 MB for a package and 1 MB for a picture. Once the pull request is merged, the machine is on the **Community** tab for everyone.

## License and safety

Machines in the collection are under the MIT license, the same as ExµArch, unless their note says otherwise. Opening a pull request shares your machine that way.

A community machine is a package like any other: data, not code. [Running other people's machines](exuarch:guide/safety) explains what a package can and can not do, for the machines you add from the collection as much as for the one you share.

## See also

- [Packages](exuarch:guide/packages): what is in the file you share, and the note
- [Running other people's machines](exuarch:guide/safety)
- [Working with AI and the command line](exuarch:guide/ai-and-the-command-line): check a package with the `exuarch` tool before you share it
