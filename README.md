# Contrastor

Contrastor is a lightweight Windows app for accessibility audits. Sample colors from any connected display with a magnifying eyedropper or enter hex codes to check WCAG 2.2 contrast thresholds for text and non-text elements.

## Getting started

- **Portable version** — Run `dist/Contrastor.exe`.
- **Installed version** — Run `dist/Contrastor-Installation.exe` and select **Installer ou mettre à jour** (Install or update). Contrastor installs for your Windows account and appears in the Start menu. To uninstall it, run the same installer and select **Désinstaller** (Uninstall).

Contrastor works offline and requires .NET Framework 4.8 or later. Windows 10 version 1903 and later and Windows 11 include a compatible version. Both executables are unsigned. The app and installer currently use French labels.

## Measuring contrast

1. Enter the foreground and background colors as `#RGB` or `#RRGGBB`. The leading `#` is optional.
2. To sample a color, position the windows you want to audit across your displays, then activate the eyedropper for the relevant field. Contrastor hides itself and freezes the visible desktop. Center the circular magnifier on the target pixel and click or press **Enter** to select it. The arrow keys move the crosshair one physical pixel at a time. Press **Space** to display the current hex code and pixel coordinates and announce them to a screen reader. Press **Esc** or right-click to cancel.
3. Read the contrast ratio and verdicts. A green check mark means pass, a red cross means fail, and a dash means the result is pending valid input. In Windows high contrast mode, the icons use system colors and keep their distinct shapes.
4. Select **Lire les résultats** (Read results) or press **Ctrl+R** to open the selectable text report. Press **Esc** to close it. Select **Copier le résultat** (Copy result) to copy the report for an audit.

The eyedropper samples the rendered screen pixel. For antialiased web text, use the effective CSS colors when available. For translucent layers, measure the composited colors against the relevant background. Each verdict tells you whether the color pair meets the threshold for that category.

| WCAG 2.2 criterion | Regular text | Large text | UI components and graphical objects |
| --- | --- | --- | --- |
| 1.4.3 Contrast (Minimum), AA | 4.5:1 | 3:1 | |
| 1.4.6 Contrast (Enhanced), AAA | 7:1 | 4.5:1 | |
| 1.4.11 Non-text Contrast, AA | | | 3:1 |

Large text is at least 18 pt, or at least 14 pt when bold. Verdicts use the ratio before rounding. [W3C Web Content Accessibility Guidelines (WCAG) 2.2](https://www.w3.org/TR/WCAG22/)

## Keyboard and screen reader access

**Tab** and **Shift+Tab** move between fields and buttons. **Alt+P** moves to the foreground field and **Alt+A** moves to the background field. After a short pause following a color edit, the app sends the ratio summary or input error to the screen reader. You can complete a contrast check with the keyboard using two known hex colors and open the report with **Ctrl+R**. The eyedropper can announce the current pixel and its coordinates. Locating an arbitrary element in the captured image remains a visual task.

## Building from source

Run `Build.cmd` on Windows to regenerate both executables in `dist`. The script uses the C# compiler included with .NET Framework.

## License

Contrastor is released under the [MIT License](LICENSE).
