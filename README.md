[English](README.md) | [简体中文](README.zh-CN.md)

# 1Remote Plus

[![version](https://img.shields.io/github/v/release/chaogei666661/1Remote-plus?color=Green&include_prereleases&sort=semver)](https://github.com/chaogei666661/1Remote-plus/releases)
[![issues](https://img.shields.io/github/issues/chaogei666661/1Remote-plus)](https://github.com/chaogei666661/1Remote-plus/issues)
[![license](https://img.shields.io/github/license/chaogei666661/1Remote-plus?color=blue)](https://github.com/chaogei666661/1Remote-plus/blob/main/LICENSE)
![build](https://github.com/chaogei666661/1Remote-plus/actions/workflows/build-on-dev-push.yml/badge.svg)

1Remote Plus is a personal remote session manager and launcher for Windows. It keeps every machine you
connect to — RDP, SSH, VNC, Telnet, SFTP, FTP, serial, RemoteApp — in one searchable list, and opens them in
tabs of a single window or in windows of their own.

> **This is a modified version.**
> This repository, [`chaogei666661/1Remote-plus`](https://github.com/chaogei666661/1Remote-plus), is a fork of
> [`chaogei/1Remote-Plus`](https://github.com/chaogei/1Remote-Plus) — which is itself a fork of
> [1Remote/1Remote](https://github.com/1Remote/1Remote) by Shawn Veck. It is not affiliated with, nor endorsed
> by, either of them. Please report problems with **this** build at
> [chaogei666661/1Remote-plus/issues](https://github.com/chaogei666661/1Remote-plus/issues); behaviour that
> also exists in `chaogei/1Remote-Plus` is better reported there.
>
> This fork builds and publishes its own releases, and the in-app update check reads this repository's
> releases page rather than either parent's.

On disk the product keeps its old name: the executable is `1Remote.exe`, the settings and database files are
`1Remote.json` and `1Remote.db`, and the `AppData` folder is `1Remote`. That is deliberate, so an existing
installation keeps its data, its autostart entry and its saved credentials when it is updated.

---

## Contents

- [What is different in this fork](#what-is-different-in-this-fork)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Download and install](#download-and-install)
- [First run](#first-run)
- [Add your first connection](#add-your-first-connection)
- [The main window](#the-main-window)
- [The launcher](#the-launcher)
- [Sessions, tabs and windows](#sessions-tabs-and-windows)
- [Connection audit log](#connection-audit-log)
- [Diagnostics bundle](#diagnostics-bundle)
- [What each server entry can do](#what-each-server-entry-can-do)
- [Protocols](#protocols)
- [Credentials and secrets](#credentials-and-secrets)
- [Security notes](#security-notes)
- [Proxies, jump hosts and port forwarding](#proxies-jump-hosts-and-port-forwarding)
- [Where your data lives](#where-your-data-lives)
- [Backup and restore](#backup-and-restore)
- [Import and export](#import-and-export)
- [Appearance: themes, frosted glass, language](#appearance-themes-frosted-glass-language)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Updates](#updates)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Contributing](#contributing)
- [License](#license)
- [Credits](#credits)
- [Further reading](#further-reading)

---

## What is different in this fork

This fork branched off upstream in August 2026. Everything below is new here; upstream does not have it.

**Fixes**

- **Proxy actually works.** In the version this forked from, a configured proxy silently built no tunnel at
  all. Proxy handling was rewritten around a local relay, and the session now keeps its real target address
  instead of being rewritten to the local tunnel endpoint.
- **Checked-arithmetic overflows** that could crash icon loading, file transfers, credential prompts and
  desktop-shortcut creation.
- **UI freezes.** Credential reads and writes, SSH port-forward setup, SFTP transfers, periodic data-source
  reloads and server-list rebuilds were moved off the UI thread, so a slow network no longer locks the
  window and every hosted session inside it.
- **A file transfer no longer loses files, or stalls, while it counts them.** Before starting, a transfer
  checked each file against every file already queued using a *linguistic* comparison. That reads two names
  the way a person reads two words, so it decided `file.txt` and `ﬁle.txt` (with the fi ligature) were the
  same file, along with names differing only by a zero-width space, a soft hyphen, or whether an accent is
  written precomposed or as a combining mark — the last of which is routine, because macOS writes names the
  decomposed way. The extra file was dropped from the transfer without appearing anywhere. The check was
  also quadratic: a 20 000-file folder spent about 10 seconds, and a 50 000-file one about a minute, showing
  *Scanning* before a byte moved. Both now take milliseconds, and paths that differ only in case are still
  treated as one file.

**New features**

- **Real proxy support for every protocol** — SOCKS4, SOCKS4a, SOCKS5 and HTTP CONNECT, plus **SSH jump
  hosts** (the equivalent of OpenSSH `ProxyJump`). Because it works through a local relay, RDP and VNC are
  covered too, not just the terminal protocols.
- **Standing port forwards** — local, remote and dynamic (SOCKS) SSH tunnels that run independently of any
  session and can start with the app.
- **Backup and restore**, plus optional upload of the same archive to a **WebDAV** folder.
- **Host identity verification**, on by default: RDP certificates, SSH host keys on SFTP and FTPS
  certificates are checked against a remembered fingerprint, with an explicit per-server opt-out.
- **Passwords from a password manager** at connect time, via a `cmd://` reference to any CLI.
- **Connection quality indicator** — an optional timer that grades each visible server on round-trip time,
  jitter and unanswered checks, not just up or down.
- **Wake on LAN** from a server's action menu.
- **Import from `~/.ssh/config`**, including its `ProxyJump` directives.
- **Send command** — type one command into several open terminal sessions at once, with saved snippets.
- **Terminal session recording** to a log file.
- Wider search, and sorting by most recently connected.

**Appearance**

- A translucent **acrylic / frosted-glass** design applied across the app, with a panel-opacity slider.
- Font-glyph icons replaced with vector paths, so they stay sharp at any DPI.
- Eight new colour themes tuned for the frosted look: Mica Slate, Nord Frost, Tokyo Night, Catppuccin Mocha,
  Emerald Glass, Cyber Neon, Rose Pine and macOS Light Glass, alongside the classic ones.

**Packaging**

- Releases are built and published from this repository, and the in-app update check reads this fork's
  releases rather than upstream's.

**Hardening added in `chaogei666661/1Remote-plus`**

- A build carrying the **placeholder encryption salt** now says so at launch and on the About page, instead
  of quietly enciphering real passwords under a publicly known key.
- **`cmd://` external secrets need a per-machine approval** before the command is ever run, so a tampered
  database or an imported profile cannot turn a password field into code execution.
- **WebDAV backups refuse plain HTTP** unless it is explicitly enabled, with a warning next to the switch.
- Generated **`.rdp` files and private-key copies are staged per session** and removed with the session.
- The vault token is **written before it is returned**, and the write no longer blocks the UI thread.
- **Cloning a server no longer shares its alternative credentials or arguments** with the original.
- A **shutdown watchdog** names what is still running before the failsafe kills the process.
- The **test project builds and runs in CI**, on pull requests as well as pushes, with Dependabot watching
  the actions and the NuGet feed. Backup restore (including Zip-Slip attempts), host trust, the stored-secret
  round trip, protocol cloning and the update check are covered by tests.

## Screenshots

The images below come from the upstream documentation site. They show what the app does; they predate this
fork's UI rework, so the chrome looks different from a current build.

<img src="https://1remote.github.io/img/home_override/hero1.png" width="800" />

<p align="center">
    <img src="https://1remote.github.io/img/home_override/protocols.png" width="400" />
</p>
<p align="center">
    <img src="https://1remote.github.io/img/home_override/hero2.gif" width="400"/>
</p>
<p align="center">
    ↑ The launcher opening an RDP session, and resizing it
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/multi-screen.jpg" width="500"/>
</p>
<p align="center">
    ↑ RDP across multiple monitors
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/RemoteApp/demo.jpg" width="800"/>
</p>
<p align="center">
    ↑ RemoteApp over RDP
</p>

## Requirements

- **Windows, 64-bit.** This is a WPF desktop application; there is no macOS or Linux build.
- **Windows 10 version 2004 (build 19041) or later**, or Windows 11. That is the Windows SDK the project
  targets.
- The frosted-glass backdrop needs **Windows 10 1803 or later**. Without it the app runs fine, just opaque.
- For the smaller of the two downloads, the **.NET 9 Desktop Runtime (x64)**. The self-contained download
  needs nothing installed.

## Download and install

There is no installer. Both downloads are a zip you unpack and run.

1. Open [Releases](https://github.com/chaogei666661/1Remote-plus/releases) and pick the newest build.
   The sidebar is not sorted by date — see [Updates](#updates) — so check the version number, not the
   position in the list.
2. Download one of the two assets:

   | Asset | Size | Needs |
   | --- | --- | --- |
   | `1Remote-<version>-net9-x64.zip` | smaller | .NET 9 Desktop Runtime (x64) installed |
   | `1Remote-<version>-net9-x64-self-contained.zip` | larger | nothing — the runtime is inside |

   If you are unsure, take the self-contained one.
3. Unpack the zip to a folder **you can write to** — for example `D:\Apps\1Remote` or a USB stick. Do not
   run it from inside the zip, and avoid `C:\Program Files` unless you intend to run as administrator.
4. Run `1Remote.exe`.

To update, close the app, unpack the new zip over the old folder, and start it again. Your profile, database
and icons are separate files and are not overwritten. To uninstall, delete the folder.

## First run

On first launch the app asks where to keep your data. Both options are offered on the same screen:

- **Portable mode** — everything is stored next to `1Remote.exe`. Pick this for a USB stick, or if you want
  the whole installation to be one folder you can copy.
- **Install for current Windows account** — everything is stored in your Windows `AppData` folder. Pick this
  if the program folder is read-only.

The choice is recorded as a marker file next to the executable, `FORCE_INTO_PORTABLE_MODE` or
`FORCE_INTO_APPDATA_MODE`. **Renaming one to the other switches modes** the next time the app starts; move
your data files yourself if you do that.

Your servers are stored in a local **SQLite** database, `1Remote.db`. It is created for you and needs no
setup. Note the warning the app shows on the Database settings page: the local database is **not encrypted**,
so protect the file the way you protect anything else with passwords in it. If you would rather keep your
list on a server, `Options → Database` can add a **MySQL** or **PostgreSQL** source and use it alongside the
local one.

## Add your first connection

1. Click the **+** button above the server list and choose **Add**. (The same menu holds the import
   entries.)
2. Choose a protocol — RDP, SSH, VNC, Telnet, SFTP, FTP, RemoteApp, Serial or an external app.
3. Fill in **Hostname** and **Port**. The port is prefilled with the protocol default.
4. Fill in **User** and **Password**, or:
   - leave the password blank to be asked each time you connect, or
   - pick a saved entry from the **Credentials Vault**, or
   - enter `cmd://<command>` to pull the password from a password manager at connect time (see
     [Credentials and secrets](#credentials-and-secrets)).
5. Optional, on the **Common** group:
   - **Tags** — type a name and press Enter. Tags are how the list is grouped and filtered.
   - **Icon** and **Note** — click the icon to replace it with your own image.
6. Optional, on the other groups, depending on protocol:
   - **Connection** — alternative addresses and accounts, tried in order when the primary is unavailable;
     a MAC address for Wake on LAN; the proxy this server should use.
   - **Display** (RDP) — window / single-monitor / multi-monitor full screen, resolution and scaling,
     performance level.
   - **Advanced Settings** (RDP) — clipboard, drives, printers, smart cards, sound, key combinations, RD Gateway.
   - **Advanced Settings** (SSH) — private key, startup command, port-forwarding rules, agent forwarding, X11.
   - **Script before connect / after disconnected** — a command line or `.bat` run around the session.
7. **Save**. Double-click the new entry to connect.

The very first time you reach a host, its identity is unknown and the app shows its fingerprint and asks
whether to remember it. Accepted fingerprints go into `.locality/known_hosts.json`; if one later changes you
are warned loudly, because that means either the server was rebuilt or someone is intercepting the
connection. A server you cannot fix — a lab box with a self-signed certificate, say — can opt out with **Do
not verify this host's identity** in its editor.

## The main window

**Views.** The main menu's **View** submenu switches between **List**, **Card** and **Tree**. Tree groups by
tag folders; List and Card are flat and can be grouped by a pinned tag.

**Search.** Press **Ctrl + F** to jump to the filter box, then type. The box matches names, addresses,
usernames and notes, and understands tag syntax:

| Typed | Meaning |
| --- | --- |
| `web` | anything matching "web" |
| `#prod` | only servers tagged `prod` |
| `#prod db` | tagged `prod` **and** matching "db" |
| `-#prod` | everything **except** servers tagged `prod` |

Extra keyword matchers — pinyin and initials, for instance — can be enabled under `Options → Launcher →
Keyword-Matchers`. They apply to this box as well as to the launcher.

**Tags.** Right-click a tag for its menu: **Pin** it to the header strip, **Rename**, **Connect** to
everything under it at once, or **Delete**. On a tag chip on a row, plain click sets it as the filter,
**Ctrl + click** adds it to the include list, **Alt + click** adds it to the exclude list.

**Sorting.** The main menu's **Sorting** submenu orders the list by Id (your own drag order), protocol, name,
address, or **Recently connected**. Protocol, name and address toggle between ascending and descending.

**Reachability and connection quality.** Turn on `Options → General → Show whether each server is reachable`
and the app opens a connection to each visible server's port on a timer (60 s by default). It uses whatever
proxy the server is configured with; servers behind an SSH jump host are not probed. It is off by default
because it is traffic to every configured host on a schedule.

The dot is graded rather than binary. Each sweep's round-trip time is kept for the last ten checks, and the
colour comes from the average, the jitter (the mean change between consecutive checks) and how many of the
ten went unanswered:

| Dot | Meaning |
| --- | --- |
| Green | Under 60 ms average, steady, nothing lost |
| Lime | Under 150 ms, or up to 50 ms of jitter |
| Amber | Under 300 ms, up to 100 ms of jitter, or 5–19% unanswered |
| Orange | 300 ms or more, 100 ms or more of jitter, or 20% or more unanswered |
| Red | The last check got no answer at all |

Hover the dot for the numbers behind the colour. Nothing extra goes on the wire for this: the grade is built
from the sweep that was already happening, not from a burst of probes.

**Tray.** Closing the window minimises to the tray by default (`Options → General → Close button behavior`).
The tray menu can list recently used sessions, reset the main window position, open the issue tracker, show
About and exit.

## The launcher

The launcher is the reason to keep this app running. Press its hotkey anywhere in Windows and a search box
appears over whatever you were doing.

- **Alt + M** — open it. Change the hotkey at `Options → Launcher`, or switch it off there entirely.
- Type to filter. The same `#tag` syntax as the main window works here.
- **↑ / ↓** — move the selection. **PageUp / PageDown** move it by five.
- **Enter** — connect to the selected server.
- **→** — open that server's action list (new window, alternative credentials, a specific runner, copy
  password, Wake on LAN, and the rest). **←** goes back.
- **Tab** — switch between the server list and **Quick Connect**.
- **Esc**, or clicking elsewhere — hide the launcher.

**Quick Connect** is the launcher's second mode: type an ad-hoc target such as `192.168.0.100:3389` and
connect without creating an entry first. Previous quick connections are listed underneath and can be picked
with **↑ / ↓**. Ticking **Remember the information above** turns one into a saved server; that option can be
disabled entirely at `Options → Launcher`.

`Options → Launcher` also controls whether notes and credentials are shown in the results, and which
**Keyword-Matchers** are active. For the hotkey to be there when you want it, turn on
`Options → General → Run automatically at OS startup (minimized)`.

## Sessions, tabs and windows

By default every session opens as a tab in one shared session window; drag a tab out to tear it into its own
window, or drop it back onto another window's tab strip to merge. `Options → General → Always open in new
window` changes the default, and any entry can be opened once in a new window from its action menu
(**Connect (New window)**).

Inside a session window:

- **Ctrl + 1 … Ctrl + 9** — jump to that tab.
- **F11**, or right-clicking the maximise button — put the *window* full screen. The toolbar's full-screen
  button instead moves the current session into a dedicated full-screen window, which is the mode
  multi-monitor RDP uses.
- **Middle-click a tab** — close it. The tab strip can also show a per-tab icon, reconnect and close button
  (`Options → General`).
- **Send command** — the toolbar button opens a dialog that types one command into every terminal session
  you tick. It works with SSH, Telnet and serial sessions; RDP and VNC cannot be typed into this way.
  Commands you use often can be saved as named snippets.
- A dropped RDP session shows a *Reconnecting…* counter and retries by itself; other protocols can show a
  **Reconnect** button on the tab.
- SFTP and FTP sessions get a file browser with **F5** refresh, **F2** rename, **Del** delete,
  **Ctrl + S** download and **Ctrl + V** upload from the clipboard.
- **Session recording** — `Options → General → Record terminal session output to a file` writes everything
  SSH, Telnet and serial sessions print into `.sessionlogs/` (or a folder you choose). Off by default: a
  session log holds whatever crossed the screen, which regularly includes output nobody meant to keep.
  Recordings are pruned on each launch by two limits you can set next to the folder — an age in days
  (30 by default) and a total size in MB (1024 by default). Either can be set to 0 to turn it off; the
  oldest go first, and only `*.log` in the top level of the folder is touched.
- **Connection failures are classified.** SSH, SFTP, FTP, VNC and the proxy relays used to print the raw
  library message into the error panel. They now say which of fifteen categories it was — the name does not
  resolve, nothing is listening on that port, the credentials were refused, the host identity changed — with
  the original message kept underneath, and only the categories where a retry could plausibly work offer one.

## Connection audit log

`Options → General → Connection audit` keeps a local record of which server was connected to, when, by which
account, through which proxy, and how it ended. It answers the question that comes up after an incident and
that a last-connect timestamp cannot: who reached that host, from which machine, and did it succeed.

- Written to `.locality/audit/connections-YYYY-MM-DD.jsonl`, one JSON object per line, one file per UTC day.
- Four events per attempt — started, opened, failed, closed — tied together by connection id, with the
  session length on the close.
- **No secrets.** The record holds the server, address, port, remote account, data source, proxy and
  outcome. It never holds a password, a private key or a `cmd://` command.
- Under `.locality`, so it does not travel with a synced or shared data source: it is a record of what
  happened on *this* machine.
- On by default, kept for 90 days. Both are settable; a retention of 0 keeps everything.
- **Export to CSV** for a review or a ticket. Fields beginning with `=`, `+`, `-` or `@` are prefixed with an
  apostrophe so a server name typed by somebody else cannot run as a formula when the file is opened.

## Diagnostics bundle

`Options → General → Diagnostics → Create a diagnostics bundle` writes a single zip to attach to a bug
report: the application log, an environment report (version, OS, runtime, locale), your settings and your
protocol runner definitions.

Every text file in it is scrubbed first. The value of any field whose name contains `password`, `passphrase`,
`secret`, `token`, `privatekey`, `credential` and similar is replaced with `[redacted]:<length>`, as are PEM
private key blocks, `cmd://` external secret commands and `-pw` / `--password` arguments. The server
database, the credential vault, host trust, command approvals, session recordings and the audit log are not
included at all, and the environment report names neither your account nor your machine.

Read it before you send it. Redaction is a filter over free text, not a proof — a password typed into a field
that is not named like one will still be in there.

## What each server entry can do

Right-click a row (or press **→** in the launcher):

| Action | Notes |
| --- | --- |
| Connect | Double-click does the same |
| Connect (New window) | Bypasses the shared tab window once |
| Connect (with alternative `<name>`) | Uses one of the entry's alternative accounts |
| Connect (via `<runner>`) | Uses PuTTY, KiTTY, WinSCP, FreeRDP … instead of the built-in client |
| Edit / Duplicate / Delete | Multi-select supported, including bulk editing |
| Copy hostname / username / password | Copying the password asks for Windows credential verification |
| Wake on LAN | Shown when the entry has a MAC address |
| Create desktop shortcut | A `.lnk` that opens this session directly |
| Open SFTP | On SSH entries, opens a file browser to the same host |
| Export \*.rdp | On RDP entries |

## Protocols

| Protocol | Client used by default | Notes |
| --- | --- | --- |
| **RDP** | Built-in `AxMsRdpClient`, or `mstsc.exe` mode | Multi-monitor, HiDPI, RD Gateway, drive/printer/clipboard redirection |
| **RemoteApp** | RDP | Publishes a single remote application instead of a desktop |
| **SSH** | PuTTY (bundled) | Private keys, startup command, port forwarding, agent and X11 forwarding |
| **Telnet** | PuTTY | |
| **Serial** | PuTTY | Local COM ports |
| **VNC** | Built-in VNC client | |
| **SFTP** | Built-in file browser | OpenSSH-format keys with the internal client |
| **FTP** | Built-in file browser | FTPS certificates take part in host verification |
| **App** | Any executable | Presets for Chrome, NoMachine, FreeRDP, PuTTY, Windows Terminal, WinSCP, UltraVNC, TightVNC |

Any protocol's client can be swapped at `Options → Protocol`: point a runner at your own executable, give it
a command-line template and environment variables, and choose whether its window is hosted inside a
1Remote Plus tab or left standalone. See the upstream [runner documentation](https://1remote.github.io/usage/protocol/runner/)
— it still applies here.

Each runner is checked as you edit it, and anything that will stop it working is listed at the top of its
panel: no program chosen, a program that is not at the path given, a `%LIKE_THIS%` placeholder that is not
one of the macros the protocol offers, and an empty private-key command line. That last one is the quiet
trap — it does not fall back to the normal command line, it replaces it, so a server that has a key
configured starts the program with no arguments at all. A mistyped macro used to produce no message
anywhere: runners are started without a shell, so `%1RM_HOSTNAM%` reaches PuTTY as those literal characters
and all the user sees is a client that opens and fails to connect.

## Credentials and secrets

**Credentials Vault** (`Options → Credentials Vault`) holds named username/password/private-key entries.
A server entry can reference one by name instead of storing its own copy, so rotating a password is one edit
rather than twenty. Each server can also carry **alternative** credentials, offered from its action menu.

**Ask every time.** Leave a password blank and you are prompted at connect time.

**From a password manager.** Enter `cmd://<command>` in a password field and the command is run when you
connect; whatever it prints on stdout is used as the secret. This works with any CLI:

```
cmd://bw get password my-server
cmd://keepassxc-cli show -a Password ~/vault.kdbx my-server
cmd://pass show servers/my-server
cmd://op read op://Private/my-server/password
```

The command runs through `cmd.exe`, so pipes and quoting behave as they do in a shell. A trailing newline is
stripped, the result is cached for the rest of the session (so a vault that prompts for a fingerprint only
asks once), and a 20-second timeout applies. There is a **Test** button next to the field that runs the
command without caching and reports how many characters came back.

**Viewing a stored password** — from the vault or the copy-password action — requires Windows credential
verification when `Options → General → Security` has it switched on.

**A caution.** The local database and the profile are not encrypted, and neither is a backup archive. Store
them accordingly. The section below says exactly what that means.

## Security notes

Read this before you point a build at a password store you care about.

**Builds without an encryption salt.** The stored-secret cipher is keyed by a compile-time constant that CI
substitutes from a repository secret. A fork has no access to the original secret, so a build produced
without one keeps the placeholder that is published in this repository — a key anybody can read. Such a
build says so on launch and on the About page. **It must not be pointed at a password store created by an
official release**: doing so re-encrypts real secrets under a known key. Build from source with your own
salt if the stored passwords need to be protected.

**What `1Remote.db` protects against.** Casual inspection, and nothing stronger. The class doing the work is
called `UnSafeStringEncipher` for a reason: it is obfuscation with a key compiled into the binary, not
per-user encryption. Anyone holding both the database file and a copy of the program can recover every
password in it. Keep the database where you would keep the passwords themselves.

**What Windows Hello gates.** The second-factor prompt guards actions inside the running app — revealing or
editing a stored credential, for example. It is not a key: the database is enciphered the same way whether
Hello is enabled or not, and turning Hello on does not make an exfiltrated database any harder to read.

**`cmd://` external secrets are a shell-out.** A password field may hold `cmd://<command line>`, which is run
through `cmd.exe` at connect time and whose output becomes the secret. That makes any writable data source —
a shared SQLite file, a compromised MySQL/PostgreSQL source, a restored backup, an imported mRemoteNG file —
a potential code-execution vector. Each distinct command has to be approved once on this machine before it
will ever run, and approvals are stored locally in `.locality/known_commands.json`; they never travel with
the database. Pre/post-connect scripts are the same class of feature and carry the same caveat.

**WebDAV backups require HTTPS.** The archive contains the whole configuration, the credential database
included, and the client sends Basic authentication pre-emptively. Plain `http://` is refused unless it is
explicitly enabled in the backup settings, which is only ever reasonable on a loopback or lab endpoint.

**SFTP and FTPS verify host identity** on first use and refuse silently changed identities; accepted
fingerprints live in `.locality/known_hosts.json`.

**Downloads stay in the folder you chose.** A recursive download builds every local path out of names the
server supplied, and neither protocol stops a server from answering a listing with `..\..\Startup\x.exe` or
`C:\Windows\System32\evil.dll`. Names like those are now re-rooted under the destination or, where that is
impossible, refused with the offending name quoted and the whole transfer stopped before anything is
written. The same check runs on double-click preview, which downloads to the temp folder and then opens the
file with its associated program — and preview now only opens a transfer that actually finished.

**Remote file names are shown as they really are.** A name can be made to render as something other than
itself: an entry named `invoice⁠<U+202E>gnp.exe` is drawn by any conforming text stack as `invoiceexe.png`,
and double-clicking it in the browser would have started a program. The file browser spells out invisible
formatting characters — bidirectional overrides, zero-width joiners, control characters — instead of obeying
them, and previewing such a file asks first, quoting the real name and the extension that will actually
decide what runs. Ordinary names, accents and CJK included, are shown unchanged.

**Uploads stop at a folder link.** Uploading a folder used to walk into every subfolder the file system
listed, junctions and symbolic links included. A link pointing back at a parent turned the scan into a walk
that kept re-entering the same tree at a longer path each time until the platform gave out, and the upload
then quietly did nothing; a link pointing anywhere else — `AppData`, a mapped drive, all of `C:\` — sent
whatever was behind it to the remote server along with the folder you actually picked. A linked folder is now
created empty on the far side and not descended into, and the transfer panel names the first few it stopped
at and counts the rest — the status line is one row high, and an ordinary Windows profile carries a dozen
compatibility junctions, so the full list used to fill it with the part you could already guess.
Linked *files* are still uploaded: reading through one is what copying that file means. Uploading a whole
drive works too, and lands in a folder named after its letter; it used to fail silently.

**Temporary files.** Generated `.rdp` files and private-key copies are staged in a per-session directory that
is removed when the session ends, rather than in the shared temp folder.

**SSH transport algorithms.** SFTP sessions, SSH jump hosts and standing port forwards all negotiate through
the bundled SSH.NET library. It offers AES-GCM and ChaCha20-Poly1305, encrypt-then-MAC integrity — which is
what lets OpenSSH's strict key exchange engage, the mitigation for the Terrapin attack (CVE-2023-48795) —
and the `mlkem768x25519` / `sntrup761x25519` post-quantum key exchanges. Algorithms OpenSSH turned off years
ago are not offered at all: arcfour, blowfish, CAST, Twofish, the MD5 and RIPEMD-160 MACs, the truncated
`-96` MACs, and `ssh-dss` host keys. A device old enough to require one of those will refuse to negotiate;
reach it with PuTTY, which this app can launch as an external SSH runner.

## Proxies, jump hosts and port forwarding

Define proxies once at `Options → Proxy`, then pick one per server in the server editor. Everything goes
through a local relay, so **all protocols are covered, RDP and VNC included** — not just the ones with
native proxy support.

| Type | Notes |
| --- | --- |
| SOCKS5 | IPv4, IPv6, remote name resolution, username/password auth |
| SOCKS4 | Names are resolved locally; the proxy only ever sees an IPv4 address |
| SOCKS4a | SOCKS4 with remote name resolution |
| HTTP CONNECT | Optional Basic proxy authentication |
| SSH jump host | The session travels inside a channel the SSH server opens to the target, like OpenSSH `ProxyJump`. Private key first, then password. |

Each proxy has a **Test** button: give it a `host:port` target and it reports whether the proxy reached it.
**Connect directly when the address is this machine** skips the proxy for local addresses. If a proxy a
server points at has been deleted or is incomplete, connecting offers to fall back to a direct connection
rather than failing silently.

**Port forwarding** (`Options → Port forwarding`) runs standing SSH tunnels that are independent of any
session. Each one points at an **SSH jump host** entry from the Proxy page — define the bastion once and
both features use it, sharing a single login.

| Direction | What it does |
| --- | --- |
| Local | Listens here, sends to `destination:port` through the SSH host |
| Remote | Listens on the SSH host, sends back to a destination reachable from here |
| Dynamic | A SOCKS proxy on a local port, no fixed destination |

Forwards can **start automatically when the app launches**. Binding to `0.0.0.0` instead of `127.0.0.1`
publishes the tunnel to your whole network, and the editor says so.

Per-session SSH forwards also exist, in an SSH entry's **Advanced Settings** group: one rule per line, either
`L 8080 intranet:80` / `R 9000 localhost:9000` / `D 1080`, or PuTTY's own `L8080=intranet:80` syntax.

## Where your data lives

Relative to the folder chosen at first run — next to `1Remote.exe` in portable mode, or your `AppData`
folder otherwise.

| Path | Contents |
| --- | --- |
| `1Remote.json` | All settings: proxies, port forwards, theme, launcher hotkey, WebDAV destination, saved commands |
| `1Remote.db` | The local SQLite database: servers, tags, credentials vault |
| `1Remote.dataSources.json` | Additional MySQL / PostgreSQL sources |
| `Protocols/` | Custom protocol runners |
| `.locality/` | Window positions, connection history, `known_hosts.json` |
| `.locality/audit/` | Connection audit log, one `.jsonl` per UTC day |
| `.icons/` | Server icons |
| `.logs/1Remote.log.md` | Application log |
| `.sessionlogs/` | Recorded terminal output, when that is enabled |
| `PuTTY/`, `KiTTY/` | Settings for those runners |

Only one instance runs at a time; starting a second one activates the first.

## Backup and restore

`Options → Backup` packs everything in the table above — servers, credentials vault, all settings, tags,
connection history, custom runners and icons — into a single archive.

- **Create a backup** writes the archive wherever you choose.
- **Restore from a backup** replaces your current data with the archive's, then closes the app; start it
  again to use the restored data. This cannot be undone.
- **Off-machine copy (WebDAV)** uploads the same archive to a WebDAV folder — Nextcloud, ownCloud, Synology
  and most NAS boxes speak it. Give it a folder URL such as
  `https://cloud.example.com/remote.php/dav/files/me/1Remote/` plus a username and password, then use
  **Back up and upload**, **Refresh list** and **Download and restore**.

Servers kept in a remote MySQL or PostgreSQL source stay in that database; the backup only records the
details needed to reach it.

The archive contains your configuration **unencrypted**. Use an HTTPS destination and a folder only you can
read.

## Import and export

From the **+** menu above the server list:

- **Import json** — a list exported from 1Remote Plus, or from upstream 1Remote.
- **Import mRemoteNG csv** — see the upstream
  [notes on mRemoteNG](https://1remote.github.io/usage/overview/#importing-from-mremoteng).
- **Import from `~/.ssh/config`** — reads your OpenSSH config; any `ProxyJump` directives are turned into
  SSH jump host entries on the Proxy page automatically. `Include` is followed (globs, `~`, and paths
  relative to `~/.ssh`, up to 16 levels deep), so a config split across `~/.ssh/config.d/*` imports whole.
  Pattern blocks such as `Host *` or `Host *.internal` are applied as the defaults ssh treats them as
  rather than skipped, with ssh's own "first value wins across the file" ordering; `Match` sections are
  read when their criteria are `all`, `host` or `originalhost`, and skipped whole when they are anything
  a program filling in an import dialog cannot answer (`exec`, `user`, `localnetwork`, `tagged`, …).
- **Import \*.rdp** — a Remote Desktop file.
- **Import PRemoteM db** — a database from the app's earlier name.

**Export** writes the currently selected servers to JSON. Individual RDP entries can also be exported as
`.rdp` from their action menu.

## Appearance: themes, frosted glass, language

`Options → Theme` picks a palette, a font family and a font size (10–20).

Frosted-glass palettes: **Dark**, Mica Slate, Nord Frost, Tokyo Night, Catppuccin Mocha, Emerald Glass,
Cyber Neon, Rose Pine, macOS Light Glass.
Classic palettes: Light, PRemoteM, SecretKey, Greystone, Asphalt, Wine, Forest, Soil and more. Any of them
can be edited colour by colour.

**Frosted glass** has its own switch — *Blur the desktop behind panels (Windows 10 1803 and later)* — and a
**Panel opacity** slider. Below roughly 120 the text starts losing contrast against a busy desktop; above
roughly 230 the blur stops being visible.

Two honest caveats about the frost. It turns itself off in a **remote desktop session** and under **high
contrast**, because DWM would sample the remote framebuffer instead of the real desktop and the panels would
wash out; the app snaps to opaque surfaces instead, which stay readable. And the **session window, the
full-screen session window and the crash reporter stay opaque by design** — the first two embed a
remote-desktop HWND that DWM blur fogs over, and the third draws a transparent halo that would frost into a
square bloom.

**Language.** `Options → General → Language` ships English, 简体中文, 繁體中文, 日本語, Deutsch, Français,
Español (AR), Galego, Italiano, Polski, Português (BR/PT), Русский and Čeština.

## Keyboard shortcuts

**Anywhere in Windows**

| Key | Action |
| --- | --- |
| `Alt + M` | Open the launcher (configurable at `Options → Launcher`) |

**Launcher**

| Key | Action |
| --- | --- |
| `↑` / `↓` | Move the selection |
| `PageUp` / `PageDown` | Move by five |
| `Enter` | Connect |
| `→` | Open the selected server's action list |
| `←` | Back, or show the note field |
| `Tab` | Switch between the server list and Quick Connect |
| `Esc` | Hide the launcher |

**Main window**

| Key | Action |
| --- | --- |
| `Ctrl + F` | Focus the search box |

**Session window**

| Key | Action |
| --- | --- |
| `Ctrl + 1` … `Ctrl + 9` | Switch to that tab |
| `F11` | Toggle full screen |

**SFTP / FTP browser**

| Key | Action |
| --- | --- |
| `F5` | Refresh |
| `F2` | Rename |
| `Del` | Delete |
| `Backspace` | Parent folder |
| `Ctrl + S` | Download to… |
| `Ctrl + V` | Upload from the clipboard |

## Updates

The app checks for a new version once an hour against **this fork's** releases page,
`https://github.com/chaogei666661/1Remote-plus/releases`. It does not look at upstream. Turn it off at
`Options → General → Do not check for new version`. There is no auto-updater; the check just tells you a
newer build exists and links to it.

**A quirk worth knowing when you download manually.** GitHub orders the releases sidebar by tag name as a
*string*, so `v1.3.0.12-beta` sorts between `v1.3.0.1-beta` and `v1.3.0.2-beta` rather than at the top. The
newest build is not necessarily the first one listed. Sort by publication date, or read the version numbers
and take the highest. (The in-app check already compares numerically, so it is not fooled by this. The
version badge at the top of this page uses `sort=semver` for the same reason.)

## Troubleshooting

**A connection fails only when a proxy is set.** Open `Options → Proxy`, select the entry, put the target in
**Test target** as `host:port`, and press **Test**. It reports whether the proxy itself reached the target.

**"Unverified host" on every connect.** The fingerprint is not being remembered — usually because the app
cannot write `.locality/known_hosts.json`. Check that the data folder is writable.

**The window has no frosted background.** Expected inside a remote desktop session, under high contrast, on
Windows 10 before 1803, and when the theme's frosted-glass switch is off. It is also expected on the session
window itself, which is opaque by design.

**"We don't have write permissions for…" at startup.** The folder holding the profile is read-only. Move the
app somewhere writable, or restart it and choose *Install for current Windows account* to keep data in
`AppData`.

**Getting more detail.** Set `Options → General → Log level` to a more verbose setting and read
`.logs/1Remote.log.md`. Attach the relevant part to an issue.

## Building from source

See **[DEVELOP.md](DEVELOP.md)**. In short: Visual Studio 2022 on Windows, restore NuGet packages, build
`1Remote.sln`. Release builds are produced by
[`.github/workflows/build-on-dev-push.yml`](.github/workflows/build-on-dev-push.yml), which publishes the
same two zips linked above.

Note that a fork build does not inherit the upstream repository's build secrets. Telemetry is inert without
them, and the string-encryption salt falls back to a publicly known constant — so a build you make yourself
must not be pointed at a password store created by an official release.

## Contributing

Bug reports and ideas are welcome in [issues](https://github.com/chaogei666661/1Remote-plus/issues). Pull requests are
welcome too; please read [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) first.

Issues about **this** build belong here. Behaviour that also exists in
[chaogei/1Remote-Plus](https://github.com/chaogei/1Remote-Plus) or in
[1Remote/1Remote](https://github.com/1Remote/1Remote) is better reported there, where it can be fixed for
everyone.

## License

GPL-3.0. See [LICENSE](LICENSE).

Copyright (C) Shawn Veck and the 1Remote contributors, for the original work.
Copyright (C) chaogei, for the 1Remote Plus modifications.
Further modifications in this fork are copyright (C) chaogei666661.

## Credits

- Original author: [Shawn Veck](https://github.com/VShawn) — [1Remote/1Remote](https://github.com/1Remote/1Remote)
- 1Remote Plus: [chaogei](https://github.com/chaogei) — [chaogei/1Remote-Plus](https://github.com/chaogei/1Remote-Plus)
- <a href="http://www.jetbrains.com/resharper/"><img src="http://www.tom-englert.de/Images/icon_ReSharper.png" alt="ReSharper" width="24" height="24" /></a> ReSharper

## Further reading

This fork has not forked the documentation site, so upstream's docs remain the reference for everything that
was not changed here:

- [Quick start](https://1remote.github.io/usage/quick-start/)
- [Overview](https://1remote.github.io/usage/overview/)
- [Custom runners](https://1remote.github.io/usage/protocol/runner/)
- [RemoteApp](https://1remote.github.io/usage/protocol/especial/remoteapp/)
- [NoMachine and other apps](https://1remote.github.io/usage/protocol/especial/app/)

Where those pages and this README disagree, this README describes the fork.
