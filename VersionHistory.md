#  Version history 
## 2.6.6.234 (Release 11)
- **Added**: ✔️ Support for shortcuts with target spotify (e.g. "spotify:search:rock").
- **Added**: 👻 Auto hide Frames. All Frames can autohide after a user selectable period of time of inactivity.
- **Added**: ⚡Run as different user and Always run as different options for shortcuts (available under icon's `CTRL + right click` menu).
- **Fixed**: 🪲 Bug for frame not rolling down (https://github.com/limbo666/DesktopFramesPlus/issues/85).
- **Fixed**: 🐜 Crash on tray double click (https://github.com/limbo666/DesktopFramesPlus/issues/75)).
- **Added**:  🎈 Support for position blank spacers (available as option on `CTRL + right click` on any frame area).
- **Added**: 🎈 Focus frame function (available either on tray menu or by pressing hotkey combination).
- **Improved**: Filter indicator icon on portal Frames follows fade logic of other indicators.
- **Improved**: Portal Frames can be shorted by: Name, Date modified, Type, Size (cycle shorting method by `CTRL + Click` on any area on the portal frame).    
- **Improved**: ☑️ Startup icons extraction proccess. The program now is significantly faster on startup.
- **Added**: Icon indication effect when user tries to `CTRL + drag`, to re-arrange items order on the frame.
- **Fixed**: Bug on `Restore` function.
- **Improved**: frame and tab renaming function.
- **Fixed**: Disabling single instance bug. Now the program can be run as multi instance if needed by tweaking `options.json` file.
- **Fixed**: Startup contents generation for `options.json` files.
- **Improved**: File and folder paths are switched to relative to program location for safer operation and portability.
- **Added**: ✅ Hotkey selection tab on option window. Hotkeys can be selected by user and settings are applied to all profiles. (Program restart required).
- **Added**: ✅ Options to disable hotkeys for Profile switching, Focus frame and Spot Search.
- **Improved**: Icon load strategy adopting Lazy loader logic from https://github.com/SMSMy/DesktopFrames fork.
- **Added**: 🦎 Chameleon mode for Frames color to match wallpaper major color.
- **Added**: 💥 Smart Desktop engine, to move files to specific portal fneces or folders. Basic rules window and functionality.
- **Fixed**: 🐛 Bug on Portal Frames navigation, where program denied navigation to subfolder with `CTRL + click`
- **Fixed**: 🪲 Bug on first tab elements renaming.
- **Fixed**: 🪲 Bug on export engine for frame names ending with dots or spaces.
- **Fixed**: 🐛 Bug on size indicator for dimension snap.
- Some other minor fixes.

## 2.6.5.210 (Release 10)
- **Added**: 💥 Profiles<br>
  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;🔖Profile Manager<br>
  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;🔖Profile Automation - Switches profile based on specific program activation <br>
  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;🔖Profile Swicthing Hotkeys - (`CTRL+ALT+[Profile Number]`, `CTRL+ALT+<`, `CTRL+ALT+>`) <br>
- **Changed**: Support files `ProfileOptions.json` is automatically created to manage profile info, `Options.json` moved under profile folder,
- **Added**: Optional `MasterOptions.json` file. This file should be manually created on program root to apply Global Configuration for all Profiles.
- **Changed**: Support folders moved under profile name folder.
- **Fixed**: 🐛 Bug for shortcuts displayed without their original icon.
- **Fixed**: 🐛 Bug for powerpoint origin shorctuts.
- **Added**: Resolution related techniques to restore frame position.
- **Added**: Manual tool to "Screen Bound Frames" available under "Options" > "Tools".
- **Updated**: Backup/Restore engine to support profiles.  

## 2.5.4.188 (Release 9)
- **Added**: ⤵️ Import tab function.
- **Fixed**: 🐛 Tab renaming bug fixed.

## 2.5.4.186
- **Added**: Basic folder navigation for Portal Frames.
- **Changed**: Tab naming pattern.

## 2.5.4.184
- **Changed**: Log engine optimized to reduce file system load.


## 2.5.4.183
- **Changed**: Heart Menu rearranged.
- **Added**: Tab overflow management and navigation mechanism.
- **Fixed**: 🐛 Bug in settings application from the Options window.


## 2.5.4.178
- **Changed**: Massive tab-related adjustments.
- **Changed**: Backup and Export/Import engine updated to support tabs.
- **Fixed**: 🐛 Several minor bugs.


## 2.5.4.172
- **Fixed**: 💊 Visual glitches in various program functions.
- **Fixed**: 🐛 Missing context menu for weblinks dropped into Frames.
- **Added**: 🎨 Icon customization option for shortcuts targeting weblinks. (This one was not easy.)

## 2.5.4.170
- **Added**: 🎨 Icon customization option for shortcuts targeting folders.
- **Added**: Support for rearranging icons on tabs using CTRL + Drag.


## 2.5.4.161
- **Added**: 💥 Tabs❕ 👋 Half of the tabs code was already present, but this release involved a long journey of debugging, stabilizing, and improving functionality to finally bring tabs to life.
- **Fixed**: 🐛 Minor bugs discovered during tab development. Overall program stability improved.
- **Changed**: Backup engine updated. The program now backs up and restores options.json (if selected during restore).


## 2.5.3.155
- **Changed**: 🎨 Customization window for Portal Frames now allows changes to icon size, icon text, etc., which were previously disabled.
- **Changed**: Snap logic improved for better Snap Near Frames functionality.
- **Changed**: 🆒 Right-click menu items on Data Frames are now populated dynamically and displayed only when actions are available.
- **Added**: ☑️ Ability for Portal Frames to display folders with their original customized icons.
- **Added**: ☑️ Daily automatic backup functionality.
- **Added**: ☑️ Cut and Copy options in the right-click menu for Portal frame items.
- **Added**: ☑️ Reset and Clear All Data buttons in Options.


## 2.5.3.145
- **Changed**: Portal frame filters. Added predefined filters. Added a (hidden) option to disable wildcards.
- **Added**: ☑️ Filter history for Portal frame filters.
- **Fixed**: 🐛 Bug in tint application.

## 2.5.3.144
- **Changed**: Initial startup values and Frames. Added a short Note frame on first start for user information.
- **Fixed**: 🐛 Bug in the frame renaming escape mechanism.
- **Added**: ☑️ Tweak to show/hide file extensions on Portal Frames.
- **Added**: ☑️ Filters for Portal Frames.


## 2.5.3.140
- **Added**: ☑️ Tweak to select a key combination for SpotSearch (~, title, space, q, F1)


## 2.5.3.137 Release 8
- **Added**: ☑️ Update checker engine
- **Fixed**: 🐛 Missing functions from drop icon restored.
- **Added**: ⚙️ Additional tweaks to automate icon extraction to desktop on frame deletion and icon removal from desktop on drop to Frames.


## 2.5.3.135
- **Fixed**: 🐛 Frames now are escaping the Windows Snap Assistant. https://github.com/limbo666/DesktopFramesPlus/issues/39
- **Changed**: Portal Frames background image.
- **Added**: ☑️ Controls to new customization functions.
- **Changed**: Options window. Controls re-arranged. Added function to remember last tab used on options menu (during runtime).
- **Changed**: Start with Windows function changed into a more reliable approach. Now registry is used instead of startup folder. This possibly fixes the issue: 🐛 https://github.com/limbo666/DesktopFramesPlus/issues/29
- **Fixed**: 🐛 Failed to save new name bug. https://github.com/limbo666/DesktopFramesPlus/issues/45
- **Added**: Basic support for MS store based apps. This is a bare minimum implementation, no shortcut icon customization support. 🐞 Bugs expected to be found on this. https://github.com/limbo666/DesktopFramesPlus/issues/34
- **Fixed**: 🐛 Bug with program stability. Caused by icon updates and introduced during code migration.
- **Fixed**: 🐛 Bug with lost context menu on frame customization and dead shortcut cleanup. https://github.com/limbo666/DesktopFramesPlus/issues/27
- **Added**: ✨🔎📣 Search pane "SpotSearch" to search and quick launch shortcuts in all data Frames. 🔥📝 Use hotkey ``CTRL+` ``
- **Changed**: 🔩 Some code refactoring.


## 2.5.2.125
- **Fixed**: 🐛 Folder name display for folder with dots on their name.
- **Fixed**: 🐛 Steam shortcuts support finally fixed https://github.com/limbo666/DesktopFramesPlus/issues/25.
- **Added**: Function to override Win + D key combination. Frames now re-appearing after Win + D is pressed https://github.com/limbo666/DesktopFramesPlus/issues/26.
- **Added**: Opacity change effect on Heart menu and Lock icons https://github.com/limbo666/DesktopFramesPlus/issues/31.
- **Added**: Option to select Menu icon and Lock icon (4 icons for each one) https://github.com/limbo666/DesktopFramesPlus/issues/31.
- **Added**: Option to set shortcut to "Always run as administrator".
- **Added**: Option to disable sound globally. 🔇 Requested on https://github.com/limbo666/DesktopFramesPlus/issues/40.
- **Added**: 🎇 Note Frames (still a bit buggy but working).
- **Changed**: Double click on frame title scrolls up/down. Ctrl + Click enters rename mode. Requested on https://github.com/limbo666/DesktopFramesPlus/issues/38.




## 2.5.2.111 Release 7
- **Added**: Program made single instance (with a twist 😃).
- **Added**: "Clear Dead Shortcuts" right click option to remove all not valid shortcuts from a frame.
- **Added**: "Send To Desktop" right click option to copy a shortcut to desktop (📝 Use CTRL + right click).
- **Added**: "Copy and Paste" options to copy items across Frames.
- **Fixed**: 🐛 All forms were redesigned and made dpi aware to work under monitors with scaling enabled.
- **Added**: Message boxes accepted Enter and Esc as validation.
- **Added**: Variables can be set on `options.json` to manual tweak program. 🐉 See [tweaks](https://github.com/limbo666/DesktopFramesPlus/blob/main/tweaks.md).
- **Changed**: Target launch mechanism. This probably will fix most common launch errors. https://github.com/limbo666/DesktopFramesPlus/issues/24
- **Changed**: 🔨🔩🔧 Another source code refactoring.
- **Added**: Option to disable frame scrollbars. https://github.com/limbo666/DesktopFramesPlus/issues/21
- **Fixed**: 🪲 🐛 Edit icon panel. Now arguments editing, icon preview, restore default settings bugs are eliminated.




## 2.5.2.95
- **Fixed**: 🐛 🪲 A majority of minor bugs and annoyances


## 2.5.2.86
- **Changed**: Major changes to existing windows messages and other forms.
- **Changed**: 🔨🔩🔧 Another massive source code refactoring.
- **Added**: Customize window, available on frame context menu of each frame that allow user to tweak all available options.
- **Removed**: ❌ Customization submenus for effects and colors from context menu.
- **Fixed**: 🐛 Bug on Unicode folder icon.

## 2.5.1.75
- **Added**: Error Handles on JSON loading for better stability against corrupted `.json` files
- **Changed**: Portal Frames are named after the target folder upon creation.
- **Changed**: Minor interface improvements for systems with resolution scaling enabled.
- **Fixed**: 🐛 Handling of shortcuts with unicode characters.
- **Changed**: Improved stability of filesystem watcher for portal Frames.
- **Added**: `Rename` option for files on portal Frames.
- **Added**: Lot of tweaks user can set by editing JSON files.



## 2.5.1.70
- **Changed**: 🔨🔩🔧 Major code refactoring.
- **Added**: 🎇 Four new launch effects.

## 2.5.1.67
- **Added**: Function to re-order icons within a frame (https://github.com/limbo666/DesktopFramesPlus/issues/15). 📝 Use `CTRL + drag`.


## 2.5.1.65
- **Changed**: `Delete frame` option moved to Heart context menu
- **Fixed**: 🐛 Bug on handling shortcuts targeting web links.
- **Added**: New icon for shortcuts targeting web links.
- **Changed**: Target check mechanism to prevent errors.
- **Added**: Indicator for network based files.


## 2.5.1.64
- **Added**: Rollup function when `Ctrl + Click` on frame title (📝 Use CTRL + click).
- **Added**: Function to filter hidden files on Portal Frames (request https://github.com/limbo666/DesktopFramesPlus/issues/13 and possibly fixing https://github.com/limbo666/DesktopFramesPlus/issues/14 as well).


## 2.5.1.58 Release 6
- **Added**: Option to show/hide tray icon (requested on https://github.com/limbo666/DesktopFramesPlus/issues/9). Attention: Hiding tray icon means you don't have access to: showing hidden Frames and hiding/showing Frames by double clicking on tray icon.
- **Added**: Option to show/hide portal Frames watermark (requested on https://github.com/limbo666/DesktopFramesPlus/issues/11).
- **Added**: Option to use recycle bin when deleting files or folders using portal Frames right click menu.
- **Added**: Lock function to Frames (requested on https://github.com/limbo666/DesktopFramesPlus/issues/9).
- **Changed**: 🔨🔩🔧 Large refactoring on Log code to organize and filter logs.
- **Changed**: Options window redesigned for better user experience.
- **Fixed**: 🐛 Bug on custom message box with wrong color selection.
- **Added**: "Peek Behind" right click selection to make Frames to reveal desktop contents behind them for 10 seconds.



## 2.5.1.42
- **Fixed**: 🐛 Bug on `Portal Frames` created by misuse of FileSystemWatcher. The program now updates target files as renamed, removed. https://github.com/limbo666/DesktopFramesPlus/issues/3
- **Added**: Context menu items for `Portal Frames` and items. Now user is able to copy target file path or shortcut destination path and open `Portal frame` target folder from right click.




## 2.5.1.40
- **Fixed**: 🐛 Bug on `Start with Windows`. The program now displays shortcuts correctly https://github.com/limbo666/DesktopFramesPlus/issues/6
- **Fixed**: 🐛 Bug with `Options` and `About` screen that misplaced controls on scaled displays https://github.com/limbo666/DesktopFramesPlus/issues/5
- **Added**: Function to display Frames which are saved out of screen bounds (restored from other systems).


## 2.5.1.37 - Release 3
- **Added**: Snap to Dimension function for better size alignment
- **Added**: Export frame and Import frame options to help move Frames across systems. Few exported Frames can be found [here](https://github.com/limbo666/DesktopFramesPlus/tree/main/Exported%20Frames)
- **Added**: Ability to get icons from dll libraries or executables on under `Edit` Requested on https://github.com/limbo666/DesktopFramesPlus/issues/1
- **Changed**: All message boxes changed to internal themed ones to follow mouse position across multimonitor systems
- **Changed**: Some theming correction on message boxes.
- **Added**: Sound on custom message box appearance.


## 2.5.0.30
- **Added**: Restore for previously backed up configurations
- **Added**: Reload all Frames function
- **Added**: Tooltips on options window
- **Added**: Restore hidden Frames to their hidden status on startup
- **Added**: Temporarily hide function for all Frames (show desktop) on tray icon double click, restore with double click


## 2.5.0.26
- **Changed**: Background color codes
- **Added**: More background colors
- **Added**: More launch effects (I ♥ Elastic)
- **Fixed**: 🐛 Bug on context menus items unchecking behavior
- **Fixed**: 🐛 Bug on customization where changes were not applied into Frames under same name
- **Added**: Random name generator for new Frames instead of the dull "New frame"
- **Added**: Custom delete confirmation message box


## 2.5.0.23
- **Added**: ❤️ Heart menu to separate right click menu item on Frames
- **Added**: Function to undo frame deletion (Restore frame)
- **Fixed**: 🐛 Bug on frame removal causing program to crash
- **Changed**: Lot of menus, descriptions, and other visual elements changed.
- **Added**: Number overlay on tray icon that indicates number of the hidden Frames


## 2.5.0.18
- **Added**: Custom animation selection for each frame
- **Added**: Custom background color selection for each frame
- **Fixed**: 🐛 Bug on frame movement across multiscreen systems (https://github.com/limbo666/DesktopFramesPlus/issues/2)
- **Changed**: Start with windows option moved under `Options` window
- **Fixed**: Passing argument in `Run as administrator` selection
- **Fixed**: 🐛 Bug on portal Frames causing program crash on startup when target folder is missing
- **Changed**: Code improvements on log function
- **Added**: Basic `Hide` function for each frame


  ## First release 🔥
- **frame JSON File** now placed in the same directory as the executable
- **First frame Line** is created automatically during the first execution
- **Program Icon** added
- **Error Handlers** for Move actions, Program execution, Empty or invalid JSON files
- **Tray Icon** indicates the application is running
- **Program Exit Option** in the tray icon’s context menu
- **New frame Creation** at mouse location for intuitive placement
- **About Screen**
- **Shortcuts no longer depend** on original shortcut files
- **Execution Arguments** of original shortcuts are preserved
- **Target Type Detection** with proper error handling
- **Visual Effects** on icon click and icon removal
- **Right-Click Context Menu for Icons** with: Run as Administrator (when applicable), Copy Path, Find Target
- **Folder Icon Appearance** fixed
- **Broken Link Detection** on startup and updated every second
- **Automatic JSON Format Updater** for existing .json
- **Frames No Longer Take Focus** from other windows when clicked
- **Options Window** to: Enable/disable snap function, Set tint level, Select base color
- **Options Saved** in `options.json`
- **Manual Backup Mechanism**: Saves Frames and shortcuts to a `backups` subfolder
- **Logging Option** for diagnostics
- **Selectable Launch Effects**: Zoom, Bounce, Fadeout, SlideUp, Rotate, Agitate
- **Run at Windows Startup** option


---
