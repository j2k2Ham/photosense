// The user guide, as the Help window shows it. Text between ** and ** names something on screen and is shown bold.

/** A place in Trovolve a topic of the guide can take the reader to, on a computer. */
export type GuidePlace = 'scan' | 'duplicates' | 'similar' | 'organize' | 'settings';

/** Where Trovolve runs, as the guide speaks of it: a Windows PC or Mac, an iPhone or iPad, or an Android phone or tablet. */
export type GuidePlatform = 'computer' | 'ios' | 'android';

/** Only on these platforms; on every one when not said. */
type Only = { readonly only?: readonly GuidePlatform[] };

/** A part of a topic of the user guide: a heading with paragraphs, steps or notes. */
export interface GuideSection extends Only {
  heading?: string;
  /** Paragraphs. */
  text?: string[];
  /** Things to do, in order. */
  steps?: string[];
  /** Things worth knowing, in no order. */
  tips?: string[];
}

/** A topic of the user guide, as Help lists it. */
export interface GuideTopic extends Only {
  id: string;
  title: string;
  /** What the topic is about, in a line. */
  summary: string;
  /** The button that takes the reader to what the topic describes, on a computer. */
  go?: { label: string; place: GuidePlace };
  /** Other words a person might search for it by, which the text itself does not use. Every topic has some. */
  keywords: string[];
  sections: GuideSection[];
}

const COMPUTER = ['computer'] as const;
const PHONES = ['ios', 'android'] as const;
const IOS = ['ios'] as const;
const ANDROID = ['android'] as const;

/**
 * The user guide shown by Help, topic by topic, for every platform. It changes whenever something on screen is
 * renamed, added or removed (CLAUDE.md, "The user guide"); `guideFor` gives one platform's part.
 */
