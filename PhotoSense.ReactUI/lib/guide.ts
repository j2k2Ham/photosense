// The user guide, as the Help window shows it. Text between ** and ** names something on screen and is shown bold.

/** A place in PhotoSense a topic of the guide can take the reader to. */
export type GuidePlace = 'scan' | 'duplicates' | 'similar' | 'organize' | 'settings';

export interface GuideSection {
  heading?: string;
  /** Paragraphs. */
  text?: string[];
  /** Things to do, in order. */
  steps?: string[];
  /** Things worth knowing, in no order. */
  tips?: string[];
}

export interface GuideTopic {
  id: string;
  title: string;
  /** What the topic is about, in a line. */
  summary: string;
  /** The button that takes the reader to what the topic describes. */
  go?: { label: string; place: GuidePlace };
  sections: GuideSection[];
}

export const GUIDE: GuideTopic[] = [
  {
    id: 'start',
    title: 'Getting started',
    summary: 'What PhotoSense does, and how to find your way around it.',
    sections: [
      {
        text: [
          'PhotoSense tidies a folder of pictures and videos in two ways. **Clean up** finds the copies of each picture or video and helps you remove all but the best one. **Organize** sorts the files of a folder into folders by where and when they were taken.',
          'Everything happens on this computer. Nothing is uploaded.',
          'Nothing you delete is erased there and then. A deleted file is moved to a folder called **_PhotoSense_Removed**, where it waits, and can be put back, until you choose **Delete permanently**.',
        ],
      },
      {
        heading: 'Finding your way',
        tips: [
          'The switch at the top left, **Clean up | Organize**, moves between the two areas. Each keeps what you were doing in it.',
          'The menu, the three lines at the top right, has the theme, **Errors**, **Change folders or rescan**, **Clear results**, **Settings**, **Help** and **Delete permanently**.',
          'Messages appear at the bottom right. One that offers **Undo** stays a little longer; an error is also kept under **Errors** until you clear it.',
        ],
      },
      {
        heading: 'A good first run',
        steps: [
          'Scan the folder your pictures are in.',
          'Look through the **Duplicates** and delete the copies you do not need.',
          'Look through the **Similar** shots, a group at a time.',
          'Switch to **Organize** and sort what is left into folders.',
          'When you are sure nothing is missed, use **Delete permanently** to free the space.',
        ],
      },
    ],
  },
  {
    id: 'scan',
    title: 'Scanning for duplicates',
    summary: 'Choosing the folders, starting a scan, and what happens while it runs.',
    go: { label: 'Go to the scan screen', place: 'scan' },
    sections: [
      {
        steps: [
          'Open **Clean up**. The first time, it shows the scan screen; later, press **Change or rescan** at the top, or choose **Change folders or rescan** in the menu.',
          'Give the **Root folder**: type its path, or press **Browse** and pick it. The folders listed are those of the computer PhotoSense runs on.',
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
          '**Start over**, on the scan screen, forgets what earlier scans recorded and reads every file again.',
          '**Clear results**, in the menu, forgets it all without scanning.',
          'Neither touches your pictures, or anything already in **_PhotoSense_Removed**.',
          'Files in **_PhotoSense_Removed** are never scanned, so a deleted copy does not turn up as a duplicate again.',
        ],
      },
    ],
  },
  {
    id: 'duplicates',
    title: 'Reviewing duplicates',
    summary: 'Looking at each picture beside its copies, and removing the copies.',
    go: { label: 'Show the duplicates', place: 'duplicates' },
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
          'Under them is a strip of every copy. Click one to show it beside the original; hover over it for its name, date, folder and where it was taken.',
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
          'Every question asked before a removal names the file that goes and the file that stays: two files holding one picture look alike, and are two files all the same.',
          'The last copy of a picture is never removed as a duplicate. If the original has been moved, changed or deleted since the scan, its copies are left alone; scan again to bring the results up to date.',
          'A picture’s Live Photo video and edit file go with it, when they belong to that picture and to no other.',
          'If the Duplicates tab is empty, the scan found no copies. Anything that only looks alike is under **Similar**.',
        ],
      },
    ],
  },
  {
    id: 'similar',
    title: 'Similar shots',
    summary: 'Burst frames and edited versions: pictures that look alike but are not copies.',
    go: { label: 'Show the similar shots', place: 'similar' },
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
          'The question asked first says that these are not copies of one file. They go to **_PhotoSense_Removed** like anything else, so a shot you miss can be put back.',
        ],
      },
    ],
  },
  {
    id: 'organize',
    title: 'Organize: suggested folders',
    summary: 'Sorting a folder by place and date, with the map and the ways to narrow what is shown.',
    go: { label: 'Open Organize', place: 'organize' },
    sections: [
      {
        steps: [
          'Switch to **Organize**. It starts with the folder you last scanned; **Choose root folder** takes any other. No scan is needed.',
          'Wait while the folder is read. Only the date and the place of each file are read. A large folder takes minutes the first time, and the files appear as they are read; after that it is quick.',
          'Choose how to group, on the right: **Group by** Place or Date, and **Then by** what each folder is split by inside. Place then Year gives folders such as Butte\\2022; Date then Place gives 2022\\Butte.',
          'For places, **Nearby places** decides whether places within 1, 5, 15, 25 or 50 miles of each other share a folder, and **Folder names** how a place’s folder is named.',
          'Each card is a folder that would be made. Click a card to see its files alone.',
        ],
      },
      {
        heading: 'The map',
        tips: [
          'With the folders grouped by place, the map shows each as a dot. Click a dot to see that folder’s files.',
          'Click the map itself, or the button in its corner, to open it large. A dot clicked there shows its files and closes the large map.',
          'When a Google Maps key is set, the map is Google’s, with **Map** and **Satellite**; otherwise it is a plain map drawn by PhotoSense. The **Map** switch above the settings hides it.',
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
    title: 'Organize: folders of your own',
    summary: 'Making your own folders and putting files in them.',
    go: { label: 'Open Organize', place: 'organize' },
    sections: [
      {
        steps: [
          'Under **Your folders**, press **New folder** and name it. A \\ in the name makes a folder inside a folder, as in Trips\\Glacier 2022.',
          'Select files and press the **Add here** button on the folder’s card (it says how many, as in Add 3 here), or drag files onto the card.',
          'Or the other way round: select files first, then press **New folder from these** in the bar under the files.',
        ],
      },
      {
        tips: [
          'Files in your own folders are left out of the suggested folders. A file can also be dragged onto a suggested folder.',
          'Nothing is moved by any of this. A folder exists on disk only once you preview it and confirm.',
          'What you have arranged but not carried out lives in the page, and is gone if the page is loaded again.',
        ],
      },
    ],
  },
  {
    id: 'move',
    title: 'Organize: previewing and moving',
    summary: 'Checking a folder before anything moves, names that are already taken, and undoing.',
    go: { label: 'Open Organize', place: 'organize' },
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
          'The **Recently moved** button, at the foot of the panel on the right, says how many moves can still be undone and opens a list of what was moved, copied or deleted, each with **Undo**. Moved files go back under the names they had, and the folders made for them go again if that leaves them empty. **Done**, the **×** in its corner or **Esc** closes the list.',
        ],
      },
    ],
  },
  {
    id: 'select',
    title: 'Organize: selecting and deleting files',
    summary: 'Selecting one file, a range or all of them, and deleting what is selected.',
    go: { label: 'Open Organize', place: 'organize' },
    sections: [
      {
        tips: [
          'Click a file to select it; click it again to let it go.',
          'Hold **Shift** and click to select everything from the last file you clicked to this one.',
          '**Select all**, above the files, selects every file being shown, on every page; turned off, it lets them go. With a folder picked, a search or a filter on, that is only the files those leave.',
          'Double-click a file to open it in the computer’s own viewer or player.',
        ],
      },
      {
        heading: 'What can be done with the selection',
        text: [
          'A bar appears under the files as soon as one is selected.',
        ],
        tips: [
          '**New folder from these** makes a folder of your own with the selected files in it.',
          'The bin deletes the selected files. It asks first, then moves them to **_PhotoSense_Removed** inside the folder being organized, with any Live Photo videos and edit files that belong only to them. **Undo** puts them back.',
          '**Clear** lets every file go.',
        ],
      },
    ],
  },
  {
    id: 'removed',
    title: 'Removed files and deleting permanently',
    summary: 'Where deleted files wait, how to put one back, and how to erase them for good.',
    go: { label: 'Open Settings', place: 'settings' },
    sections: [
      {
        text: [
          'Whatever you delete, in Clean up or in Organize, is moved to a folder called **_PhotoSense_Removed** inside the folder it was in: the scanned folder, or the folder being organized. Each file keeps its place inside that folder, so that it can be found again.',
        ],
      },
      {
        heading: 'Putting a file back',
        tips: [
          'In Organize, press **Undo** in the message, or open **Recently moved** and press **Undo** beside the deletion.',
          'Otherwise, move the file out of **_PhotoSense_Removed** back to where it was, with the computer’s own file manager. Scan again for Clean up to list it once more.',
        ],
      },
      {
        heading: 'Deleting permanently',
        steps: [
          'Choose **Delete permanently** in the menu.',
          'A warning lists every **_PhotoSense_Removed** folder that holds files, with how many and how much. Nothing has been erased yet.',
          'Press **Delete permanently**. A second, small question asks **Are you sure?**',
          'Press **Delete permanently** there. Only now are the files erased.',
        ],
        tips: [
          'This cannot be undone. The files do not go to the Recycle Bin, and Undo can no longer bring them back.',
          'The second question has a switch, **Do not ask me this second time again**. **Settings**, in the menu, turns the question back on.',
          'A file that cannot be erased, one that is open in another program for instance, is left, and the message says so.',
        ],
      },
    ],
  },
  {
    id: 'menu',
    title: 'Settings, theme and errors',
    summary: 'What the menu holds besides the things described elsewhere.',
    go: { label: 'Open Settings', place: 'settings' },
    sections: [
      {
        tips: [
          '**Theme** switches between dark and light.',
          '**Errors** lists everything that went wrong, with the time, until you clear it. While there are any, a red count shows at the top of the page.',
          '**Settings** has the switch for the second question before deleting permanently. Settings are kept in this browser.',
          '**Help** opens this guide.',
        ],
      },
    ],
  },
  {
    id: 'trouble',
    title: 'If something looks wrong',
    summary: 'What the commonest puzzles mean, and what to do about them.',
    sections: [
      {
        heading: 'A red bar says the PhotoSense service is not answering',
        text: [
          'The page is still open but the program behind it has stopped, so what is shown may be out of date. Start PhotoSense again (dev-start.ps1) and press **Retry**.',
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
          'Folders are those of the computer PhotoSense runs on, by their full path. Use **Browse** to pick one. A disk that is not plugged in is not found either.',
        ],
      },
      {
        heading: 'A file was left where it is',
        text: [
          'It is open in another program, or it has changed since the scan. Close the program, or scan again, and try once more. The reason is kept under **Errors**.',
        ],
      },
      {
        heading: 'The map is a plain one',
        text: [
          'Google’s map needs a key, set when PhotoSense is started, and a connection to Google. Without either, the plain map is shown; it works the same way.',
        ],
      },
    ],
  },
];

const plain = (topic: GuideTopic) => [topic.title, topic.summary, ...topic.sections.flatMap(s => [s.heading ?? '', ...(s.text ?? []), ...(s.steps ?? []), ...(s.tips ?? [])])]
  .join(' ').replaceAll('**', '').toLowerCase();
const TEXT = new Map(GUIDE.map(topic => [topic.id, plain(topic)]));

/** The topics that mention every word looked for, in the guide's own order; all of them when nothing is looked for. */
export function searchGuide(query: string): GuideTopic[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  return GUIDE.filter(topic => words.every(word => TEXT.get(topic.id)!.includes(word)));
}
