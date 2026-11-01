# Storage widget

User request, 1 Nov 2026 (Windows and the phone). A Storage card shows free and used space on the PC's fixed drives, with a warning colour when a drive runs low. It is hidden by default, so the default layout doesn't change; turn it on in Edit layout's Widgets list.

## What it reads

- `StorageService.Read` lists `DriveInfo.GetDrives()` with `DriveType.Fixed` that are ready, in letter order. Network, removable and optical drives are left out, and a drive that isn't ready (a locked BitLocker volume, an empty card slot) is skipped. Virtual drives that Windows reports as fixed (for example Google Drive's) are listed like any other.
- Free space is `TotalFreeSpace`, the number File Explorer shows. Sizes are 1024-based with Explorer's labels: "820 MB", "45.2 GB", "212 GB", "1.82 TB".
- Reading can wait on a sleeping disk, so it runs off the UI thread, once a minute while the card is shown on the page (every two minutes in Quiet mode), from the existing one-second heartbeat. A hidden card, another page or a minimized dashboard doesn't read the drives.
- The phone's `storage` section shares the dashboard's last reading for the same interval, so a phone never makes the PC read its drives more often. It sends the threshold and each drive's name, label, total and free bytes; the phone works out what's low with the same rule (`StorageText.java`).

## Low space

Settings → Widgets → Storage has a slider, 1–50% free, default 10% (`AppSettings.StorageLowPercent`). A drive with less free space than that turns its bar or ring and its free-space number to the Warning colour, and the card's heading names it ("C: LOW", or "2 DRIVES LOW").

## Designs (`CardSizing.Storage`, the same numbers on the phone)

| Design | When | Shows |
| --- | --- | --- |
| Small | under 340 wide or 200 tall | One row per drive: the letter, the free space as a large reading, and a thin used-space bar. |
| Medium | otherwise, below Large | A row per drive: letter and name, "212 GB free", a bar, and "803 GB used of 953 GB · 16% free" while every drive fits with that line (76 units a row). With more drives the rows drop the line and use smaller text (36 units a row), so four drives fit the reference 472 × 250 card. |
| Large | 440 × 340 and up | A tile per drive with a ring of used space, the free percentage inside, and the drive's name and "149 GB free of 953 GB" under it. Two tiles across, four on cards 960 wide. |

Each design holds as many drives as its rows or tiles fit (`CardSizing.StorageCapacity`); the rest are counted in a last line, "+3 more drives", which turns to the warning colour when one of them is low ("· 1 low").

The card has no tap action in this version.

## Checks

`tests/Storage/StorageTests.cs` covers sizes, the low rule and heading flag, the threshold's default, save and clamp, the hidden default placement, the design and capacity numbers, a live read of this PC's drives (read only), and every design at 140% text with a labelled four-drive fixture staying inside the card. The phone's checks (`AndroidTests.java`) cover the same numbers and wording, and parse the real `storage` section from the Windows checks' sample.

Not tried on a phone or emulator; the phone card builds and its numbers are checked like the PC's.
