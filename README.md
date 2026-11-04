# Taskbar Folders

> Group apps into folders and pin them to the Windows 11 taskbar.  
> Click the pinned icon → a clean themed flyout slides up with your apps.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/keyvan-alavi/TaskbarFolders?include_prereleases)](https://github.com/keyvan-alavi/TaskbarFolders/releases)

---

### Why this exists?

Windows 11 still doesn’t let you create real folders on the taskbar.  
This small tool solves that problem without any background process or tray icon.

---

### What's new in 1.3

| # | Feature |
|---|---------|
| 1 | **Folder colours** — automatic theme colour, 12 presets or any custom colour |
| 2 | **Add apps from the Start menu** — searchable picker + “+ Add apps” inside the menu |
| 3 | **Drag & drop** — drop apps onto folders or between folders (Ctrl to copy) |
| 4 | **Easier pinning** — Pin button, Drag-to-taskbar handle, drag folder row, auto “Pinned ✓” |
| 5 | **Opening animations** — Slide+Fade, Slide, Fade, Zoom or None (per-folder override) |
| 6 | **Themes** — Windows 11 (follows system + accent), Light, Dark, Aurora, Sunset, Forest, Midnight |
| 7 | **Settings page** — appearance, animation, Windows integration, data folder, contact links |

---

### Screenshots

| Manager | Flyout |
|---------|--------|
| ![Manager](assets/screenshot-manager.png) | ![Flyout](assets/screenshot-flyout.png) |

---

### Requirements

- Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)

---

### How to use

1. Download `TaskbarFolders.exe` and put it in a **permanent** location (e.g. `C:\Tools\TaskbarFolders\`).
2. Run the program → create a folder → add apps (drag & drop, Start menu picker, or “+ Add apps”).
3. (Optional) Set folder colour, icon, layout and animation.
4. Click **Pin to taskbar** (or drag the folder / use the drag handle).
5. Click the pinned icon on the taskbar anytime to open the menu.

> Already pinned folders from older versions keep working if the exe stays at the same path.

---

### Build from source

```powershell
git clone https://github.com/keyvan-alavi/TaskbarFolders.git
cd TaskbarFolders
dotnet publish -c Release
```

Output:  
`bin\Release\net8.0-windows\win-x64\publish\TaskbarFolders.exe`

---

### Contact & Support

- Website: [https://technokey.ir](https://technokey.ir)
- Telegram: [https://t.me/Kewin_67](https://t.me/Kewin_67)
- Email: key1.alavi@gmail.com
- GitHub: [keyvan-alavi](https://github.com/keyvan-alavi)

---

### License

MIT License – free to use, modify and distribute.

Made with ♥ by **Keyvan Alavi**
