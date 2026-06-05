# Desktop Frames + User Manual


<div style="width:20%; margin: auto;">


</div>


## Overview

Desktop Frames + is a powerful desktop organization tool that creates virtual "Frames" on your desktop, allowing you to group and organize icons in a clean and convenient way. Think of Frames as visible containers that help you organize your desktop shortcuts, files, and folders into logical groups.

---

## Getting Started

### System Requirements

- Windows 10 or Windows 11 operating system
- .NET Framework support


### First Launch

When you first start Desktop Frames +, the application will:

1. Create necessary basic configuration files
2. Add itself to the system tray

---

## Core Features

### 1. Creating Frames

**What are Frames?**
Frames are containers on your desktop that group related shortcuts and files together. You can think of them as boxes that keep your desktop organized.

**frame Types:**

- **Frames (Data Frames)**: Regular containers for shortcuts targeting files or folders and web links
- **Portal Frames**: Special Frames mirroring the contents of folders
- **Note Frames**: Frames that display text.

**How to Create a frame:**

1. The first two Frames are created automatically on first run. Look on the top left corner of your screen.
2. From there clicking the heart menu you can select New frame to create a new frame, New Portal frame to create a new Portal frame or New Note frame to crete a new note frame.

**Rename a frame:**

- ~~Double click~~ CTRL: + Click the frame title. Type your preferred name and click out of the edit area to finish. Changed on v2.5.2.125

### 2. Adding Items to Frames

**Adding Shortcuts:**

- Drag and drop existing desktop shortcuts, folders, or files into any frame. This will generate automatically a shortcut for the target.

  **Supported types**

- Shortcuts (.lnk files)
- Web links (.url files), shortcuts targeting web sites or links from web browsers
- Folders and directories
- Document files
- Network targers

### 3. Moving Items Between Frames

**Using the Move Dialog:**

1. Right-click on any item in a frame
2. Select "Move to..." from the context menu
3. Choose the destination frame from the hierarchical list
4. Confirm the move operation

**Using Copy Item:**

1. Right-click on any item in a frame
2. Select "Copy Item" from the context menu
3. Right-click on any other frame and select "Paste Item"

---

## Customization Options

### 1. frame Appearance

Access frame customization by right-clicking on a frame title bar and selecting "Customize...":
This will show the Customize frame window.



**frame Settings:**

- **Custom Color**: Choose from 13 preset colors (Red, Green, Teal, Blue, Bismark, White, Beige, Gray, Black, Purple, Fuchsia, Yellow, Orange)
- **Custom Launch Effect**: Customize the effect on icon click. See Launch Effects below
- **Border Color**: Customize the frame border appearance
- **Border Thickness**: Adjust border width (1-5 pixels)

**Title**

- **Title Text Color**: Change frame title color
- **Title Text Size**: Small, Medium, or Large
- **Bold Title Text**: Make frame titles bold

**Icons**

- **Icon Size**: Tiny (24px), Small (32px), Medium (40px), Large (48px), Huge (64px)
- **Icon Spacing**: Adjust space between icons (0-20 pixels)
- **Text Color**: Color for item labels
- **Disable Text Shadow**: Remove shadow effects from text
- **Grayscale Icons**: Convert all icons to grayscale ()

### 2. Launch Effects

When you click on items in Frames, choose from these visual effects:

- **Zoom**: Items grow before launching
- **Bounce**: Items bounce up and down
- **FadeOut**: Items fade away
- **SlideUp**: Items slide upward
- **Rotate**: Items spin before launching
- **Agitate**: Items shake vigorously
- **GrowAndFly**: Items grow and fly off screen
- **Pulse**: Items pulse with light
- **Elastic**: Items stretch elastically
- **Flip3D**: Items flip in 3D space
- **Spiral**: Items spiral outward
- **Shockwave**: Creates a shockwave effect
- **Matrix**: Matrix-style digital rain effect
- **Supernova**: Explosive light effect
- **Teleport**: Items teleport with particle effects


## Change Icon Order

Hold down the CTRL button and drag the icon to its new position within the frame.


## Customize Shortcuts

You can check and change the icons properties by right clicking the icon and selecting "Edit..."

In the edit window you can set name, target path, arguments and icon for the shortcut. Note: The program supports ico, exe and dll files as icon sources. 



## Manage Invalid Shortcuts

The icons on the Frames which are targeting files and folders are getting continuously checked for target validity. If target is missing or it is not accessible an icon indicate that this shortcut is not functioning, so you can check the target and fix it or delete the dead shortcut. You can right click on this icon and select "Remove".   

**Clear Dead Shortcuts:** If you want to remove all dead shortcuts within a frame with just one move right click on the frame and select "Clear Dead Shortcuts"



## Convenient Usage Functions

**Hide frame**: Right click a frame an select "Hide frame". This sets the frame as hidden and the number of hidden Frames increases ion the tray icon. To show the frame again you can use the tray icon context menu where the hidden Frames are shown as items on "Show Hidden Frames" menu

**Peek Behind**: Right click a frame an select "Hide frame". The frame hides for 10 second to help access the desktop behind the frame. This feature is helpful if for some reason you need to see behind the frame for a short time. A count down timer is shown in place.

