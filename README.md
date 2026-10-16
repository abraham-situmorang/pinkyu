# Pinkyu 🟢🔴

An ultra-lightweight Windows desktop utility with zero window UI that monitors your internet connectivity directly from the **System Tray (Taskbar notification area near Date & Time)**.

---

## ✨ Features

- **Zero UI Window**: Runs silently in the background without cluttering your desktop.
- **Visual LED Blinking Indicator**:
  - 🟢 **Blinking Green**: Connection is healthy and responsive.
  - 🔴 **Blinking Red**: Connection timeout / packet loss (blinks with urgent contrast to immediately catch your attention).
- **Nonstop 3-Second Ping**: Continuously sends an asynchronous ICMP ping every 3 seconds with minimal CPU and memory overhead.
- **Realtime Tooltip**: Hover over the tray icon to see live status and latency (e.g., `Pinkyu: Connected (16 ms) - 8.8.8.8`).
- **Tray Context Menu (Right Click)**:
  - View live status & roundtrip latency.
  - Switch target on-the-fly (`8.8.8.8 Google`, `1.1.1.1 Cloudflare`, or enter a custom IP/hostname).
  - Force an immediate check with **"Ping Now"**.
  - Toggle **"Start with Windows"** with a single click.
  - Safely close with **"Exit"**.
- **100% Portable (Self-Contained)**:
  - Compiled into a single `Pinkyu.exe`.
  - Runs on any 64-bit Windows machine **without needing .NET Runtime installed**.

---

## 📍 Executable (Direct Download)

You can download `Pinkyu.exe` directly from the root of this repository:
- **Direct download**: [Pinkyu.exe](https://raw.githubusercontent.com/abraham-situmorang/pinkyu/main/Pinkyu.exe) or clone/download this repository.
- **Local path**: `Pinkyu.exe` in this folder.

---

## ❓ Is 8.8.8.8 the right target?

**Yes, absolutely!**
1. **Google Anycast Routing**: `8.8.8.8` is globally distributed across Google edge nodes, automatically routing your ping to the nearest ISP gateway.
2. **99.99% Uptime**: Because it almost never experiences downtime, a timeout is a reliable indicator that your local connection or ISP is having issues.
3. **Alternative (`1.1.1.1`)**: Cloudflare DNS is also an excellent option and sometimes yields slightly lower latency on certain ISPs. You can switch between them anytime via the tray menu.

---

## 🚀 How to Run on Windows Startup

Choose whichever option is most convenient:

### Option 1: Directly from the Tray Menu (Recommended)
1. Run `Pinkyu.exe`.
2. Right-click the Pinkyu tray icon near the date & time.
3. Check **"Start with Windows"**. That's it!

### Option 2: Via PowerShell
Run this command in PowerShell:
```powershell
$exePath = "C:\repos\pingkyu\publish\Pinkyu.exe"
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Pinkyu" -Value "`"$exePath`""
```

To remove it from startup:
```powershell
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Pinkyu" -ErrorAction SilentlyContinue
```

### Option 3: Via Windows Startup Folder
1. Press `Win + R`, type `shell:startup`, and press Enter.
2. Create a shortcut to `Pinkyu.exe` inside that folder.

---

## 👤 Author

**Abraham Situmorang**

