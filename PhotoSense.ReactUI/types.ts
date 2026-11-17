// Shared DTO-type definitions aligned with backend contracts (PhotoSense.Contracts).
/** A photo a scan recorded, as the Clean up screens show it. */
export interface PhotoDto {
  id: string;
  fileName: string;
  sourcePath: string;
  folder: string;
  fileSizeBytes: number;
  width: number;
  height: number;
  format?: string;
  /** A video: matched only as an identical file, shown without a picture. */
  isVideo: boolean;
  durationSeconds?: number;
  /** Local time where the picture was taken, without a zone. */
  takenOn?: string;
  cameraModel?: string;
  latitude?: number;
  longitude?: number;
  /** Nearest town to where it was taken, e.g. "Buxton, North Carolina, US" or "Near Butte, Montana, US". */
  placeName?: string;
  set: 'Primary' | 'Secondary' | 'Unknown';
  /** Marked to keep by the user; bulk removal skips it. */
  kept: boolean;
}

/** How sure the match between a photo and the one kept is. */
export type MatchKind = 'identical' | 'samePicture' | 'similar';

/** A photo matched against the one a group keeps. */
export interface GroupMemberDto {
  photo: PhotoDto;
  match: MatchKind;
  /** Why the group's keeper was preferred over this photo. */
  keeperReason: string;
}

/** A photo to keep and the photos matched against it. */
export interface DuplicateGroupDto {
  key: string;
  /** The best copy: the one that stays. */
  keeper: PhotoDto;
  members: GroupMemberDto[];
  reclaimableBytes: number;
}

/** Which groups are listed: exact copies and conversions, or look-alikes to review. */
export type GroupMode = 'duplicates' | 'similar';

/** One page of the groups a scan found. */
export interface GroupsPageDto {
  mode: GroupMode;
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
  /** Files a bulk removal would take, across every duplicate group. */
  removableCount: number;
  reclaimableBytes: number;
  items: DuplicateGroupDto[];
}

/** What removing the copies of one or more groups did. */
export interface BulkRemovalResultDto {
  removed: number;
  bytes: number;
  skipped: number;
  /** Sidecars and Live Photo videos moved along with the files they belonged to. */
  companions: number;
  problems: string[];
  /** What to undo the removal by; absent when no file was moved. */
  batchId?: string | null;
}

/** What removing one file did. */
export interface RemovePhotoResultDto {
  /** Where the file now is, when it was moved. */
  heldAt: string | null;
  companions: number;
  /** What to undo the removal by; absent when no file was moved. */
  batchId: string | null;
}

/** How far a scan has got, and what is left by what earlier scans took. */
export interface ScanProgressSnapshotDto {
  instanceId: string;
  startedUtc: string;
  completedUtc?: string;
  primaryTotal: number;
  primaryProcessed: number;
  secondaryTotal: number;
  secondaryProcessed: number;
  primaryPercent: number;
  secondaryPercent: number;
  overallPercent: number;
  /** Seconds the scan has left, going by earlier scans and then by its own pace; absent while there is nothing to go by. */
  secondsLeft?: number | null;
  /** The ids of the latest pictures read that have a preview, oldest first. */
  recent?: string[];
}

/** A folder on the computer the service runs on. */
export interface FolderDto {
  name: string;
  /** The full path, as the service sees it. */
  path: string;
}

/** The folders inside one folder, or the places to start from. */
export interface FolderListingDto {
  /** The folder that was listed; absent when the starting places are listed instead. */
  path?: string | null;
  /** The folder it is in; absent at the top of a disk. */
  parent?: string | null;
  folders: FolderDto[];
}

/** What the service holds, and the scan it last ran since it was started. */
export interface ScanStatusDto {
  instanceId: string;
  completed?: string | null;
  /** Files on record from every scan so far. */
  totalPhotos: number;
}

/** The folders to scan, whether their subfolders are looked in, and whether earlier results are forgotten first. */
export interface StartScanRequest {
  primaryLocation: string;
  secondaryLocation?: string;
  recursive: boolean;
  /** Forget what earlier scans recorded and read every file again. */
  startOver?: boolean;
}

// ---- Organize

/** Where files were taken: a town, or a landmark or area within reach of one. */
export interface OrganizePlaceDto {
  town: string;
  /** The region the town is in ("Montana"); the country where the list has no region for it. */
  state: string;
  country: string;
  /** A landmark or area known at this spot: a park, a district, a sight. Absent when none is. */
  area?: string | null;
  latitude: number;
  longitude: number;
}

