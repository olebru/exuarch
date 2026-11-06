# Saving to GitHub

Your machines are kept in this browser's own storage. That is quick and private, but it is only one browser on one computer: clearing the site's data removes them, and another computer does not have them. **Export** saves a copy as a file whenever you think of it. Saving to GitHub does it on every save: connect a repository of your own once, and each time you save a machine, ExµArch also writes it there as a package file. Connect the same repository in another browser, and the machines come with it.

It is optional. Without it, nothing changes and nothing leaves your browser.

## What you need

- A GitHub account.
- A repository for your machines. An empty one is fine, and it can be private.
- A *fine-grained personal access token* for that repository: a password for one repository only, which you create on GitHub.

To create the token:

1. Open [new fine-grained token](https://github.com/settings/personal-access-tokens/new) on GitHub.
2. Give it a name, such as `ExµArch`, and an expiry date.
3. Under **Repository access**, choose **Only select repositories** and pick the repository for your machines.
4. Under **Permissions**, set **Contents** to **Read and write**. Nothing else is needed.
5. Create it and copy the token. GitHub shows it only once.

## Connecting

1. Open the package menu, the ▾ next to the package name, and choose **Connect a GitHub repository…**.
2. Enter the repository as `owner/name`, for example `octocat/my-machines`, and paste the token.
3. Press **Connect**.

ExµArch checks that the token can reach the repository, and then compares the machines in its `machines/` folder with the ones saved in this browser:

- A machine only in the repository is added to this browser.
- A machine only in this browser is written to the repository.
- A machine in both that differs is left as it is, in both places, and **Open from GitHub…** opens so you can choose which to keep. A machine with unsaved changes in this browser counts as differing, so connecting never throws those changes away.

A note shows how many machines went each way. The menu now shows **GitHub: owner/name…**.

## What is written

Each machine is one file, `machines/<name>.json`, the same package file **Export** writes and **Import…** opens. A character a file name can not contain, such as `/` or `:`, becomes `-`.

- **When:** every **Save**, every **Save as…**, a new machine from **New…**, and a machine from **Import…** or **Add to my machines**.
- **What:** only saved versions. Unsaved changes stay in the browser until you save them.
- **How:** each write is a commit to the repository's default branch, named after the machine. A file that has not changed is not written again, so saving twice makes one commit.
- **Not deleted:** deleting a machine in **Machines** removes it from this browser only. Its file stays in the repository.

When a write fails, for example without a network connection, the machine is still saved in the browser and a warning shows why it did not reach GitHub. Once the problem is solved, press Ctrl+S (⌘S on a Mac): it writes the machine again even when there are no new changes.

## Open from GitHub

**Open from GitHub…**, in the package menu, reads the repository's `machines/` folder again and lists each machine with how it compares with this browser:

- **same as here:** **Open** loads it.
- **only on GitHub**, for example one you saved in another browser after this one connected: **Add and open** adds it to this browser and loads it.
- **differs from this browser:** **Use GitHub's** replaces the version saved in this browser, and any unsaved changes to it. **Keep this browser's** writes the browser's saved version to the repository.
- **can not be read:** the file is not a package, and the reason is shown under it.

The repository is read only when you connect and when you open this list; nothing changes in this browser in the background.

The repository is also a history: GitHub keeps every version you saved. To go back to an older one, download it from the file's history on GitHub and use **Import…**.

Because the files are ordinary package files, anything that works with an exported machine works with them: [working with AI and the command line](exuarch:guide/ai-and-the-command-line) shows how to check and edit one outside the app.

## The token

- **Where it is kept:** in this browser's storage for this site, next to your machines. It is sent only to `api.github.com`, when you connect and when a machine is saved.
- **What it can do:** only what you gave it: read and write the contents of that one repository. It can not reach your other repositories or your account.
- **When it expires:** saves report that GitHub rejected the token. Create a new one, choose **GitHub: owner/name…**, **Disconnect**, and connect again.
- **Disconnecting:** **GitHub: owner/name…** in the package menu, then **Disconnect**, removes the token from this browser. The files stay in the repository. **Reset everything** removes it too. To make sure a token is never used again, delete it on GitHub as well.
- **Shared computers:** anyone who uses this browser can save through the connection. Disconnect when you are done.

Never paste the token anywhere else. The only place ExµArch takes it is **Connect a GitHub repository…**: anyone who asks for it in a machine's note, a message or on a web page is asking for something else. See [running other people's machines](exuarch:guide/safety).

## See also

- [Packages](exuarch:guide/packages): what is in the file, and how Save, Import and Export work
- [Sharing a machine with the community](exuarch:guide/sharing-machines)
- [Working with AI and the command line](exuarch:guide/ai-and-the-command-line)
