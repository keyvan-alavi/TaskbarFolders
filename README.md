# Taskbar Folders

> Group apps into folders and pin them to the Windows 11 taskbar.  
> Click the pinned icon → a clean acrylic flyout slides up with your apps.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/keyvan-alavi/TaskbarFolders?include_prereleases)](https://github.com/keyvan-alavi/TaskbarFolders/releases)

---

### Why this exists?

Windows 11 still doesn’t let you create real folders on the taskbar.  
This small tool solves that problem without any background process or tray icon.

---

### Features (v0.2.0-beta)

- Create unlimited folders
- Drag & drop `.exe` and `.lnk` files
- **Custom folder icons**: Automatic grid / Emoji / Custom image (PNG, ICO, JPG)
- **Flexible layout**: 1–4 columns (list view or icon grid)
- Option to show / hide app names in grid view
- Windows 11 style acrylic flyout with rounded corners
- Dark / Light theme support (follows system)
- Launch as normal user or as Administrator (right-click or `Shift` + `Enter`)
- Keyboard navigation (Arrow keys, Enter, Esc)
- Almost zero RAM usage when idle (process exits after the menu closes)
- Error logging (`%AppData%\TaskbarFolders\error.log`)

---

### Screenshots

| Manager | Flyout |
|---------|--------|
| ![Manager](assets/screenshot-manager.png) | ![Flyout](assets/screenshot-flyout.png) |

---

### Requirements

- Windows 11 (recommended 22H2 or newer for acrylic backdrop)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)

---

### How to use

1. Download `TaskbarFolders.exe` and put it in a **permanent** location (e.g. `C:\Tools\TaskbarFolders\`).
2. Run the program.
3. Create a new folder and drag your apps (`.exe` or `.lnk`) into it.
4. (Optional) Customize the folder icon and layout (1–4 columns).
5. Click the button to create the taskbar shortcut.
6. A folder window opens → **Right-click** on that folder → **Pin to taskbar**.
7. From now on, just click the pinned icon on the taskbar to open the flyout.

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

Feel free to open issues or send feedback.

---

### License

MIT License – free to use, modify and distribute.