/** A picture or video in the folder being organized. */
export interface OrganizeFileDto {
  /** Stands for the file in requests about it. It changes when the file is moved. */
  id: string;
  name: string;
  folder: string;
  sizeBytes: number;
  isVideo: boolean;
  /** When it was taken, as the camera's clock had it; failing that, when the file was last changed. No zone. */
  date: string;
  /** Nothing in the file says when it was taken, so the date is the file's own. */
  dateFromFile: boolean;
  width: number;
  height: number;
  durationSeconds?: number | null;
  /** Which of the listing's places it was taken at; absent with no position, or none near a known place. */
  place?: number | null;
  /** The video half of a Live Photo whose picture is beside it: it goes wherever the picture goes. Told once the whole folder is read. */
  livePhoto?: boolean;
}

/** Every picture and video under a folder, once the folder has been read. */
export interface OrganizeFilesDto {
  root: string;
  /** Every file's details were already on record from a scan, so nothing had to be read. */
  fromScan: boolean;
  places: OrganizePlaceDto[];
  files: OrganizeFileDto[];
}

/** How far the reading of a folder has got, with the files read since the asker last heard. */
export interface OrganizeProgressDto {
  /** The folder is being read at the moment. */
  reading: boolean;
  /** Pictures and videos found in it. */
  total: number;
  /** How many of them have been gone through so far. */
  done: number;
  /** Tells one reading of the folder from the next: what was had of another reading is no part of this one. */
  readingId: string | null;
  /** How many files read so far come before the ones in this answer. */
  from: number;
  /** More files have been read than this answer holds: ask again at once. */
  more: boolean;
  /** Files read since the ones the asker said it had, in the order they were read. */
  files: OrganizeFileDto[];
  /** Places the asker has not had yet; a file's place is its number among all the places of the reading. */
  places: OrganizePlaceDto[];
}

/** Where a set of files is to go. */
export interface OrganizeDestination {
  /** The folder the new folder is made in. */
  basePath: string;
  /** The new folder's name; "\" makes subfolders. Not used when the files go straight into the base folder. */
  folderName: string;
  /** Put the files in the base folder itself, without a new folder. */
  direct: boolean;
}
/** A file to plan for, with the subfolder of the destination it goes to when the folder is split inside. */
export interface OrganizePlanFile { id: string; subfolder?: string; }

/** A file already in the folder another is going to, under the name it has or that name with a number. */
export interface OrganizeExistingDto {
  id: string;
  name: string;
  sizeBytes: number;
  date: string;
  width: number;
  height: number;
  isVideo: boolean;
  placeName?: string | null;
  /** Byte for byte the same file as the one arriving. */
  identical: boolean;
}

/** A file whose name is taken where it would go. */
export interface OrganizeClashDto {
  /** The file that is going. */
  id: string;
  existing: OrganizeExistingDto[];
  /** The first name with a number that is free there. */
  nextFree: string;
}

/** Where each file would go and which names are taken there; nothing has moved. */
export interface OrganizePlanDto {
  /** The folder the files go to, before any split by year. */
  destination: string;
  /** That folder is already there. */
  exists: boolean;
  /** Where each file goes, in the order they were asked about. */
  items: { id: string; folder: string }[];
  clashes: OrganizeClashDto[];
  /** Every file name already in each folder files are going to: nothing arriving may take one of them. */
  taken: Record<string, string[]>;
  /** Live Photo videos and edit files that belong to the files and are not among them. */
  companions: number;
}

/** Files to move or copy into a folder, each under the name it arrives with, with or without the files that go with them. */
export interface OrganizeApplyRequest extends OrganizeDestination {
  mode: 'move' | 'copy';
  /** Bring Live Photo videos and edit files along with their pictures. */
  companions: boolean;
  /** What the folder is called in the record of what was done. */
  label: string;
  files: { id: string; name: string; subfolder?: string }[];
}

/** What a move, copy or removal did. */
export interface OrganizeApplyDto {
  batchId: string;
  done: number;
  bytes: number;
  companions: number;
  /** Given another name on arrival so that nothing was replaced. */
  renamed: number;
  skipped: number;
  problems: string[];
  /** Where each file now is (for a copy, where the copy is): the files asked for that went, one that went along with its picture among them. */
  items: { id: string; path: string }[];
}

/** One move or copy that can still be undone. */
export interface OrganizeBatchDto {
  id: string;
  /** The folder the files went to; for files that were deleted, the folder they were taken out of. */
  label: string;
  mode: 'move' | 'copy' | 'remove';
  count: number;
  bytes: number;
  utc: string;
}