**Hide/Unhide All Frames**: Double click the tray icon. All Frames will toggle. Note: This function doesn't change the status of hidden Frames with teh above mentioned "Hide frame" function.

**Rollup/RollDown a frame**: ~~Ctrl + Click~~ Double click on frame title. The frame will rollup into its title. To Roll down again to frame previous height ~~Ctrl + Click~~ Double click again the title area. Changed on v2.5.2.125

**frame Lock**: Click on the shield icon🛡️on top right corner of the frame. This locks frame position and size. Rollup/Rolldown is still available when locked.



## Advanced usage

Right clicking any shortcut the following usage options are available:
**Run as Administrator :** Executes the target program using administrator rights. Note: This uses also the passed arguments on the icon.

**Always run as Administrator :** Marks the shortcut to be executed using administrator rights on each launch. Note: This uses also the passed arguments on the icon.

**Copy Path -> Folder Path** : Copies the target folder path

**Copy Path -> Full Path** : Copies the target file path

**Open target folder** : Open target folder in file explorer



## Search (v2.5.2.135)
Double click ta any enpty arean on any frame or ``CTRL + ``` to see the search window. Search is performed on all data Frames and matching shortcuts are displayed below the search field.  


## Import/Export Frames

Click on the heart ❤️ menu and select "Export this frame" to export a frame into a  *.frame file.  This file will be exported into "Exports" subfolder on program path.
To import a frame exported previously click on the heart ❤️ menu and select "Import a frame", browse for a *.frame file and import it.



---

## Application Settings

Access settings through the system tray icon → "Options":

### General Tab

**Startup:**

- **Start with Windows**: Automatically launch with system startup

  

**Selections:**

- **Single Click to Launch:** Enables single click on icons, otherwise double click is needed. Change requires program restart. 

- **Enable Snap Near Frames**: Automatically align Frames to each other

- **Enable Dimension Snap**: Automatically snaps size to the closest value of multiple of 10

- **Enable Tray Icon**: Display icon in system notification area

- **Use Recycle Bin on Portal Frames 'Delete Item' command:** Send files to recycle bin instead of deleting them
  
  

**Style:**

- **Enable Portal Frames Watermark:** Enables the "portal" watermark image on portal Frames
- **Enable Note Frames Watermark:** To be added
- **Disable Frames Scrollbars:** Hides scrollbars from Frames when contents exceed the available display area
- **Enable Sounds:**  Toggles program ability to make sound on message boxes 

- **frame Tint **: Adjust overall Frames tint (0-100%)
- **Menu Tint **: Adjust menu icons base tint (0-100%)
- **Color**: Sets the default Frames color
- **Launch Effect**: Sets the default launch effect


- **Menu Icon**: Select the  menu icon
- **Lock Icon**: Select the lock icon

### Tools Tab

**Backup and Restore:**

- **Backup Data**: Create timestamped backups of all Frames and shortcuts
- **Import Backup**: Restore from previous backup files
- **Open Backups Folder:** Opens backups folder in file explorer



### Look Deeper Tab

- **Enable Logging **: Enable detailed logging

- **Open Log **: Open log file for viewing

  

---



---

## Troubleshooting

### Common Issues

**Elements on help forms are not aligned properly **

1. Check the resolution scaling.

2. Set scaling to 100% if it is possible.

   This is an ongoing development and will be fixed in future versions 

**Performance Issues**

1. Disable logging if enabled. Log can delay the program performance

2. Set launch effect to Zoom or FadeOut

   

**Configuration Issues**

1. Reset the program! Delete options.json and Frames.json from progarm fodler and try again



**Portal Frames Issues**

1. Delete portal frame from Frames.json and try creating again
2. Verify the path exists and it is accessible



---

## Tips for Best Use

### Organization Strategies

**By Category:**

- Create Frames for different types of applications (Games, Work, Utilities)
- Use color coding to visually distinguish categories
- Keep related items together for easy access

**By Frequency:**

- Place frequently used items in easily accessible Frames
- Use larger icon sizes for important applications
- Consider using launch effects for visual feedback

**By Project:**

- Create temporary Frames for specific projects

- Include all related files, documents, and tools

  

---

## Technical Information

### File Locations

**Configuration Files:**

- `Frames.json`: Main frame configuration and layout
- `Desktop_Frames.log`: Application log file
- `Shortcuts/`: Folder containing frame shortcut files

**Backup Structure:**

- `Backups/[TIMESTAMP]_backup/`: Timestamped backup folders
- Contains both configuration and shortcut files
- Compressed archives for easy storage and transfer

**Dependencies:**

- .NET Framework
- Windows Forms and WPF libraries
- Icon extraction libraries
- JSON processing components

---



### Version Information

The application version is displayed in the About dialog, accessible through the system tray menu. This information is useful when reporting issues or checking for updates.

---

*This manual covers the core functionality of Desktop Frames +. The application includes many additional features and customization options discoverable through exploration and experimentation.*

*Ctrl + Click is your friend* <br>


Nov 2025 Nikos Georgousis
Hand Water Pump

