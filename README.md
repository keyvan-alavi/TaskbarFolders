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

### What's new in 1.4

| # | Feature |
|---|---------|
| 1 | **Scan this PC** — scans Start-menu apps, auto-tags them (AI, Development, Browsers, Media, Office, Games…) and can create one folder per category |
| 2 | Fixed the 2/3/4-column crash |
| 3 | **Drag to taskbar** now actually starts a proper drag |
| 4 | **Pin flow fixed** — opens the shortcut in Explorer already selected + step-by-step helper |
| 5 | **Single manager instance** — reopening brings the existing window forward |
| 6 | **Config backup** — every save keeps `config.backup.json` |
| 7 | All previous features (colours, themes, animations, Start-menu picker, drag & drop…) |

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
2. Run the program → create a folder or use **Scan PC…** to auto-organise.
3. Add apps (drag & drop, Start menu, Scan, or “+ Add apps”).
4. (Optional) Set colour, icon, layout and animation.
5. Click **Pin to taskbar** (or use Drag to taskbar / drag the folder row).
6. Click the pinned icon on the taskbar anytime to open the menu.

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