/** What undoing a move, copy or removal did. */
export interface OrganizeUndoDto {
  restored: number;
  skipped: number;
  problems: string[];
}

// ---- Removed files

/** A folder that holds removed files. */
export interface RemovedFolderDto {
  path: string;
  files: number;
  bytes: number;
}

/** Everything that has been removed and not yet erased. */
export interface RemovedFilesDto {
  /** The folders that hold removed files; one that holds none is not listed. */
  folders: RemovedFolderDto[];
  files: number;
  bytes: number;
}

/** What erasing did. */
export interface EraseRemovedDto {
  erased: number;
  bytes: number;
  /** Files that could not be erased and are still there. */
  skipped: number;
  problems: string[];
}
/** What PhotoSense says about itself under About in the menu (PhotoSense.Contracts.About.AboutDto). */
export interface AboutDto {
  name: string;
  /** The answering program's own version: 1.2.0.57 for an ordinary build, 1.2.0 for a release. */
  version: string;
  /** Which program is answering: "server" on a computer, "iphone" on an iPhone or iPad. */
  program: string;
  publisher: string;
  copyright: string;
  /** Where to write for help; null until one is set in product.json. */
  supportEmail: string | null;
  website: string | null;
  privacyPolicyUrl: string | null;
  termsUrl: string | null;
  supportUrl: string | null;
}

/** The documents About shows: the privacy policy, terms of use and support page in Markdown, and the third-party notices. */
export type AboutDocument = 'privacy' | 'terms' | 'support' | 'notices';
/** How much of a phone's photos PhotoSense may see (PhotoSense.Contracts.Library.PhotoAccessDto); on a computer, always everything. */
export interface PhotoAccessDto {
  access: 'everything' | 'selected' | 'nothing';
}

/** Where sending photos from this phone to a computer has got to (PhotoSense.Contracts.Transfer.SendStateDto). */
export interface SendStateDto {
  stage: 'idle' | 'waiting' | 'comparing' | 'sending' | 'sent' | 'failed';
  /** The number to compare with the computer's, while it is being compared: "482 917". */
  code: string | null;
  computer: string | null;
  /** Files the computer has said are safely there. */
  done: number;
  total: number;
  bytes: number;
  totalBytes: number;
  /** The names of the pictures and videos every file of which is safely on the computer, which can be deleted from the phone. */
  deletable: string[];
  /** The folder on the computer the files went to, when they all went to one. */
  where: string | null;
  /** What this phone is called, as the computer shows it. */
  phone: string;
  problem: string | null;
  /** The name of the file going to the computer now; none between files. */
  current: string | null;
}
/** How many pictures and videos were deleted from the phone. */
export interface DeleteSentDto {
  deleted: number;
}
/** A phone offering photos, found by this computer. */
export interface FoundPhoneDto {
  key: string;
  name: string;
  via: 'wifi' | 'usb';
}
/** The phones offering photos now. */
export interface FoundPhonesDto {
  phones: FoundPhoneDto[];
}
/** Where receiving photos from a phone has got to (PhotoSense.Contracts.Transfer.ReceiveStateDto). */
export interface ReceiveStateDto {
  stage: 'idle' | 'connecting' | 'comparing' | 'receiving' | 'done' | 'failed';
  code: string | null;
  phone: string | null;
  /** Files that have arrived, those already there among them. */
  done: number;
  total: number;
  bytes: number;
  totalBytes: number;
  /** Files of which the very same file was in the folder already, so that nothing was written. */
  alreadyThere: number;
  folder: string | null;
  problem: string | null;
}

/** Which plan someone has. */
export type PlanTier = 'free' | 'trial' | 'pro' | 'family';
/** How Pro is paid for. */
export type PlanKind = 'yearly' | 'monthly' | 'lifetime' | 'family';
/** A plan for sale, priced as the store says (PhotoSense.Contracts.Plans.PlanOfferDto). */
export interface PlanOfferDto {
  kind: PlanKind;
  /** The price as the store writes it, such as "$19.99". */
  price: string;
  /** The price as a number, in `currency`. */
  amount: number;
  /** The currency's ISO 4217 code, such as "USD". */
  currency: string;
}
/** The plan, the removals left today, and the plans for sale (PhotoSense.Contracts.Plans.PlanDto). */
export interface PlanDto {
  tier: PlanTier;
  kind: PlanKind | null;
  /** When the plan or its trial began (ISO 8601). */
  started: string | null;
  /** When it renews or a trial ends (ISO 8601); null when it never does. */
  until: string | null;
  /** The phone that told this computer of the plan. */
  sharedBy: string | null;
  /** "App Store" or "Google Play"; null on a computer. */
  store: string | null;
  removalsPerDay: number;
  removedToday: number;
  /** How many more can be removed today; null when there is no limit. */
  leftToday: number | null;
  offers: PlanOfferDto[];
}
/** How buying a plan ended, and the plan then (PhotoSense.Contracts.Plans.PlanBuyResultDto). */
export interface PlanBuyResultDto {
  outcome: 'bought' | 'cancelled' | 'pending';
  plan: PlanDto;
}