export const GUIDE: GuideTopic[] = [
  // ——— On a computer ———
  {
    id: 'start',
    only: COMPUTER,
    title: 'Getting started',
    summary: 'What Trovolve does, and how to find your way around it.',
    keywords: ['overview', 'introduction', 'how to', 'first time', 'begin', 'windows', 'mac', 'pc', 'terms of use', 'keep a backup', 'accept'],
    sections: [
      {
        text: [
          'Trovolve tidies a folder of pictures and videos. **Clean up** finds the copies of each picture or video and helps you remove all but the best one. **Organize** sorts the files of a folder into folders by where and when they were taken. **Phones** is where phones and tablets with Trovolve back up to this computer.',
          'Everything happens on this computer. Nothing is uploaded.',
          'The first time Trovolve opens in a browser, it asks you to **Keep a backup of photos you care about**: Trovolve asks before it removes or moves anything, but a mistake can still lose a photo or video. **Read the terms of use** shows them; **Accept and continue** accepts them and carries on. It is asked once for you, not on every device: accepted on a phone paired with this computer, it is not asked here. If you accepted on your phone before pairing it, choose **Pair a phone** on the question, and it goes once the phone is paired.',
          'Nothing you delete is erased there and then. A deleted file is moved to a folder called **_Trovolve_Removed**, where it waits, and can be put back, until you choose **Move to Recycle Bin** (**Move to Trash** on a Mac).',
          'A move, copy or deletion happens in full or not at all. Should one file not go, the files that had gone go back, and the message says which file stopped it. One cut short because Trovolve stopped is put back the next time it starts.',
        ],
      },
      {
        heading: 'Finding your way',
        tips: [
          'The switch at the top left, **Clean up | Organize | Phones**, moves between the three areas. Each keeps what you were doing in it. A dot on **Phones** says it is new, until you first open it.',
          'The menu, the three lines at the top right, has **Theme**, **Change folders or rescan** (only while Clean up shows a scan’s results), **Clear results** (while Clean up shows a scan’s results, or Organize a folder’s files), **Settings**, **Support** (with **Errors** and the **Support page** under it), **Help**, **About Trovolve** and **Move to Recycle Bin** (**Move to Trash** on a Mac).',
          'Beside the menu, your plan: **Trovolve Free**, with how many free removals are left today, or **Trovolve Pro**. Point at it for more.',
          'Messages appear at the bottom right. One that offers **Undo** stays a little longer; an error is also kept under **Errors**, in the menu’s **Support**, until you clear it.',
          'In a narrow window the screen rearranges itself to fit. In **Clean up**, click a group to review it, and **All groups** goes back to the list. In **Organize**, **Files** and **Folders** at the top switch between the files and the folders, and clicking a folder shows its files. Make the window wider and the two sit side by side again.',
        ],
      },
      {
        heading: 'A good first run',
        steps: [
          'Scan the folder your pictures are in.',
          'Look through the **Duplicates** and delete the copies you do not need.',
          'Look through the **Similar** shots, a group at a time.',
          'Look through **Categories**: large videos, screenshots, bursts and blurry shots.',
          'Switch to **Organize** and sort what is left into folders.',
          'When you are sure nothing is missed, use **Move to Recycle Bin** in the menu to send them to the computer’s Recycle Bin, and empty it there to free the space.',
        ],
      },
    ],
  },
  {
    id: 'scan',
    only: COMPUTER,
    title: 'Scanning for duplicates',
    summary: 'Choosing the folders, starting a scan, and what happens while it runs.',
    go: { label: 'Go to the scan screen', place: 'scan' },
    keywords: ['rescan', 'search', 'find copies', 'folder', 'drive', 'disk', 'backup folder', 'subfolders'],
    sections: [
      {
        steps: [
          'Open **Clean up**. The first time, it shows the scan screen; later, press **Change or rescan** at the top, or choose **Change folders or rescan** in the menu.',
          'Give the **Root folder**: type its path, or press **Browse** and pick it. The folders listed are those of this computer.',
          'If you like, give a **Secondary folder** too, a backup for example. Copies are then found between the two as well as inside each.',
          'Leave **Recursive** ticked to include the folders inside the folder.',
          'Press **Scan**.',
        ],
      },
      {
        heading: 'While it runs',
        text: [
          'The screen shows how far the scan has got, how many files are done and roughly how long is left. **Show full log** shows what it is doing, line by line.',
          'A first scan reads every file, which takes minutes for some thousands of files; HEIC pictures take the longest. Later scans skip every file that has not changed, so they are much quicker.',
          'A scan runs in the background. You can go to Organize meanwhile, and if the page is loaded again it picks the scan up where it is. When the scan finishes, a message says how many groups it found and the results are shown.',
        ],
      },
      {
        heading: 'Starting afresh',
        tips: [
          '**Start over**, on the scan screen, forgets what earlier scans recorded and reads every file again. It, and **Back to results**, are offered once a scan has found something.',
          '**Clear results**, in the menu, forgets it all without scanning.',
          'Neither touches your pictures, or anything already in **_Trovolve_Removed**.',
          'Files in **_Trovolve_Removed** are never scanned, so a deleted copy does not turn up as a duplicate again.',
        ],
      },
    ],
  },
  {
    id: 'duplicates',
    only: COMPUTER,
    title: 'Reviewing duplicates',
    summary: 'Looking at each picture beside its copies, and removing the copies.',
    go: { label: 'Show the duplicates', place: 'duplicates' },
    keywords: ['copies', 'copy', 'same photo', 'twice', 'dupes', 'identical', 'compare', 'keep', 'original'],
    sections: [
      {
        text: [
          'After a scan, the **Duplicates** tab has a group for every picture or video that has copies. The picture on a tile is the **original**: the best copy, the one that stays. The number on the tile, such as +2, is how many copies it has.',
          'The best copy is the one with the most detail. Between a HEIC and a JPEG of the same picture, the JPEG is kept, because it opens everywhere. A video counts as a copy only when it is the very same file.',
        ],
      },
      {
        heading: 'Going through a group',
        steps: [
          'Click a group. The original is shown beside one of its copies, with what differs between the two and why the original was preferred.',
          'Under them is a strip of every copy. Click one to show it beside the original; hover over it for its name, date, folder, where it was taken and what differs from the original.',
          'Click either picture to open the two large, side by side. There, **Space** flips between them in the same spot, and the arrow keys go through the copies.',
          'Press **Keep this copy** to mark a copy you want to keep. Deleting in bulk skips it.',
          'Press **Delete this copy** to remove the copy shown, or **Delete all copies** (the button says how many) to remove every copy of the group. The original stays.',
        ],
      },
      {
        heading: 'Many at once',
        tips: [
          '**Delete all duplicates**, at the top right, removes every copy in every group and leaves the best copy of each.',
          'Tick the box on a tile to select its group, or use **Select all** above the tiles to select every group on the page. The bar that appears under the tiles deletes the copies of the selected groups together.',
          'The search box finds groups by file name or folder. **Hide reviewed** hides the groups in which every copy is marked keep.',
        ],
      },
      {
        heading: 'Good to know',
        tips: [
          'Hover over a tile, or reach it with **Tab**, for the details of the picture on it: its name, when and where it was taken, its folder, its size and the camera. **Esc** puts the details away.',
          'Every question asked before a removal names the file that goes and the file that stays: two files holding one picture look alike, and are two files all the same.',
          'The message after a removal has an **Undo** button, which puts every file it took back where it was, into its group again.',
          'The last copy of a picture is never removed as a duplicate. If the original has been moved, changed or deleted since the scan, its copies are left alone; scan again to bring the results up to date.',
          'A picture’s Live Photo video and edit file go with it, when they belong to that picture and to no other.',
          'If the Duplicates tab is empty, the scan found no copies. Anything that only looks alike is under **Similar**.',
        ],
      },
    ],
  },
  {
    id: 'similar',
    only: COMPUTER,
    title: 'Similar shots',
    summary: 'Burst frames and edited versions: pictures that look alike but are not copies.',
    go: { label: 'Show the similar shots', place: 'similar' },
    keywords: ['burst', 'edited', 'look alike', 'near duplicates', 'best shot'],
    sections: [
      {
        text: [
          'The **Similar** tab has the pictures that look alike without being copies of one file: the frames of a burst, or an edited version beside the picture it was made from. The picture on a tile is the **best shot** of its group.',
          'These are different pictures, so they are never removed by **Delete all duplicates**. Go through them a group at a time, exactly as with duplicates: look at each shot beside the best one, then delete the ones you do not want.',
        ],
      },
      {
        heading: 'Several groups at once',
        tips: [
          'Tick the groups you have looked at, or **Select all** on the page, and use the bar under the tiles to delete their similar shots together. The best shot of each group stays.',
          'The question asked first says that these are not copies of one file. They go to **_Trovolve_Removed** like anything else, so a shot you miss can be put back.',
        ],
      },
    ],
  },
  {
    id: 'categories',
    only: COMPUTER,
    title: 'Large videos, screenshots, bursts and blurry shots',
    summary: 'The Categories tab: kinds of file that are often safe to let go, each to look through and delete.',
    keywords: ['categories', 'screenshot', 'screen shot', 'large files', 'big videos', 'burst', 'blur', 'out of focus', 'whatsapp', 'telegram', 'free space', 'storage'],
    sections: [
      {
        text: [
          'The **Categories** tab, beside **Duplicates** and **Similar**, lists kinds of file that are often safe to let go, largest first, each with how many there are and how much space they take: **Large videos**, **Screenshots**, **Bursts**, **Blurry**, and **Messaging apps** when WhatsApp or Telegram saved files in the folder. The number on the tab is how many files they hold in all.',
          'Click a kind to see its files. Hover over a file for the folder it is in.',
        ],
      },
      {
        heading: 'Each kind',
        tips: [
          '**Large videos** are videos over 200 MB, largest first, each with its size and how sharp its picture is (4K, 1080p, 720p). None is chosen to start with: click the ones to delete. Making a video smaller is done in Trovolve on a phone, where it was filmed.',
          '**Screenshots** are pictures of a screen, found by their name or by a folder called Screenshots. All are chosen to start with: click one to keep it.',
          '**Bursts** are three or more shots taken within a second of each other by one camera. Trovolve keeps the sharpest shot of each burst, framed and marked **Keep**; click another shot to keep it instead. The other shots are deleted.',
          '**Blurry** are pictures whose sharpest part is still soft. All are chosen to start with: click one to keep it. A scan measures how sharp each picture is; until one has, Blurry says **Scan again to find blurry shots**.',
          '**Messaging apps** are the files WhatsApp and Telegram saved, in folders named after them. All are chosen to start with.',
          '**Select all** and **Select none** choose every file of the kind shown, or none.',
        ],
      },
      {
        heading: 'Deleting',
        tips: [
          'The button says how many files go: **Delete 12 files**, or for bursts **Delete 4 extra shots**. The question asked first names the files that go and, for bursts, the shot that stays.',
          'They go to **_Trovolve_Removed** like anything else, and the message after has **Undo**, which puts them back.',
          'A file marked keep in **Duplicates** or **Similar** is never listed here.',
          'On the free plan, the question says how many free removals are left today. Choose more than that and Trovolve says so, with **Delete … now** to delete the largest that fit.',
        ],
      },
    ],
  },
  {
    id: 'organize',
    only: COMPUTER,
    title: 'Organize: suggested folders',
    summary: 'Sorting a folder by place and date, with the map and the ways to narrow what is shown.',
    go: { label: 'Open Organize', place: 'organize' },
    keywords: ['sort', 'arrange', 'tidy', 'location', 'gps', 'year', 'month', 'trip', 'map', 'kilometres', 'miles', 'filter'],
    sections: [
      {
        steps: [
          'Switch to **Organize**. It starts with the folder you last scanned; **Choose root folder** takes any other. No scan is needed.',
          '**Clear results**, in the menu while Organize shows a folder’s files, puts that folder away with its search, filters and folders of your own, and Organize asks for a folder again. Nothing on the disk changes.',
          'Wait while the folder is read. Only the date and the place of each file are read. A large folder takes minutes the first time, and the files appear as they are read; after that it is quick.',
          'Choose how to group, on the right: **Group by** Place or Date, and **Then by** what each folder is split by inside. Place then Year gives folders such as Butte\\2022; Date then Place gives 2022\\Butte.',
          'For places, **Nearby places** decides whether places within 1, 5, 15, 25 or 50 miles of each other share a folder, and **Folder names** how a place’s folder is named. Choose kilometers instead under **Distances** in Settings.',
          'Each card is a folder that would be made. Click a card to see its files alone.',
        ],
        tips: [
          'Moving a suggested folder is part of Pro, which comes to this computer from a phone that bought it. On the free plan the suggestions and their previews show all the same.',
        ],
      },
      {
        heading: 'The map',
        tips: [
          'With the folders grouped by place, the map shows each as a dot. Click a dot to see that folder’s files.',
          'Click the map itself, or the button in its corner, to open it large. A dot clicked there shows its files and closes the large map.',
          'When a Google Maps key is set, the map is Google’s, with **Map** and **Satellite**; otherwise it is a plain map drawn by Trovolve. The **Map** switch above the settings hides it.',
        ],
      },
      {
        heading: 'Seeing only some of the files',
        tips: [
          'The search box finds files by name, place or month.',
          '**All**, **Not organized** and **No location**, above the files, filter them.',
          '**Photos & videos**, **Photos** and **Videos**, beside them, show both kinds or only one. The two work together: **Not organized** with **Videos** shows the videos still to sort. Each count says how many a click would show.',
          'The kind shown is also what the folders take. With **Photos** on, the suggested folders and your own folders move only pictures, and with **Videos** only videos; the rest wait for their turn. A Live Photo’s video is never moved on its own: it goes with its picture.',
          'A folder you picked is named above the files, with a × to let it go.',
          '**Clear all** puts away the folder, the search, the filter and the kind together, and shows every file again. **Photos & videos** alone shows both kinds again.',
          'Files with no place in their details are counted on the **No location** card. Group by date to sort those.',
        ],
      },
    ],
  },
  {
    id: 'own',
    only: COMPUTER,
    title: 'Organize: folders of your own',
    summary: 'Making your own folders, or using ones already there, and putting files in them.',
    go: { label: 'Open Organize', place: 'organize' },
    keywords: ['new folder', 'create folder', 'drag', 'drop', 'existing folder'],
    sections: [
      {
        steps: [
          'Under **Your folders**, press **New folder** and name it. It is made inside the folder being organized; **Change**, beside **Made inside**, makes it somewhere else. A \\ in the name makes a folder inside a folder, as in Trips\\Glacier 2022.',
          'Or choose **Folder already there** and pick one, such as a folder you made before: the files go straight into it. **›** shows the folders inside a folder, and **Up** the folder above.',
          'Select files and press the **Add here** button on the folder’s card (it says how many, as in Add 3 here), or drag files onto the card.',
          'Or the other way round: select files first, then press **New folder from these** in the bar under the files. The same window opens, and the selected files go in the folder you set up.',
        ],
      },
      {
        tips: [
          'Files in your own folders are left out of the suggested folders. A file can also be dragged onto a suggested folder.',
          'Nothing is moved by any of this. A new folder exists on disk only once you preview it and confirm.',
          'Nothing in a folder already there is replaced: a file arriving under a name it already has gets a number. A card says where its folder is when that is not a new folder in the folder being organized; hover over it for the whole path.',
          'What you have arranged but not carried out lives in the page, and is gone if the page is loaded again.',
        ],
      },
    ],
  },
  {
    id: 'move',
    only: COMPUTER,
    title: 'Organize: previewing and moving',
    summary: 'Checking a folder before anything moves, names that are already taken, and undoing.',
    go: { label: 'Open Organize', place: 'organize' },
    keywords: ['preview', 'copy', 'rename', 'name taken', 'clash', 'conflict', 'undo', 'history', 'recently moved'],
    sections: [
      {
        steps: [
          'Press **Preview** on a folder’s card. Every file it would take is shown; nothing has moved yet.',
          'Click a file to leave it where it is. **Leave all out** and **Include all** do it for every file.',
          'Change the **Folder name** if you like.',
          'Under **Goes to**, **Change** picks another place: make the new folder there, or put the files straight into the folder you pick.',
          'Choose **Move** or **Copy**. A copy leaves the originals where they are and uses that much more space.',
          'Leave the tick on to bring Live Photo videos and edit files along with their pictures.',
          'Press the **Move** button at the bottom right, which says how many files will go.',
        ],
      },
      {
        heading: 'Names already taken',
        text: [
          'Nothing is ever replaced. A file going where its name is taken gets a number, as in IMG_6435 (1).JPG. **Review each name** shows such a file beside the file that has its name, with what differs between them, and says when the two are the very same file.',
        ],
        tips: [
          'For each you choose **Add a number**, **Rename it** or **Leave it where it is**.',
          'The small bin under each of the two files deletes that file, for when they are the same or one is not wanted. It asks first, and says which of the two stays.',
          '**Space** flips between the two in the same spot; the arrow keys go to the previous or the next name.',
        ],
      },
      {
        heading: 'Undoing',
        tips: [
          'The message after a move has an **Undo** button.',
          'The **Recently moved** button, at the foot of the panel on the right, says how many moves can still be undone and opens a list of what was moved, copied or deleted, in Organize or in Clean up, each with **Undo**. Moved files go back under the names they had, and the folders made for them go again if that leaves them empty. **Done**, the **×** in its corner or **Esc** closes the list.',
          'A file whose old name has been taken since goes back with a number, as in IMG_6435 (1).JPG. One that is no longer where the move put it is left, and the message says so.',
          'An undo too happens in full or not at all: if one file cannot go back, none does, and the message says why.',
        ],
      },
    ],
  },
  {
    id: 'select',
    only: COMPUTER,
    title: 'Organize: selecting and deleting files',
    summary: 'Selecting one file, a range or all of them, and deleting what is selected.',
    go: { label: 'Open Organize', place: 'organize' },
    keywords: ['choose', 'pick', 'shift', 'select all', 'remove', 'trash', 'open file', 'viewer'],
    sections: [
      {
        tips: [
          'Click a file to select it; click it again to let it go.',
          'Hold **Shift** and click to select everything from the last file you clicked to this one.',
          '**Select all**, above the files, selects every file being shown, on every page; turned off, it lets them go. With a folder picked, a search or a filter on, that is only the files those leave.',
          'Double-click a file to open it in the computer’s own viewer or player.',
          'Hover over a file, or reach it with **Tab**, for its details: its name, when and where it was taken, its folder and its size. **Esc** puts them away.',
        ],
      },
      {
        heading: 'What can be done with the selection',
        text: [
          'A bar appears under the files as soon as one is selected.',
        ],
        tips: [
          '**New folder from these** puts the selected files in a folder of your own: a new one, wherever you choose, or one that is already there.',
          'The bin deletes the selected files. It asks first, then moves them to **_Trovolve_Removed** inside the folder being organized, with any Live Photo videos and edit files that belong only to them. **Undo** puts them back.',
          '**Clear** lets every file go.',
        ],
      },
    ],
  },
  {
    id: 'phones',
    only: COMPUTER,
    title: 'Backing up a phone to a computer',
    summary: 'Pairing a phone or tablet with this computer, where its backups go, and what Phones shows of each.',
    keywords: ['iphone', 'ipad', 'android', 'tablet', 'pair', 'connect', 'qr code', 'transfer', 'import', 'sync', 'wifi', 'wi-fi', 'back up', 'receive', 'trip folders', 'trips', 'rename', 'file names', 'name by date', 'computer name', 'forget', 'unpair', 'remove phone'],
    sections: [
      {
        text: [
          'Trovolve on an iPhone, iPad or Android phone backs up its photos and videos straight to Trovolve on this computer, over your own Wi-Fi, never through the internet; no one else on the network can read them. Both need to be on the same Wi-Fi, with Trovolve open on this computer.',
          'This computer waits for the phones paired with it whenever Trovolve runs. A phone it does not know is turned away, unless **Pair a phone** is open.',
        ],
      },
      {
        heading: 'Pairing a phone',
        steps: [
          'Open **Phones**, at the top left, and choose **Pair a phone**.',
          'It says what phones will call this computer. To give it a name of your own, such as “Office PC”, choose **Rename…**, type the name, and **Save**. Phones paired already learn the new name at their next backup.',
          'It shows a QR code, and under **Or enter this code on the phone**, a six-digit code. Both change every 5 minutes.',
          'On the phone, choose **Connect your computer**, then **Scan the code** and point the phone at the QR code: the two are paired at once.',
          'Or, on the phone, choose **Enter the code instead** and type the six digits. Both then show the same number: check that they match before pairing on each.',
          'Once paired, **Done** closes the window. The two know each other from then on, and do not ask again.',
        ],
      },
      {
        heading: 'Where backups go',
        tips: [
          'Each phone’s backups go to **Pictures/Trovolve**, in a folder named after the phone.',
          'Each file is checked by its SHA-256 checksum as it arrives, and only a file that arrived whole is kept. One whose name is taken in the folder arrives under a new name: nothing on this computer is replaced.',
          'Every time a phone backs up, this computer first checks the files it holds for it. One deleted or changed here is sent again by the phone; one moved between the phone’s folder and its trip folders is found where it is.',
          'When a phone asks, this computer moves a trip’s copies into a folder of their own in **Pictures/Trovolve**, or the folder you chose for that phone’s trips, named by the month and the trip’s name, such as **2026-07 Glacier National Park**. A name already taken there gets a number, and nothing is replaced. The move is listed in **Recently moved**, where **Undo** puts the copies back; either way they still count as backed up, and are not sent again.',
        ],
      },
      {
        heading: 'What Phones shows',
        tips: [
          '**Phones and tablets**, on the left, lists every phone paired with this computer, with when it last backed up. Choose one to see it.',
          '**Backup** shows a backup arriving, file by file, each with a tick once it matched its checksum, and otherwise when the last one was.',
          '**On this computer** says how many of its photos and videos are here; **Checked** that each matched its SHA-256 checksum; **Safe to delete on the phone** how many the phone could remove, and how much space that frees there.',
          '**Files from this phone** names the folder its backups go to. **Change…** sends them to another folder from then on; what is there already stays. **Trip folders** says where they are made and how many were made when the phone asked; its **Change…** makes them in another folder from then on.',
          'A phone’s hidden vault arrives encrypted, in a **Hidden** folder inside its folder. This computer cannot open those files: only the phone, or a new phone given its recovery key, can. Leave them where they are, so that a backup never sends them twice.',
          '**File names** says how the phone’s copies are named here: **As on the phone** (IMG_4821.HEIC) to start with. **Rename by date…** opens **Rename files by date**: choose **Date and time** (2026-03-02 14.31.07.HEIC) or **Date, time and place** (2026-03-02 14.31.07 Big Sky.HEIC), see the first few names now and after, and **Rename** them. Copies backed up from then on are named the same way. A Live Photo’s video takes its picture’s name; files with the same date and time get -2, -3 at the end; a file that does not say when it was taken keeps its name. Only the copies on this computer change, as one move listed in **Recently moved**, where **Undo** puts the names back. Renaming is part of Pro, which a phone’s Pro covers. Once named by date, **Change names…** also offers **As on the phone** for what arrives from then on.',
          '**Pause backups** turns the phone away until **Resume backups**. **Activity** lists what happened.',
          '**Forget this phone…** (or tablet), at the foot, asks first, then forgets it: it leaves the list and must be paired again to back up here. Its backups stay in its folder, and nothing is deleted here or on the phone. On the phone, photos backed up only here stop counting as safe to delete. If Pro on this computer came from that phone, this computer goes back to the free plan until another paired phone with Pro backs up. The phone learns of it the next time it connects, and asks to be paired again.',
          'Paired again, a phone carries on in the same folder: what is there already counts again, and nothing is sent twice.',
        ],
      },
    ],
  },
  {
    id: 'removed',
    only: COMPUTER,
    title: 'Removed files and the Recycle Bin',
    summary: 'Where deleted files wait, how to put one back, and sending them to the Recycle Bin or the Trash.',
    go: { label: 'Open Settings', place: 'settings' },
    keywords: ['trash', 'recycle bin', 'bin', 'deleted', 'restore', 'recover', 'get back', 'undelete', 'erase', 'free space', 'empty', 'delete permanently'],
    sections: [
      {
        text: [
          'Whatever you delete, in Clean up or in Organize, is moved to a folder called **_Trovolve_Removed** inside the folder it was in: the scanned folder, or the folder being organized. Each file keeps its place inside that folder, so that it can be found again.',
        ],
      },
      {
        heading: 'Putting a file back',
        tips: [
          'Press **Undo** in the message that says what was deleted, in Clean up or in Organize. Every file it took goes back where it was, and Clean up lists the copies in their groups again.',
          'Later, open **Recently moved** in Organize and press **Undo** beside the deletion. It lists what Clean up deleted too.',
          'Otherwise, move the file out of **_Trovolve_Removed** back to where it was, with the computer’s own file manager. Scan again for Clean up to list it once more.',
        ],
      },
      {
        heading: 'Moving them to the Recycle Bin',
        steps: [
          'Choose **Move to Recycle Bin** in the menu (**Move to Trash** on a Mac).',
          'A question says how many files go to the Recycle Bin and lists every **_Trovolve_Removed** folder that holds files. Nothing has gone yet.',
          'Press **Move to Recycle Bin**. A second, small question asks **Are you sure?**',
          'Press **Move to Recycle Bin** there. Only now do the files go.',
        ],
        tips: [
          'You can get them back from the Recycle Bin, or the Trash, until it is emptied. Undo in Trovolve can no longer bring them back.',
          'A folder on a drive with no Recycle Bin, such as a network share or, on Windows, a USB stick or memory card, is named in the first question in red: its files are erased for good, and cannot be got back.',
          'The second question has a switch, **Do not ask me this second time again**. **Settings**, in the menu, turns the question back on.',
          'A file that cannot be moved, one that is open in another program for instance, is left, and the message says so.',
        ],
      },
    ],
  },
  {
    id: 'plan',
    only: COMPUTER,
    title: 'The free plan and Pro',
    summary: 'How many files the free plan removes each day on this computer, and how Pro reaches it.',
    keywords: ['price', 'cost', 'pay', 'buy', 'purchase', 'subscription', 'upgrade', 'limit', 'daily', '50', 'premium'],
    sections: [
      {
        text: [
          'Trovolve is free. The free plan removes **50 photos and videos a day** on this computer, in Clean up and Organize alike. Each one you delete counts, once it has gone to **_Trovolve_Removed**. **Undo** brings it back, and gives back the removal the same day; the count starts again at midnight. Each device has its own 50.',
          'The plan beside the menu says how many are left today, as in **Trovolve Free · 38 left today**. A question before deleting says it too.',
        ],
      },
      {
        heading: 'Deleting more than are left',
        tips: [
          'Before you delete more than are left today, a question says how many you chose and how many are left.',
          '**Delete … now** deletes as many as are left: in Clean up, the groups of copies that free the most first; elsewhere, the largest files first. **Not now** deletes nothing.',
        ],
      },
      {
        heading: 'Trovolve Pro',
        tips: [
          'Pro has no daily limit. It is bought in Trovolve on an iPhone, iPad or Android phone, not on a computer.',
          'One purchase covers Trovolve on your phone, tablet and computer. This computer learns of it from the phone the next time the phone backs up here, and the plan beside the menu then says **Trovolve Pro**.',
          'Moving Organize’s suggested folders, by place or by date, is part of Pro. On the free plan you can still see every suggestion and preview it; **Move** says it is part of Pro. Folders of your own, from files you choose, move on any plan.',
        ],
      },
    ],
  },
  {
    id: 'menu',
    only: COMPUTER,
    title: 'Settings, theme, support and About',
    summary: 'What the menu holds besides the things described elsewhere.',
    go: { label: 'Open Settings', place: 'settings' },
    keywords: ['preferences', 'options', 'dark mode', 'light mode', 'system theme', 'version', 'privacy', 'terms', 'support', 'faq', 'licences', 'kilometers', 'miles', 'contact', 'email', 'help me', 'report a problem', 'errors'],
    sections: [
      {
        tips: [
          '**Theme** says the theme in use and opens beneath itself (or press the right arrow) to show **Dark**, **Light** and **Follow device setting**. Dark or Light keeps one; Follow device setting, what Trovolve starts on, follows the computer’s own light or dark setting, and changes when it does.',
          '**Support** opens beneath itself (or press the right arrow) to show **Errors** and the **Support page**. While there are errors, Support says how many.',
          '**Errors** lists everything that went wrong, with the time, until you clear it. While there are any, a red count shows at the top of the page too.',
          'The **Support page** opens the guide or the errors, and has **Email support**: an email to Trovolve’s support with its version and your system already in it, and the last three errors, if any. **Copy details** copies the same, for a message written elsewhere. Beside them are answers to common questions.',
          '**Settings** has the switch for the second question before moving files to the Recycle Bin, and **Distances**: miles, or kilometers, for Organize and its map. Settings are kept in this browser.',
          '**Help** opens this guide.',
          '**About Trovolve** shows the version of Trovolve you are running and who makes it, and holds the support document too, with answers to frequently asked questions (press a question to open its answer), the **Privacy policy**, the **Terms of use**, and the **Acknowledgements** of what others made that Trovolve uses.',
        ],
      },
    ],
  },
  {
    id: 'trouble',
    only: COMPUTER,
    title: 'If something looks wrong',
    summary: 'What the commonest puzzles mean, and what to do about them.',
    keywords: ['problem', 'error', 'not working', 'stuck', 'help', 'fix', 'broken', 'missing'],
    sections: [
      {
        heading: 'A red bar says the Trovolve service is not answering',
        text: [
          'The page is still open but the program behind it has stopped, so what is shown may be out of date. Start Trovolve again and press **Retry**.',
        ],
      },
      {
        heading: 'The Duplicates tab is empty after a scan',
        text: [
          'The scan found no copies, which is what you should see once they have all been removed. The tab offers a button to the similar shots when there are any.',
        ],
      },
      {
        heading: 'A scan sits at 0%',
        text: [
          'It is counting the files first, which takes a while for a large folder or a slow disk. The numbers start moving once it knows how many there are.',
        ],
      },
      {
        heading: 'Folder not found',
        text: [
          'Folders are those of this computer, by their full path. Use **Browse** to pick one. A disk that is not plugged in is not found either.',
        ],
      },
      {
        heading: 'A file was left where it is',
        text: [
          'It is open in another program, or it has changed since the scan. A file that cannot be moved stops the whole move or deletion, so nothing went and everything is where it was. Close the program, or scan again, and try once more. The reason is kept under **Errors**.',
        ],
      },
      {
        heading: 'A phone cannot find this computer',
        text: [
          'Trovolve must be open on this computer, and both must be on the same Wi-Fi. A phone not yet paired is turned away until **Pair a phone** is open in **Phones**. If scanning the QR code does not find it, try **Enter the code instead** on the phone.',
        ],
      },
      {
        heading: 'The map is a plain one',
        text: [
          'Google’s map needs a key, set when Trovolve is started, and a connection to Google. Without either, the plain map is shown; it works the same way.',
        ],
      },
    ],
  },

  // ——— On an iPhone, iPad or Android phone or tablet ———
  {
    id: 'phone-start',
    only: PHONES,
    title: 'Getting started',
    summary: 'The first launch, the four tabs, Settings, finding your way back, and the home screen widgets.',
    keywords: ['overview', 'introduction', 'welcome', 'first time', 'begin', 'setup', 'tabs', 'back', 'tablet', 'sidebar', 'rail', 'collections', 'side by side', 'widget', 'home screen',
      'shortcut', 'quick action', 'long press', 'press and hold', 'terms of use', 'keep a backup', 'accept'],
    sections: [
      {
        text: [
          'Trovolve frees up space and keeps your photos tidy. **Clean up** finds the copies of each photo or video and helps you remove all but the best one. **Organize** sorts your photos by where and when they were taken. **Backup** copies them to Trovolve on your own computer, and then shows what is safe to delete here.',
          'Everything happens on this device and on your own computer. Nothing is uploaded to Trovolve.',
        ],
      },
      {
        heading: 'The first launch',
        steps: [
          '**Get started**, on the welcome screen.',
                    '**Continue** asks the phone’s own question of which photos Trovolve may see. Allow them all: with only some, Trovolve checks only those, so most of Clean up and Safe to delete cannot work. If you said no, **Open Settings** goes to where access is turned on; come back, and Trovolve carries on.',
          '**Connect your computer** pairs this device with Trovolve on your Mac or PC, for backups. **Skip for now** leaves it for later, from **Backup** or **Settings**.',
          '**Keep a backup of photos you care about**: Trovolve asks before it removes or moves anything, but a mistake can still lose a photo or video. **Read the terms of use** shows them; **Accept and continue** accepts them and carries on. It is asked once for you, not on every device: if you accepted it on the computer you just paired with, it is not asked here, and accepting it here means the computer does not ask. On a device that had Trovolve before, it is asked the next time Trovolve opens.',
          'Trovolve then reads your library, saying how many photos and videos it has read and what it has found so far. **Stop** ends the scan, keeping what it read.',
          '**Scan complete** says how much space could be freed, and from what: duplicates, similar shots and large videos. **Continue**.',
          'On the free plan, with something to free, Trovolve Pro is offered once. **Continue with Free**, or the ×, carries on without it. The first launch ends in **Clean up**.',
        ],
      },
      {
        heading: 'Finding your way',
        tips: [
          'The tabs, **Library**, **Clean up**, **Organize** and **Backup**, are at the foot of the screen. Each keeps where you were in it; tap the tab you are on to go back to its first screen.',
          'The gear at the top right of each tab opens **Settings**. On a tablet, **Settings** is at the foot of the sidebar or the rail instead.',
          'Messages appear at the bottom of the screen. One that offers **Undo** stays a little longer; an error stays until you press OK, and is listed under **Errors** in Settings.',
        ],
      },
      {
        only: IOS,
        tips: [
          'Swipe from the left edge, or tap the back arrow, to go back from a screen opened over another.',
          'On an iPad the tabs are in a sidebar on the left, with **Trips**, **Needs fixing** and **Hidden** under **Collections**, and **Settings** at its foot; each opens beside the sidebar, in place of the tab. **Hide sidebar** gives the screen its whole width, with the tabs at the top and the gear back on each tab; **Show sidebar** brings it back.',
          'On an iPad the list and what you open from it sit side by side: in **Clean up**, the kinds Find lists beside the one chosen, the largest first; in **Organize**, the suggestions beside the one chosen; in **Trips**, the trips beside a map, the one chosen in a card with **Open trip**; in **Settings**, the list beside **Subscription**; in **Backup**, every location with its switch beside the page of the one chosen, where **Connect to server**, the photos **Choose photos to send** offers and what was sent show too. **Library** shows what is on your computer, **Trips** and **Year in review** in a row above the photos, eight across. While you swipe, a panel beside the photo says what is marked and what it frees, and your streak. Sheets open in the middle of the screen.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'The phone’s Back gesture or button goes back from a screen opened over another, and puts away a sheet.',
          'On a tablet the tabs are down the left side, with **Trips**, **Needs fixing** and **Hidden** under a line, and **Settings** at the foot; each opens beside them, in place of the tab.',
          'On a tablet the list and what you open from it sit side by side: in **Clean up**, the kinds Find lists beside the one chosen, the largest first; in **Organize**, the suggestions beside the one chosen; in **Trips**, the trips beside a map, the one chosen in a card with **Open trip**; in **Settings**, the list beside **Subscription**; in **Backup**, every location with its switch beside the page of the one chosen, where **Connect to server**, the photos **Choose photos to send** offers and what was sent show too. **Library** shows what is on your computer, **Trips** and **Year in review** in a row above the photos, eight across. While you swipe, a panel beside the photo says what is marked and what it frees, and your streak. Sheets open in the middle of the screen.',
        ],
      },
      {
        heading: 'Widgets and the app icon',
        text: [
          'Two widgets keep Trovolve on your home screen. **Storage** shows how full your device is, how much Clean up can free, and how backing up stands; tap it for **Free up space**, or **Back up now** to open **Backup** and start a backup. Before you pair a computer, its button says **Pair** and opens **Backup** to pair one. **Swipe to clean** shows how many of today’s photos are left to swipe, the last seven days and your streak; tap it to start swiping.',
          'The widgets show how things stood when you last left Trovolve, or when your device last let it back up or check its storage. Open Trovolve to bring them up to date.',
          'Press and hold the Trovolve icon for **Search photos**, **Start swipe** and **Back up now**. The widgets and the menu are free.',
        ],
      },
      {
        only: IOS,
        steps: [
          'To add a widget, press and hold an empty spot of your home screen until the apps jiggle, then tap **Edit** and **Add Widget**.',
          'Search for **Trovolve**, choose **Storage** or **Swipe to clean**, and tap **Add Widget**.',
        ],
      },
      {
        only: ANDROID,
        steps: [
          'To add a widget, press and hold an empty spot of your home screen, then tap **Widgets**.',
          'Find **Trovolve**, then press and hold **Storage** or **Swipe to clean** and place it on your home screen.',
        ],
      },
    ],
  },
  {
    id: 'library',
    only: PHONES,
    title: 'Library',
    summary: 'Every photo and video, by month: searching, trips, fixing dates, the hidden vault, viewing one, sharing it, choosing several, and what can be done with them.',
    keywords: ['photos', 'pictures', 'gallery', 'camera roll', 'view', 'select', 'choose', 'delete', 'send', 'filter', 'no location', 'videos', 'month',
      'year in review', 'recap', 'wrapped', 'stats', 'share card', 'brag', 'story', 'search', 'find', 'look for', 'favorite', 'heart', 'info', 'camera',
      'share', 'location', 'gps', 'privacy', 'metadata', 'exif', 'trips', 'travel', 'vacation', 'holiday', 'map', 'home', 'journey', 'trip folder',
      'wrong date', 'fix date', 'missing location', 'needs fixing', '1970', '2000', 'rename', 'file names', 'hidden', 'hide', 'vault', 'private', 'secret',
      'lock', 'face id', 'fingerprint', 'encrypted', 'recovery key', 'unhide'],
    sections: [
      {
        text: [
          '**Library** shows every photo and video, newest first, by month. The title says how many there are. A Live Photo is shown once, its video going with its picture.',
          'Tap one to see it whole, under the day it was taken, where and at what time. A video shows its details, but Trovolve does not play videos yet.',
        ],
      },
      {
        heading: 'Viewing one',
        tips: [
          '**Share** opens **Share photo**: see **Sharing without the place**, below.',
          '**Favorite** marks it a favorite in your phone’s own photos, or takes the mark away. The heart is filled while it is one.',
          '**Info** shows its name, when and where it was taken, the camera, its size, and where it is kept, with **Send** and the way to put it somewhere. Tap **Info** again to hide them.',
          '**Delete** asks first, naming what goes.',
        ],
      },
      {
        only: IOS,
        tips: [
          'The four are in the glass bar under the photo, as icons. The arrow at the top left, or a swipe from the left edge, goes back to Library.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'The four are in the bar at the foot, each with its name. The arrow at the top left, or Back, goes back to Library.',
        ],
      },
      {
        heading: 'Searching',
        text: [
          'Tap **Search photos**, at the top of Library, and type what you are looking for, then search. It knows places (“Butte”), months (“July”), years (“2023”) and what is in a photo (“dog”, “beach”), and you can mix them: “dog at the beach 2023”.',
          'What you searched for is shown as chips: **In photo**, **Place**, **Month** and **Year**. Under them, how many photos match, and the photos, the latest first. Tap one to open it.',
          'Before you search, **Recent** lists your last five searches and **Try** a few to start with. The × in the field clears it.',
          'Searching by place and date is free. Searching for what is in photos is part of Pro: on the free plan a card says so, with **Get Pro**, and shows the photos that match the place and date of your search.',
          'Search runs on this device, which names what is in each photo as it scans. Your photos and your searches are not uploaded.',
        ],
      },
      {
        only: IOS,
        tips: [
          'What is in each photo is named by Apple’s own image recognition on your iPhone or iPad. **Cancel** closes Search.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'What is in each photo is named by Google’s ML Kit on your phone, with the model Google Play services keeps; a phone without Google Play services finds photos by place and date only. The arrow, or Back, closes Search.',
        ],
      },
      {
        heading: 'Fix dates and places',
        text: [
          'The **Needs fixing** tile, beside **Trips**, opens **Fix dates and places**: how many photos and videos need a date or a place. Fixing is part of Pro.',
          '**Camera clock not set** lists photos dated on a camera’s first day (Jan 1, 1970, 1980 or 2000) or in the future. Choose the day they were really taken, then **Fix**: each keeps its time of day. Your phone asks before they change.',
          '**Missing location** lists photos with no place that were taken within 30 minutes of a photo that has one. **Add locations** gives each the place of the photo taken nearest in time, after your phone asks. Screenshots and photos saved from WhatsApp or Telegram are never given a place.',
          'After each fix, Trovolve says what changed, with **Undo**; the card keeps **Undo** until the next fix of that kind. Undo puts back what was there before, after your phone asks again.',
          '**File names on** your computer shows how the copies there are named. **Rename** has the computer name them by date and time (2026-03-02 14.31.07.HEIC), and those backed up from then on too. Files on your phone keep their names. To undo it, open Organize on the computer and choose **Recently moved**.',
        ],
      },
      {
        only: IOS,
        text: ['Photos keeps the new date and place with each photo; the file itself is not changed. Other apps that read Photos see them too.'],
      },
      {
        only: ANDROID,
        text: ['A JPEG, PNG or WebP picture has its own date and place written again in its file; the picture itself is untouched. A video, or a HEIC picture, gets its new date in Android’s own record, but cannot be given a place, so it is not listed under Missing location.'],
      },
      {
        heading: 'Hidden',
        text: [
          'The **Hidden** tile, beside **Needs fixing**, opens the hidden vault: photos and videos you keep out of your library, encrypted inside Trovolve and opened only after your phone checks it is you. The hidden vault is part of Pro.',
          '**Turn on Hidden** shows your **recovery key**: 32 letters and digits in groups of four. Write it down and keep it away from your phone. With it, a new phone paired with your computer brings your hidden items back; without it, nobody can open them, Trovolve included. **Show recovery key**, while Hidden is open, shows it again.',
          '**Add photos** opens your library to choose from. **Hide** encrypts each one into Hidden and checks the copy before anything leaves the library; then your phone asks before they leave it. They wait where your phone keeps what is deleted, for 30 days, as anything deleted does, and do not count toward the free plan’s daily removals.',
          'Hidden locks when you choose **Lock** and whenever Trovolve goes to the background. Locked, it shows only how many items it holds.',
          'Tap an item to see it. **Unhide** puts it back in your library with its date and place. **Delete** puts it back, then deletes it after your phone asks, where it can still be found for 30 days; on the free plan it counts toward the day’s removals.',
          'Backups carry hidden items to your computer still encrypted: the computer keeps them in a **Hidden** folder beside your phone’s photos and cannot open them. If you delete Trovolve, hidden items that are not backed up are deleted too, and Hidden says how many that would be.',
          'On a new phone, pair your computer first, then open Hidden and choose **Restore with a recovery key**. Type the key, and **Restore** brings your hidden items back from the computer after a backup.',
        ],
      },
      {
        only: IOS,
        text: ['Hidden opens with Face ID or Touch ID, or your iPhone’s passcode when they fail. An iPhone with no passcode cannot open Hidden. What is hidden, or deleted from Hidden, waits in **Recently Deleted** in Photos. The recovery key is kept in your iPhone’s Keychain, never in a backup of the phone.'],
      },
      {
        only: ANDROID,
        text: ['Hidden opens with your fingerprint or face, or your phone’s PIN, pattern or password. What is hidden, or deleted from Hidden, waits in the bin. The recovery key is sealed by Android’s own Keystore, and hidden items are left out of Google’s backup of the app.'],
      },
      {
        heading: 'Trips',
        text: [
          'The **Trips** tile under the search field opens **Trips**: the photos and videos you took away from home, grouped by place and date, the latest trip first.',
          'A trip is at least 10 photos and videos taken more than 30 miles from home, with no more than two days between them. A photo taken at home ends a trip. Each is named after the landmark or town where you took the most, or two towns when you spent time in both.',
          'To call a trip something of your own, open it and choose **Rename**, type the name, and **Save**. Its folder on your computer takes the name too. **Use Trovolve’s name** gives it Trovolve’s again.',
          'Home is the town where you took photos on the most days, and the note at the top of Trips names it. **Change** lets you choose another town among those in your photos, or go back to **Where most photos are taken**.',
          '**List** shows each trip with its dates and how many photos and videos it has. **Map** shows each trip as a pin with its count, beside **Home**; tap a pin to open the trip. The map is drawn on your phone, and where your photos were taken never leaves it.',
          'Tap a trip to see its cover, its dates and what you took each day. Tap a photo to view it.',
          'Once you have paired a computer, **Make a folder on** your computer puts the trip’s copies there in a folder of their own, named by the month and the place, such as **Pictures/Trovolve/2026-07 Glacier National Park**. Anything of the trip the computer does not have yet is sent first. Nothing changes on your phone. When it is done, Trovolve says where the folder is, and the trip says **Folder created**.',
          'Seeing your trips, the map and each trip’s photos is free. Making a trip’s folder on your computer is part of Pro: on the free plan, **Make a folder on** opens **Trovolve Pro** first.',
        ],
      },
      {
        heading: 'Sharing without the place',
        text: [
          '**Share**, under a photo, opens **Share photo**. **Remove location**, on to start with, leaves where it was taken out of the copy you share; **Remove camera details** leaves out the camera’s model, lens and exposure. **Share** hands the copy to your phone’s share sheet; **Not now** puts it away.',
          'The photo in your library keeps its location and details. Only the shared copy changes.',
          'Whether **Remove location** starts on is set by **Remove location when sharing**, under **Privacy** in Settings.',
        ],
      },
      {
        only: IOS,
        tips: [
          'A photo keeps its kind, HEIC or JPEG; a video is copied as it is, not made smaller.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'A JPEG, PNG or WebP keeps its kind; any other picture, such as a HEIC, is shared as a JPEG. A video is copied as an MP4, as it is, not made smaller; the copy carries no camera details either way.',
        ],
      },
      {
        heading: 'Seeing only some',
        tips: [
          '**All**, **Not organized**, **No location** and **Videos**, under the title, each show only those, with how many there are.',
          'Once this device backs up to a computer, **On** the computer (by its name) and **Waiting** show what is there already and what is still to go. A tick on a photo says it is on the computer, and the foot of the list says when the computer last checked them.',
        ],
      },
      {
        only: IOS,
        tips: [
          '**Not organized** shows the photos in no album yet.',
          'If you let Trovolve see only some of your photos, a note says so, and **Choose more** opens the phone’s own picker. Copies among the others are not found.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          '**Not organized** shows the photos in no folder yet, and those still where the camera put them, in **DCIM/Camera**.',
          'If you let Trovolve see only some of your photos, a note says so, and **Choose more** lets you choose others. Copies among the others are not found.',
        ],
      },
      {
        heading: 'Choosing several',
        steps: [
          'Tap **Select**, then tap each photo or video you want. The title says how many are chosen.',
          'The bar at the foot then offers **Send**, the way to put them somewhere, and **Delete**.',
        ],
      },
      {
        only: IOS,
        tips: [
          '**Add to album** lists your albums, or **+ New album…** makes one. A name already taken in the album gets a number: nothing is replaced.',
          '**Cancel** stops choosing.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          '**Move to folder** lists your folders, or **+ New folder…** makes one in **Pictures**. A name already taken in the folder gets a number: nothing is replaced.',
          'The × at the top, or Back, stops choosing.',
        ],
      },
      {
        heading: 'Sending and deleting',
        tips: [
          '**Send** backs up the chosen photos and videos to your computer now, and opens **Backup** to show them going. It copies them; nothing leaves this device.',
          '**Delete** asks first, naming what goes, and the message after says how many went. Moves have **Undo**; deletions do not.',
        ],
      },
      {
        only: IOS,
        tips: [
          'What you delete goes to the phone’s **Recently Deleted**, after iOS asks too, and waits there for 30 days. It is counted toward the day’s free removals. To bring it back, open **Recently Deleted** in Photos.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'What you delete goes to the phone’s bin, after Android asks too, and waits there for 30 days. It is counted toward the day’s free removals. To bring it back, open the bin in your gallery app.',
        ],
      },
      {
        heading: 'Once your computer has them',
        text: [
          'When photos are on your computer, a card at the top of Library says how many, and how much space removing them here would free. **Review** opens **Safe to delete**; the × hides the card. See **Backing up to your computer**.',
        ],
      },
      {
        heading: 'Year in review',
        text: [
          'In December, and through January, a card at the top of Library offers **Year in review**: your year in photos, made on the device. **Watch** opens it.',
          'Five cards, one after another: how many photos and videos you took; your trips, with the one you took the most photos on; your busiest month, with a bar for each month; what you cleaned up, how much space it freed, your longest swipe streak and how many days you swiped; and the year in a line.',
          'Tap the right of the screen to go on and the left to go back. The cross, or Back, closes it.',
          '**Share** hands the card showing, as a picture, to your phone’s share sheet. No photos are on these cards.',
          'Space freed and items removed count from the update that brought Year in review; before it, items removed come from the free plan’s daily counts.',
        ],
      },
      {
        heading: 'Sharing what you freed',
        text: [
          'After **Safe to delete**, after deleting in **Swipe to clean**, and after compressing videos, Trovolve offers a share card: “I freed 28.1 GB with Trovolve”. Tap it, or **Share**.',
          '**Share your result** shows the card, **Square** or as a **Story**, with or without your streak and how many items. **Share** hands it to your phone’s share sheet; **Not now** puts it away. None of your photos are on the card.',
        ],
      },
    ],
  },
  {
    id: 'phone-cleanup',
    only: PHONES,
    title: 'Clean up',
    summary: 'Swipe to clean, a set of photos a day; what Find finds, from copies and similar shots to large videos, screenshots, bursts and blurry shots; compressing videos; Free up space and low-storage alerts.',
    keywords: ['duplicates', 'copies', 'copy', 'same photo', 'twice', 'dupes', 'similar', 'burst', 'compare', 'keep', 'original', 'free space', 'scan again', 'swipe', 'streak', 'daily', 'tinder', 'left', 'right',
      'screenshot', 'large videos', 'big videos', 'blur', 'out of focus', 'whatsapp', 'telegram', 'storage', 'full', 'space', 'compress', 'shrink', 'smaller', 'hevc',
      'notification', 'alert', 'running out'],
    sections: [
      {
        text: [
          '**Clean up** says at the top how much space it has found to free, with the phone’s storage as a bar. Then come how many free removals are left today, on the free plan, with **Get Pro**; **Swipe to clean**; and **Find**.',
          '**Find** lists what Trovolve found, largest first, each with how many there are to delete and how much space they take: **Large videos**, **Duplicates**, **Screenshots**, **Bursts**, **Similar shots** and **Blurry**. Tap one to open it.',
        ],
      },
      {
        only: ANDROID,
        text: ['Find also lists **Messaging apps**: the photos and videos WhatsApp and Telegram saved in their folders.'],
      },
      {
        only: IOS,
        text: ['Find also lists **Messaging apps** when WhatsApp or Telegram saved photos in your library.'],
      },
      {
        heading: 'Swipe to clean',
        steps: [
          'Each day brings a set of up to 50 photos and videos, picked at random from those you have never swiped. The set stays the same all day, and a photo you kept or deleted never comes back in another.',
          'Tap **Start**, or **Continue** where you left off. One photo shows at a time, with when and where it was taken, what it is and its size, and **On** your computer when it is backed up there.',
          'Swipe left, or tap **Delete**, to mark it to delete; swipe right, or tap **Keep**, to keep it. **Undo**, at the top right, takes back the last one. Nothing is deleted while you swipe.',
          'At the foot, what you have marked so far, and how much space it would free.',
          'When the set is through, or you close it with something marked, the summary shows what is marked and how much it frees, your streak, and how many you reviewed, kept and marked. Tap any photo marked to keep it after all.',
          'Tap **Delete** and the number of items. Everything marked is deleted together, after the phone’s own question, and the summary then says how much space was freed.',
        ],
        tips: [
          'The streak counts the days in a row you swiped; the dots show the last seven days.',
          'Swipe to clean never stops you to sell anything. On the free plan the foot says how many free removals are left today; once you have marked more than that, it says the rest wait.',
          'If more are marked than the free plan has left today, the summary says so: **Delete … now** deletes the largest that fit, and the rest stay marked for tomorrow. **Delete all … with Pro** deletes them all once Pro is bought.',
        ],
      },
      {
        only: IOS,
        tips: [
          'What Swipe to clean deletes goes straight to the phone’s **Recently Deleted**, which keeps it for 30 days; it does not wait in Trovolve Removed, and Trovolve’s Undo cannot bring it back.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'What Swipe to clean deletes goes straight to the phone’s bin, which keeps it for 30 days; it does not wait in Pictures/Trovolve Removed, and Trovolve’s Undo cannot bring it back.',
        ],
      },
      {
        heading: 'Large videos, screenshots, bursts and blurry shots',
        tips: [
          '**Screenshots** and **Blurry** show their pictures in a grid, all chosen to start with: tap one to keep it. **Deselect all** and **Select all** are at the top right. **Messaging apps** works the same way.',
          '**Blurry** pictures are those whose sharpest part is still soft. A scan measures how sharp each picture is; until one has, Blurry says **Scan again to find blurry shots**.',
          '**Bursts** shows each burst with its shots, the one kept framed and marked **Keep**: Trovolve keeps the sharpest. Tap another shot to keep it instead. **Delete** and the number of extra shots deletes the rest.',
          '**Large videos** lists the videos over 200 MB, largest first, with where and when each was taken, how sharp its picture is (4K, 1080p, 720p), its size, and about what a smaller copy would take. Tap one to compress it or delete it.',
          'The button at the foot says how many go, and on the free plan how many free removals are left today.',
        ],
      },
      {
        heading: 'Compressing large videos',
        steps: [
          'In **Large videos**, tap a video. **Compress video** shows the original beside the copy, with about how much space the copy would save.',
          'Choose how small: **High** keeps the video’s own size; **Balanced** makes it 1080p; **Small**, 720p. All are HEVC, which keeps a video sharp in far less space.',
          'Tap **Compress**. Trovolve makes a smaller copy on the phone, with the original’s date and place, and shows how far it has got; **Cancel** stops it.',
          'Then the phone asks before the original goes, and it waits in the phone’s bin for 30 days. Say no and the original and the copy are both kept.',
        ],
        tips: [
          '**Compress all**, at the top of Large videos, compresses every video worth it at **Balanced**, one after another, then asks once about all the originals.',
          'The saving is worked out from each video’s length and size before it is compressed, so it is said as “about”; the message after says what was really saved.',
          'Compressing is part of **Trovolve Pro**, and takes nothing from the day’s free removals. On the free plan, **Compress** shows what Pro adds.',
          'A video already about as small as it gets at a quality says so, and **Delete this video instead** deletes it.',
        ],
      },
      {
        heading: 'Low-storage alerts',
        text: [
          'Turn on **Low-storage alerts**, under **Alerts** in Clean up or under **Clean up** in Settings, and Trovolve sends a notification when less than a tenth of the phone’s storage is free, at most once a week, saying how much **Free up space** would free. Tap it to open Free up space.',
          'The first time you turn it on, the phone asks whether Trovolve may send notifications. Said no, the switch stays off; turn notifications on for Trovolve in the phone’s settings, then turn the switch on again.',
        ],
      },
      {
        only: ANDROID,
        tips: ['The alert has **Review**, which opens Free up space, and **Not now**, which puts it away.'],
      },
      {
        only: IOS,
        tips: ['iOS decides when Trovolve may look in the background, so an alert can come later than the storage ran low; it also comes when you open Trovolve.'],
      },
      {
        heading: 'Free up space',
        steps: [
          'Tap the storage bar at the top of Clean up.',
          'It says how full the phone is, and how much the three quickest kinds to check would free: **Duplicates**, **Screenshots** and **Blurry**. A photo that is two of them counts once.',
          'Untick any you want to keep, then tap **Delete** and the number of items.',
        ],
        tips: ['Large videos and similar shots are worth a look one by one, so they are in Clean up, not here. Low-storage alerts come at most once a week.'],
      },
      {
        only: IOS,
        tips: [
          'What these screens and Free up space delete goes straight to the phone’s **Recently Deleted**, after the phone’s own question, and waits there for 30 days. It does not wait in Trovolve Removed, and Trovolve’s Undo cannot bring it back.',
          'Each counts toward the day’s free removals. Choose more than are left and Trovolve says so, with the choice to delete the largest that fit.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'What these screens and Free up space delete goes straight to the phone’s bin, after the phone’s own question, and waits there for 30 days. It does not wait in Pictures/Trovolve Removed, and Trovolve’s Undo cannot bring it back.',
          'Each counts toward the day’s free removals. Choose more than are left and Trovolve says so, with the choice to delete the largest that fit.',
        ],
      },
      {
        heading: 'Duplicates and similar shots',
        text: [
          '**Duplicates** are copies of the same photo or video; **Similar shots** are shots that look alike, such as the frames of a burst. Each row is a group, with two of its pictures, the file that stays and how much there is to delete.',
          'In each group one stays: the **Original** for copies, the best copy, with the most detail; the **Best shot** for similar shots.',
        ],
      },
      {
        heading: 'Reviewing a group',
        steps: [
          'Tap a group. The original is shown beside a copy, with where each is kept, **What differs** between them and **The same in both**.',
          'With more than one copy, **Copy 1**, **Copy 2** and so on show each in turn.',
          'Tap either picture to see it full screen. Tap again to flip between the two in the same spot; with several copies, the arrows go to the previous or next one.',
          '**Delete** and the copy’s name removes the copy shown, after a question naming the file that goes and the file that stays.',
          '**Keep this copy too** marks a copy to keep, which **Delete all duplicates** then skips. **Marked keep. Tap to unmark** takes the mark off.',
        ],
      },
      {
        heading: 'All at once',
        tips: [
          'At the top of **Duplicates**, **Delete all duplicates** deletes every copy in every group, keeping the best copy of each, after a question saying how many go and how much they take.',
          'Similar shots are different pictures, so they are never deleted all at once: go through them a group at a time.',
          'The foot of Clean up says when Trovolve last read your library, and **Scan again** reads it again. A scan reads only what has changed since.',
        ],
      },
      {
        heading: 'Good to know',
        tips: [
          'After **Delete all duplicates**, the message names one of the originals that stay, and where it is.',
          'The last copy of a photo is never deleted as a duplicate.',
          'When nothing is left, Clean up says so, and where what you deleted waits.',
        ],
      },
      {
        only: IOS,
        tips: [
          'What you delete goes to the phone’s **Recently Deleted**, after iOS asks too, and waits there for 30 days. Trovolve cannot take anything back out of it, so the message after a deletion has no **Undo**.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'What you delete goes to the phone’s bin, after Android asks too, and waits there for 30 days. Trovolve cannot take anything back out of it, so the message after a deletion has no **Undo**.',
        ],
      },
    ],
  },
  {
    id: 'phone-organize',
    only: PHONES,
    title: 'Organize',
    summary: 'Suggestions by place and date, previewing one, names already taken, moving and undoing.',
    keywords: ['sort', 'arrange', 'tidy', 'albums', 'folders', 'location', 'place', 'year', 'month', 'trip', 'map', 'preview', 'move', 'undo', 'recently moved', 'miles', 'kilometres'],
    sections: [
      {
        text: [
          '**Organize** suggests where to put what is not sorted yet, by **Place** or by **Date**. What you move drops out of the suggestions.',
          'Organize is part of Pro. On the free plan you can still see every suggestion, the map and each preview with their real counts; **Move** opens **Trovolve Pro** first. Choosing photos yourself in **Library** and putting them somewhere stays free.',
        ],
      },
      {
        only: IOS,
        text: [
          'On an iPhone or iPad, a suggestion is an album. Only photos in none of your albums are suggested; your own albums are left as they are. A photo is kept once, so it can be moved but not copied.',
        ],
      },
      {
        only: ANDROID,
        text: [
          'On Android, a suggestion is a folder, made in **Pictures** unless you choose another place. Android keeps photos only inside DCIM, Pictures or Movies. Only photos in no folder of yours yet, and those still in **DCIM/Camera**, are suggested. A photo can be moved on the phone, not copied; to copy photos, send them to a computer.',
        ],
      },
      {
        heading: 'The suggestions',
        tips: [
          'By place, each suggestion is a place, with some of its photos, how many and how large, and when they were taken. By date, each is a year, with the places in it.',
          '**Options** sets how: **Group by**; **Nearby places**, **Combine** within 1, 5, 15, 25 or 50 miles (or kilometers, as **Distances** in Settings says) or **Keep separate**; names after the **Landmark or area** or the **Town**; what goes inside each, by **Year**, **Month** or **Nothing**; and whether to sort **Photos & videos**, **Photos** or **Videos**.',
          'The map button at the top shows the suggestions as dots on a plain map; tap a dot to see that suggestion under the map. The map is drawn on this device, and no position is sent anywhere. The list button goes back.',
          'Photos with no location stay where they are when grouping by place. The row that counts them opens Library’s **No location**.',
        ],
      },
      {
        heading: 'Previewing and moving',
        steps: [
          'Tap **Preview** on a suggestion. Nothing moves yet.',
          'Change its name if you like, and **Change**, beside **Made inside**, to choose where it is made.',
          'Its files are shown by year, month or place. Tap any file to leave it where it is; it shows **Stays**. **Show more** shows more of them.',
          'Tap **Move**, which says how many files go. The message after has **Undo**, and the suggestion shows **Moved**.',
        ],
      },
      {
        heading: 'Names already taken',
        tips: [
          'Nothing is ever replaced. A file arriving where its name is taken gets a number, as in IMG_4299 (1).HEIC, and the preview says how many names are taken.',
          '**Review each name** shows each such file, **Going in**, beside the one **Already there**, and says when the two are byte for byte the same file. For each, choose **Add a number**, **Give it another name** or **Leave it where it is**; **Delete** under either removes that one, after a question saying which stays. **Next name** goes on; **Done** ends it.',
        ],
      },
      {
        heading: 'Your own, and undoing',
        tips: [
          '**Recently moved** lists what was moved, in Organize or Library, each with **Undo**. Files go back under the names they had. Deletions go to the phone’s own bin and are not listed.',
        ],
      },
      {
        only: IOS,
        tips: [
          'Under **Your albums**, **New album** names an album of your own. Put photos in it from Library, with **Add to album**.',
          'An album left empty by **Undo** stays in Photos until you delete it there: iOS asks you before an app deletes an album.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'Under **Your folders**, **New folder** names a folder of your own. Put photos in it from Library, with **Move to folder**.',
          'Android asks you once before each set of photos is moved.',
        ],
      },
    ],
  },
  {
    id: 'backup',
    only: PHONES,
    title: 'Backing up to your computer',
    summary: 'Pairing with your computer, backing up over Wi-Fi with each file checked, and then freeing space here.',
    keywords: ['back up', 'transfer', 'sync', 'copy to computer', 'pc', 'mac', 'windows', 'pair', 'connect', 'qr code', 'wifi', 'wi-fi', 'send', 'automatic', 'safe to delete', 'free space', 'storage', 'forget', 'unpair', 'disconnect computer'],
    sections: [
      {
        text: [
          'Trovolve backs up your photos and videos straight to Trovolve on your own Mac or PC, over your own Wi-Fi, never through the internet; no one else on the network can read them. Both need to be on the same Wi-Fi, with Trovolve open on the computer.',
        ],
      },
      {
        heading: 'Connecting your computer',
        steps: [
          'On the computer, open **Phones** and choose **Pair a phone**. It shows a QR code and a six-digit code.',
          'Here, choose **Connect your computer** in **Backup**, or in **Settings**, under **Locations**, tap **Computer**.',
          '**Scan the code** and point the camera at the QR code: the two are paired at once.',
          'Or choose **Enter the code instead**, type the six digits and tap **Pair**. Both then show a number: tap **It’s the same number** only if the computer shows the same one.',
          'Once paired, Trovolve says where on the computer your backups go: **Pictures/Trovolve**, in a folder named after this device. The folder is changed on the computer, in **Phones**.',
        ],
      },
      {
        heading: 'Forgetting your computer',
        tips: [
          'In **Settings**, under **Locations**, tap the computer, then **Forget this computer**. After the question, this device stops backing up to it until you pair them again.',
          'The backups on the computer stay there; nothing is deleted on either. Photos backed up only on the computer stop counting as safe to delete.',
          'Trovolve tells the computer, so that it forgets this device too. If the computer is not found on this Wi-Fi, it still lists this device: forget it there too, in **Phones**.',
          'If the computer forgot this device first, **Backup** says so, and offers **Connect your computer** to pair them again. Paired again, backups carry on in the same folder, and nothing is sent twice.',
        ],
      },
      {
        heading: 'Backing up',
        tips: [
          '**Backup** says how many new photos and videos are waiting. **Back up now** sends only what is not on the computer yet, with each Live Photo’s video.',
          'Each file is checked by its SHA-256 checksum as it arrives, and only a file that arrived whole is kept. Nothing on the computer is replaced: a name already taken there gets a new one.',
          '**Pause** stops a backup part way; **Resume** carries on from there.',
          '**Send now**, **Choose photos to send**, opens Library to choose some; **Send** then backs up just those.',
        ],
      },
      {
        heading: 'Automatic backup (Pro)',
        tips: [
          '**Back up automatically** backs up by itself; **Only while charging** waits for the charger; with **Include videos** off, videos stay behind, though a Live Photo’s video always goes with its picture.',
          'Backing up yourself, with **Back up now** or **Send now**, stays free.',
        ],
      },
      {
        only: IOS,
        tips: [
          'An iPhone or iPad backs up by itself whenever Trovolve is open and the computer is on the same Wi-Fi, and with Trovolve closed when iOS chooses to wake it: usually while it is charging on Wi-Fi, often at night. iOS gives it a few minutes each time; what is left goes the next time.',
          'With Trovolve closed it backs up only on Wi-Fi, never over mobile data or a hotspot. If you swipe Trovolve away in the app switcher, iOS stops waking it until you open it again.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'An Android phone backs up by itself on Wi-Fi about once an hour, with Trovolve closed too, and whenever it is open.',
        ],
      },
      {
        heading: 'Safe to delete',
        tips: [
          'Once photos are on the computer, **Review**, on Library’s card, opens **Safe to delete**. It lists only photos and videos whose every file matched its copy on the computer at the last backup, and shows how much of this device’s storage removing them frees.',
          'Under **Keep on this** device, **Favorites** leaves your favorite photos here, and **Last 30 days** what you took lately. Turn one off to remove those too.',
          '**Remove** removes them, the largest first, after the phone’s own question. The copies on the computer stay. **Space freed** then shows the storage before and now.',
          'On the free plan each counts toward the day’s free removals; past them, **Remove the largest now** removes as many as are left today.',
        ],
      },
      {
        only: IOS,
        tips: [
          'Removed photos go to **Recently Deleted**, which keeps them for 30 days. If iCloud Photos is on, removing them here also removes them from iCloud and your other Apple devices; the copies on the computer are not affected.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'Removed photos go to the phone’s bin, which keeps them for 30 days.',
        ],
      },
      {
        heading: 'If a backup stops',
        tips: [
          'If the phone or the computer hears nothing from the other for a minute, as when the phone loses the Wi-Fi or its screen locks, the backup stops and says why. What arrived whole is kept; **Try again** carries on.',
        ],
      },
      {
        only: IOS,
        tips: [
          'A photo kept only in iCloud is fetched first, which can take a while; the computer is told meanwhile, and waits.',
        ],
      },
    ],
  },
  {
    id: 'locations',
    only: PHONES,
    title: 'Cloud storage and your own locations',
    summary: 'Backing up to Google Drive, OneDrive, Dropbox or a server of your own, beside or instead of your computer.',
    keywords: ['cloud', 'google drive', 'onedrive', 'dropbox', 'server', 'nas', 's3', 'amazon', 'azure', 'backblaze', 'wasabi', 'webdav', 'nextcloud', 'sftp', 'ssh', 'location', 'locations', 'sign in', 'rename', 'send to', 'check all backups'],
    sections: [
      {
        text: [
          'Each place that keeps a copy of your photos and videos is a **location**: your computer, Google Drive, OneDrive, Dropbox, and any server you connect. They are set up in **Settings**, under **Locations**, and Trovolve backs up to every one that is on.',
          'Trovolve never sees your password: signing in happens on the service’s own page, and Trovolve may reach only its own folder there. Your photos go only to the locations you turn on, never to Trovolve.',
        ],
      },
      {
        heading: 'Turning on cloud storage',
        steps: [
          'In **Settings**, under **Locations**, turn on **Google Drive**, **OneDrive** or **Dropbox**.',
          'The service’s sign-in page opens. Sign in and allow Trovolve; you come back here, and the location is on.',
          'Backups go to **My Drive/Trovolve** on Google Drive, and **Apps/Trovolve** on OneDrive and Dropbox.',
        ],
        tips: [
          'Turning a location off keeps you signed in, and turning it on again backs up there at once. Nothing there is deleted.',
        ],
      },
      {
        heading: 'Connect to server (Pro)',
        steps: [
          'In **Settings**, under **Locations**, tap **Connect to server…**.',
          'Choose the kind: **Amazon S3**, **S3-compatible** (Backblaze B2, Wasabi, Cloudflare R2, MinIO and the like), **Azure Blob**, **WebDAV** (Nextcloud, ownCloud and most NAS drives) or **SFTP** (any computer you reach over SSH).',
          'Give it a **Name** of your own, and fill in how to reach it: the bucket or container, the address, and the keys or password.',
          'Tap **Connect**. Trovolve lists the folder, writes a test file, reads it back, compares its checksum and deletes it. Only then is the location added and turned on; if something is wrong, **Could not connect** says what to change.',
        ],
        tips: [
          'Use a key that reaches only the bucket or folder Trovolve uses, as the screen suggests.',
          'SMB shares are not offered: reach a NAS by WebDAV or SFTP instead.',
        ],
      },
      {
        only: IOS,
        tips: [
          'Keys and passwords are kept in the Keychain on this device and are sent only to their location.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'Keys and passwords are kept in the Android Keystore on this device and are sent only to their location.',
        ],
      },
      {
        heading: 'A location’s own page',
        tips: [
          'Tap a location in **Settings** for its page: how it stands, its storage where the service says, its folder, how many copies are checked, the last backup and the last check.',
          'A server you connected shows its type and how it is reached, its keys masked. **Rename** changes its name at any time; **Edit** changes how it is reached, and is checked again before it is saved.',
          '**Turn off** stops backing up there. **Disconnect** signs out of a cloud service; **Remove location** removes a server; **Forget this computer** forgets the computer until you pair them again. None of them deletes anything there, but photos backed up only there stop counting as safe to delete.',
        ],
      },
      {
        heading: 'Backing up and checking',
        tips: [
          '**Backup** lists the locations that are on, with how far each has got. **Back up now** backs up to them all; **Pause** stops, and what is there stays there.',
          'A copy counts as backed up only when it is checked: by the checksum the location keeps of it, or, where it keeps none (WebDAV and SFTP), by reading it back once. **Check all backups** checks every copy again.',
          'Under **Safe to delete** in Settings, **Checked on** sets whether a photo counts as backed up once its copy is checked on **Any location** or on **2 locations**.',
        ],
      },
      {
        heading: 'Sending to a location',
        steps: [
          'In Library, choose some photos and tap **Send**.',
          'With other locations on, **Send to** asks where: your computer, or a location. The one chosen last is offered first.',
          'Tap **Send**. Once every copy is checked, **Sent** says how many arrived and offers to delete them from this device; the copies at the location stay. Any that did not match stay here.',
        ],
      },
      {
        only: IOS,
        tips: [
          'Deleted photos go to **Recently Deleted** for 30 days, after iOS asks you. If iCloud Photos is on, deleting them here also deletes them from iCloud and your other Apple devices.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'Deleted photos go to the phone’s bin for 30 days, after Android asks you.',
        ],
      },
      {
        heading: 'The free plan and Pro',
        tips: [
          'The free plan always backs up to your computer, within the day’s free removals, and to one other location besides. Until a backup has gone to that one, turning on another offers Pro, or **Use … instead**, which turns the first off and the new one on.',
          'Once a backup has gone to it, that other location is the free plan’s for good: it cannot be turned off, signed out of or removed, and another location comes only with Pro. Your computer can still be turned off and on.',
          'Pro backs up to as many locations as you like, and adds **Connect to server**. If Pro ends, your computer stays as it was, of the others the one used last stays on, and nothing is deleted anywhere.',
          'Deleting after a send counts toward the day’s free removals.',
        ],
      },
    ],
  },
  {
    id: 'phone-removed',
    only: PHONES,
    title: 'Deleted photos',
    summary: 'Where deleted photos go, how to get them back, and what waits in Trovolve Removed from before.',
    keywords: ['trash', 'bin', 'recently deleted', 'deleted', 'restore', 'recover', 'get back', 'undelete', 'erase', 'free space', 'empty', 'trovolve removed', 'delete permanently'],
    sections: [
      {
        only: IOS,
        text: [
          'Whatever you delete in Trovolve, in Library, Clean up, Swipe to clean, Organize or Safe to delete, goes to the phone’s **Recently Deleted**, after iOS asks you too. It waits there for 30 days, then iOS erases it.',
          'To bring a photo back, open **Recently Deleted** in Photos and recover it there. Trovolve cannot take anything out of Recently Deleted, so its messages after a deletion have no **Undo**.',
        ],
      },
      {
        only: ANDROID,
        text: [
          'Whatever you delete in Trovolve, in Library, Clean up, Swipe to clean, Organize or Safe to delete, goes to the phone’s bin, after Android asks you too. It waits there for 30 days, then Android erases it.',
          'To bring a photo back, open the bin in your gallery app and restore it there. Trovolve cannot take anything out of the bin, so its messages after a deletion have no **Undo**.',
        ],
      },
      {
        heading: 'What waits in Trovolve Removed from before',
        text: [
          'Before this version, Trovolve moved what you deleted into Trovolve Removed, where Undo could still bring it back. Nothing new goes there now.',
        ],
        steps: [
          'The first time you open this version, a question says how many items still wait there and offers to move them. Tap **Move**; the phone asks too.',
          'Tap **Not now** to leave them. In **Settings**, **Trovolve Removed** then says how many wait, and **Move to …** offers it again.',
        ],
        tips: [
          'Moving them does not count toward the day’s free removals: each was counted when you first deleted it.',
          'Once nothing waits there, Trovolve Removed leaves Settings.',
        ],
      },
      {
        only: IOS,
        tips: [
          'They wait in the album **Trovolve Removed**, and go to **Recently Deleted**.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'They wait in **Pictures/Trovolve Removed**, and go to the bin.',
        ],
      },
    ],
  },
  {
    id: 'phone-plan',
    only: PHONES,
    title: 'The free plan and Pro',
    summary: 'How many photos the free plan removes each day, what Pro adds, and buying, restoring or cancelling it.',
    keywords: ['price', 'cost', 'pay', 'buy', 'purchase', 'subscription', 'upgrade', 'limit', 'daily', '50', 'trial', 'cancel', 'restore', 'family', 'lifetime', 'monthly', 'yearly', 'premium', 'reminder'],
    sections: [
      {
        text: [
          'Trovolve is free. The free plan removes **50 photos and videos a day**, in Library, Clean up, Swipe to clean, Organize and Safe to delete alike. **Undo** gives back the removal the same day, and the count starts again at midnight. Each device has its own 50. Every question before deleting says how many are left today.',
          'Before you delete more than are left, the **Trovolve Pro** sheet says how many you chose and how many are left. **Delete … now** deletes as many as are left, the largest first; **Not now** deletes nothing.',
        ],
      },
      {
        heading: 'Trovolve Pro',
        tips: [
          'Pro removes as many as you like each day, backs up automatically, and organizes by place and date, with trip folders on your computer. One purchase covers every device signed in to the same store account, and the computers you pair with them; a computer learns of it the next time this device backs up to it.',
          'The plans: **Yearly**, with a 7-day free trial; **Monthly**; and **Lifetime**, paid once. **Family** covers up to 6 people. Prices are the store’s own, in your currency.',
          '**Settings** shows your plan at the top; tap it for **Subscription**, with what each plan includes, the free removals left today, and the plans.',
          'Starting the free trial turns on its reminder, a notification two days before it ends, and the phone asks first whether Trovolve may send one. **Remind me 2 days before it ends**, in Subscription, turns it off or on again.',
          'Compressing large videos is part of Pro.',
        ],
      },
      {
        only: IOS,
        tips: [
          'Pro is bought through the App Store. **Restore purchases** finds what was bought with the same Apple Account. **Manage in App Store** is where a subscription is changed or cancelled; you keep Pro until it ends.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          'Pro is bought through Google Play. **Restore purchases** finds what was bought with the same Google account. **Manage in Google Play** is where a subscription is changed or cancelled; you keep Pro until it ends.',
        ],
      },
    ],
  },
  {
    id: 'phone-settings',
    only: PHONES,
    title: 'Settings',
    summary: 'Everything under the gear: your plan, the computer, the theme, photo access and the rest.',
    keywords: ['preferences', 'options', 'gear', 'dark mode', 'light mode', 'theme', 'permission', 'access', 'version', 'privacy', 'errors', 'kilometers', 'miles', 'notifications', 'location when sharing', 'support', 'contact', 'email', 'report a problem'],
    sections: [
      {
        tips: [
          'Your plan, at the top, opens **Subscription**.',
          '**Locations**: your computer (paired, with its backup folder, or **Not paired yet**: tap it to connect one), Google Drive, OneDrive and Dropbox, each with a switch, and **Connect to server…** to add a server of your own. Tap one for its own page. **Safe to delete**: **Checked on** any location, or on 2.',
          '**Theme**: **Auto** follows the phone’s own light or dark setting; **Dark** or **Light** keeps one. **Distances**: miles or kilometers, for Organize.',
          '**Clean up**: **Low-storage alerts**, a notification when less than a tenth of the storage is free, at most once a week.',
          '**Photo access** says what Trovolve may see, and opens the phone’s own settings for Trovolve to change it. **Scan again** reads your library again.',
          '**Trovolve Removed**, while anything still waits there from before, says how much, and **Move to …** sends it to where the phone keeps deleted photos for 30 days.',
          '**Recently moved** lists what can still be undone.',
          '**Support**: **Errors** lists what went wrong. **Support** opens the support page: the guide, the errors, **Email support** (an email to Trovolve’s support with its version and your phone’s system already in it, and the last three errors, if any), **Copy details**, and answers to common questions.',
          '**Privacy**: **Remove location when sharing** sets whether **Share photo** starts with **Remove location** on.',
          '**Help** opens this guide. **About Trovolve** shows the version and who makes it, with the support page, the terms and what others made that Trovolve uses. **Privacy policy** opens the privacy policy.',
        ],
      },
      {
        only: IOS,
        tips: [
          '**Photo access** says **Full access**, **Some chosen** or **None**.',
        ],
      },
      {
        only: ANDROID,
        tips: [
          '**Photo access** says **All photos and videos**, **Some chosen** or **None**.',
        ],
      },
    ],
  },
  {
    id: 'phone-trouble',
    only: PHONES,
    title: 'If something looks wrong',
    summary: 'What the commonest puzzles mean, and what to do about them.',
    keywords: ['problem', 'error', 'not working', 'stuck', 'help', 'fix', 'broken', 'missing', 'permission', 'cannot find'],
    sections: [
      {
        heading: 'Trovolve cannot see my photos',
        text: [
          'You said no when the phone asked. In **Settings**, **Photo access** opens the phone’s own settings for Trovolve: allow all photos, and come back.',
        ],
      },
      {
        heading: 'Clean up misses copies',
        text: [
          'With access to only some photos, Trovolve checks only those. Allow them all, or **Choose more** in Library. **Scan again**, in Settings, then reads the library again.',
        ],
      },
      {
        heading: 'The computer is not found',
        text: [
          'Trovolve must be open on the computer, and both must be on the same Wi-Fi. To pair, **Pair a phone** must be open in **Phones** on the computer. If scanning the code does not find it, try **Enter the code instead**.',
        ],
      },
      {
        heading: 'A backup stopped',
        text: [
          'The phone or the computer heard nothing from the other for a minute, as when the phone left the Wi-Fi or its screen locked. What arrived whole is kept: **Try again** carries on from there.',
        ],
      },
      {
        heading: 'A photo was left where it is',
        text: [
          'The message says why. Nothing else was changed for it: a move or deletion happens in full or not at all. The reason is also kept under **Errors**, in Settings.',
        ],
      },
    ],
  },
];

const shownOn = (platform: GuidePlatform) => (part: Only) => !part.only || part.only.includes(platform);

/** The guide as one platform shows it: its own topics, each with its own sections. */
export function guideFor(platform: GuidePlatform): GuideTopic[] {
  const shown = shownOn(platform);
  return GUIDE.filter(shown).map(topic => ({ ...topic, sections: topic.sections.filter(shown) }));
}

const plain = (topic: GuideTopic) => [topic.title, topic.summary, ...topic.keywords, ...topic.sections.flatMap(s => [s.heading ?? '', ...(s.text ?? []), ...(s.steps ?? []), ...(s.tips ?? [])])]
  .join(' ').replaceAll('**', '').replaceAll('’', "'").toLowerCase();

/**
 * One platform's topics that mention every word looked for, or are known by it, in the guide's own order; all of
 * them when nothing is looked for.
 */
export function searchGuide(query: string, platform: GuidePlatform): GuideTopic[] {
  const words = query.toLowerCase().replaceAll('’', "'").split(/\s+/).filter(Boolean);
  return guideFor(platform).filter(topic => { const text = plain(topic); return words.every(word => text.includes(word)); });
}