/** Where pairing a phone with a computer has got to (PhotoSense.Contracts.Transfer.PairingStateDto). */
export interface PairingStateDto {
  /** A computer: idle, waiting, comparing, paired or failed. A phone: idle, scanning, finding, connecting, comparing, paired or failed. */
  stage: 'idle' | 'waiting' | 'scanning' | 'finding' | 'connecting' | 'comparing' | 'paired' | 'failed';
  /** On a computer while waiting: the QR code's text. */
  link: string | null;
  /** On a computer while waiting: the six digits to type on the phone. */
  code: string | null;
  /** On a computer while waiting: when the codes change (ISO 8601). */
  expires: string | null;
  /** While comparing: the number to compare, "482 917". */
  check: string | null;
  /** The other device: the phone on a computer, the computer on a phone. */
  other: string | null;
  /** On a phone once paired: where the computer puts its backups. */
  folder: string | null;
  problem: string | null;
}
/** What the scans so far have found to free, by kind (PhotoSense.Contracts.Duplicates.FindingsDto). */
export interface FindingsDto {
  items: { kind: 'duplicates' | 'similar' | 'largeVideos'; count: number; bytes: number }[];
}

/** How a phone backs up by itself, and what Safe to delete keeps (PhotoSense.Contracts.Transfer.BackupSettingsDto). */
export interface BackupSettingsDto {
  automatic: boolean;
  onlyWhileCharging: boolean;
  includeVideos: boolean;
  keepFavorites: boolean;
  keepRecent: boolean;
}

/** How backing up stands on the phone (PhotoSense.Contracts.Transfer.BackupStateDto). */
export interface BackupStateDto {
  stage: 'unpaired' | 'ready' | 'running' | 'paused' | 'failed';
  computer: string | null;
  folder: string | null;
  waiting: number;
  waitingBytes: number;
  onComputer: number;
  done: number;
  total: number;
  bytes: number;
  totalBytes: number;
  lastChecked: string | null;
  lastBackup: string | null;
  strip: { id: string; done: boolean }[];
  problem: string | null;
  settings: BackupSettingsDto;
}

/** Which of the library's pictures and videos are on the computer, and which wait (PhotoSense.Contracts.Transfer.LibraryBackupDto). */
export interface LibraryBackupDto {
  computer: string | null;
  onComputer: string[];
  waiting: string[];
  lastChecked: string | null;
}

/** A number of pictures and videos, and how much they take (PhotoSense.Contracts.Transfer.ItemCountDto). */
export interface ItemCountDto {
  count: number;
  bytes: number;
}

/** What Safe to delete could delete, and what the keep rules keep (PhotoSense.Contracts.Transfer.SafeReviewDto). */
export interface SafeReviewDto {
  computer: string | null;
  folder: string | null;
  lastChecked: string | null;
  checked: ItemCountDto;
  favorites: ItemCountDto;
  recent: ItemCountDto;
  removable: ItemCountDto;
  storageTotal: number;
  storageFree: number;
  cloudPhotos: boolean;
  settings: BackupSettingsDto;
}

/** What Safe to delete deleted (PhotoSense.Contracts.Transfer.SafeRemovalDto). */
export interface SafeRemovalDto {
  removed: number;
  bytes: number;
  left: number;
  storageTotal: number;
  storageFreeBefore: number;
}

/** A phone that backs up to this computer, as Phones shows it (PhotoSense.Contracts.Transfer.BackupPhoneDto). */
export interface BackupPhoneDto {
  id: string;
  name: string;
  kind: string | null;
  folder: string;
  path: string;
  paused: boolean;
  paired: string;
  lastBackup: string | null;
  lastChecked: string | null;
  held: number;
  heldBytes: number;
  safe: number;
  safeBytes: number;
  arriving: { done: number; total: number; bytes: number; totalBytes: number; started: string; arrived: string[] } | null;
  events: { at: string; text: string }[];
}
