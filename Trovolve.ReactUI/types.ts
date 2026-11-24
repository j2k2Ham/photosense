// Shared DTO-type definitions aligned with backend contracts (Trovolve.Contracts).
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
  /** What to undo the removal by; absent when no file was moved, or when they went to a phone's bin. */
  batchId?: string | null;
  /** The photos kept in place of the copies that went, by path, so that the message can name what stays. */
  stays: string[];
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
  /** One of Organize's suggestions, by place or by date, which only Pro moves; files the person chose move on any plan. */
  suggested?: boolean;
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
  /** Its files go to a bin they can be got back from; otherwise, on a drive with none, they are erased for good. */
  recyclable: boolean;
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
  /** Of the files that went, those that went to a bin they can be got back from. */
  recycled: number;
}
/** What Trovolve says about itself under About in the menu (Trovolve.Contracts.About.AboutDto). */
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
  /** Where Trovolve for Mac and PC is downloaded from; null until the web site has it. */
  downloadUrl: string | null;
}

/** The documents About shows: the privacy policy, terms of use and support page in Markdown, and the third-party notices. */
export type AboutDocument = 'privacy' | 'terms' | 'support' | 'notices';
/** How much of a phone's photos Trovolve may see (Trovolve.Contracts.Library.PhotoAccessDto); on a computer, always everything. */
export interface PhotoAccessDto {
  access: 'everything' | 'selected' | 'nothing';
}

/** Where sending photos from this phone to a computer has got to (Trovolve.Contracts.Transfer.SendStateDto). */
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
/** Where receiving photos from a phone has got to (Trovolve.Contracts.Transfer.ReceiveStateDto). */
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
/** A plan for sale, priced as the store says (Trovolve.Contracts.Plans.PlanOfferDto). */
export interface PlanOfferDto {
  kind: PlanKind;
  /** The price as the store writes it, such as "$19.99". */
  price: string;
  /** The price as a number, in `currency`. */
  amount: number;
  /** The currency's ISO 4217 code, such as "USD". */
  currency: string;
}
/** The plan, the removals left today, and the plans for sale (Trovolve.Contracts.Plans.PlanDto). */
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
  /** In a development build, the plan chosen for testing; null in any other build, which offers none. */
  testPlan: TestPlan | null;
}
/** A plan a development build can test as (Trovolve.Domain.Entities.TestPlan): "asBought" is the plan as bought. */
export type TestPlan = 'asBought' | 'free' | 'trial' | 'monthly' | 'yearly' | 'lifetime';
/** How buying a plan ended, and the plan then (Trovolve.Contracts.Plans.PlanBuyResultDto). */
export interface PlanBuyResultDto {
  outcome: 'bought' | 'cancelled' | 'pending';
  plan: PlanDto;
}
/** Whether the person accepted the note asking for a backup, here or on a device paired with this one (Trovolve.Contracts.Plans.TermsDto). */
export interface TermsDto {
  /** The day it was accepted ("2026-11-23"); null when it has not been. */
  accepted: string | null;
}

/** Where pairing a phone with a computer has got to (Trovolve.Contracts.Transfer.PairingStateDto). */
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
  /** On a computer: what it is called, as phones show it. */
  name?: string | null;
}
/** What the scans so far have found to free, by kind (Trovolve.Contracts.Duplicates.FindingsDto). */
export interface FindingsDto {
  items: { kind: 'duplicates' | 'similar' | 'largeVideos'; count: number; bytes: number }[];
}

/** How a phone backs up by itself, and what Safe to delete keeps (Trovolve.Contracts.Transfer.BackupSettingsDto). */
export interface BackupSettingsDto {
  automatic: boolean;
  onlyWhileCharging: boolean;
  includeVideos: boolean;
  keepFavorites: boolean;
  keepRecent: boolean;
}

/** How backing up stands on the phone (Trovolve.Contracts.Transfer.BackupStateDto). */
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

/** Which of the library's pictures and videos are on the computer, and which wait (Trovolve.Contracts.Transfer.LibraryBackupDto). */
export interface LibraryBackupDto {
  computer: string | null;
  onComputer: string[];
  waiting: string[];
  lastChecked: string | null;
}

/** A number of pictures and videos, and how much they take (Trovolve.Contracts.Transfer.ItemCountDto). */
export interface ItemCountDto {
  count: number;
  bytes: number;
}

/** What Safe to delete could delete, and what the keep rules keep (Trovolve.Contracts.Transfer.SafeReviewDto). */
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

/** What Safe to delete deleted (Trovolve.Contracts.Transfer.SafeRemovalDto). */
export interface SafeRemovalDto {
  removed: number;
  bytes: number;
  left: number;
  storageTotal: number;
  storageFreeBefore: number;
}

/** A computer a phone forgot (`POST backup/forget`), and whether the computer heard and forgot the phone too (Trovolve.Contracts.ForgottenComputerDto). */
export interface ForgottenComputerDto {
  computer: string;
  /** False when the computer was not found: it still lists the phone until it is forgotten there. */
  told: boolean;
}

/** A phone that backs up to this computer, as Phones shows it (Trovolve.Contracts.Transfer.BackupPhoneDto). */
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
  /** How many trip folders were made here when it asked. */
  tripFolders: number;
  /** Where its trip folders are made, as the person knows it: "Pictures/Trovolve". */
  tripsFolder: string;
  /** The same, whole, for the folder picker to start from. */
  tripsPath: string;
  /** How its copies are named here. */
  names: FileNaming;
  arriving: { done: number; total: number; bytes: number; totalBytes: number; started: string; arrived: string[] } | null;
  events: { at: string; text: string }[];
}

/** A photo or video of Swipe to clean: of today's set, or marked to delete on a day before (Trovolve.Contracts.Organizing.SwipeCardDto). */
export interface SwipeCardDto {
  /** Its id, as Library knows it. */
  id: string;
  /** What it takes, a Live Photo's video included. */
  bytes: number;
  /** Every file of it is on the computer, checked. */
  onComputer: boolean;
  /** Marked to delete (true), kept (false), or not swiped yet (null). */
  delete: boolean | null;
  /** Marked to delete, then tapped on the summary to be kept after all. */
  spared: boolean;
}

/** A day of the last week, and whether something was swiped on it (Trovolve.Contracts.Organizing.SwipeDayDto). */
export interface SwipeDayDto {
  /** "2026-11-17". */
  date: string;
  swiped: boolean;
}

/** What a deletion at the end of a swipe session did (Trovolve.Contracts.Organizing.SwipeFreedDto). */
export interface SwipeFreedDto {
  deleted: number;
  bytes: number;
  /** How many stay marked for another day. */
  left: number;
}

/** How Swipe to clean stands today (Trovolve.Contracts.Organizing.SwipeStateDto). */
export interface SwipeStateDto {
  date: string;
  /** Today's set, in the order shown. */
  set: SwipeCardDto[];
  /** Marked to delete on days before, and not deleted yet. */
  earlier: SwipeCardDto[];
  /** The last seven days, today last. */
  week: SwipeDayDto[];
  /** Days in a row something was swiped. */
  streak: number;
  /** What today's last deletion deleted; null before one. */
  freed: SwipeFreedDto | null;
  /** Photos and videos never swiped, today's set aside. */
  unswiped: number;
}

/** A category of Clean up beside duplicates and similar shots (Trovolve.Contracts.Cleaning). */
export type CategoryName = 'largeVideos' | 'screenshots' | 'bursts' | 'blurry' | 'messaging' | 'duplicates';

/** How many of a category there are, and what they take (Trovolve.Contracts.Cleaning.CategorySummaryDto). */
export interface CategorySummaryDto {
  category: CategoryName;
  /** Pictures or videos; for bursts, the shots that would go. */
  count: number;
  bytes: number;
}

/** What Clean up's categories hold (Trovolve.Contracts.Cleaning.CategoryReportDto). */
export interface CategoryReportDto {
  /** Largest first. */
  categories: CategorySummaryDto[];
  /** Pictures no scan has measured the sharpness of yet. */
  unmeasured: number;
  /** A phone's storage; null on a computer. */
  storageTotal: number | null;
  storageFree: number | null;
}

/** A burst, every shot in the order taken, with the one kept (Trovolve.Contracts.Cleaning.BurstDto). */
export interface BurstDto {
  keeper: string;
  shots: PhotoDto[];
}

/** What deleting from Clean up's categories did (Trovolve.Contracts.Cleaning.CategoryRemovalDto). */
export interface CategoryRemovalDto {
  deleted: number;
  bytes: number;
  skipped: number;
  problems: string[];
  /** On a computer, what Undo puts them back by; null on a phone, where they went to its own bin. */
  batchId: string | null;
}

/** How small a compressed video is made: its own frame, 1080 lines or 720 lines, as HEVC. */
export type CompressionQuality = 'high' | 'balanced' | 'small';

/** About what a video's copy takes at each quality (Trovolve.Contracts.Cleaning.CompressionEstimatesDto); null where a copy is not worth making, or its frame or length is not known. */
export type CompressionEstimatesDto = Record<CompressionQuality, number | null>;

/** A large video a phone can compress, with about what its copy would take (Trovolve.Contracts.Cleaning.CompressibleVideoDto). */
export interface CompressibleVideoDto {
  video: PhotoDto;
  estimates: CompressionEstimatesDto;
}

/** A video compressed: what the original took, and what its copy takes (Trovolve.Contracts.Cleaning.CompressedVideoDto). */
export interface CompressedVideoDto {
  id: string;
  fileName: string;
  before: number;
  after: number;
}

/** How compressing stands on a phone (Trovolve.Contracts.Cleaning.CompressionStateDto). */
export interface CompressionStateDto {
  stage: 'idle' | 'running' | 'done' | 'stopped' | 'failed';
  quality: CompressionQuality;
  total: number;
  done: number;
  /** The name of the video being compressed now. */
  current: string | null;
  /** How far through them all, from 0 to 1. */
  progress: number;
  results: CompressedVideoDto[];
  /** What the copies saved, all told. */
  saved: number;
  /** Once it has ended, whether the originals went to the phone's bin; null while running, or when none was compressed. */
  originalsDeleted: boolean | null;
  /** Why it failed. */
  problem: string | null;
}

/** A phone's notifications: which are on, and whether the phone lets Trovolve send any (Trovolve.Contracts.Cleaning.NoticeStateDto). */
export interface NoticeStateDto {
  lowStorage: boolean;
  trialReminder: boolean;
  allowed: boolean;
}

/** What a notification tapped asked to open, once (Trovolve.Contracts.Cleaning.NoticeOpenedDto). */
export interface NoticeOpenedDto {
  opens: 'free-up-space' | 'subscription' | 'backup' | 'swipe' | 'search' | null;
}

/** A town photos were taken in during the year, with how many (Trovolve.Contracts.Cleaning.YearPlaceDto). */
export interface YearPlaceDto {
  town: string;
  photos: number;
}

/** A trip of the year, as Year in review lists it (Trovolve.Contracts.Cleaning.YearTripDto). */
export interface YearTripDto {
  name: string;
  /** The month it started, 1 to 12. */
  month: number;
  photos: number;
}

/** Year in review: the year due now, or null, and that year in photos (Trovolve.Contracts.Cleaning.YearReviewDto). */
export interface YearReviewDto {
  /** December for the year ending, January for the year just ended; null the rest of the year. */
  year: number | null;
  photos: number;
  videos: number;
  /** How many trips started that year. */
  trips: number;
  /** The three trips with the most photos, in the order they were taken. */
  topTrips: YearTripDto[];
  /** January first. */
  months: number[];
  /** 1 to 12; 0 when none was taken. */
  busiestMonth: number;
  busiestPlace: YearPlaceDto | null;
  freed: number;
  removed: number;
  longestStreak: number;
  sessions: number;
}

/** What a search names, part by part (Trovolve.Contracts.Cleaning.SearchPartDto). */
export interface SearchPartDto {
  kind: 'inPhoto' | 'place' | 'month' | 'year';
  /** The words, as written. */
  text: string;
}

/** What a search found, latest first, up to 500 (Trovolve.Contracts.Cleaning.SearchResultDto). */
export interface SearchResultDto {
  parts: SearchPartDto[];
  photos: PhotoDto[];
  /** How many match in all. */
  total: number;
  /** How many of those that match are videos; the rest are pictures. */
  videos: number;
  /** It named things in photos, which only Pro searches: the free plan matched its date and place alone. */
  proNeeded: boolean;
}

/** What the viewer shows beside a picture or video (Trovolve.Contracts.Cleaning.PhotoDetailsDto). */
export interface PhotoDetailsDto {
  /** The camera it was taken with; null when not known. */
  camera: string | null;
  /** It is a favorite in the phone's own photos app. */
  favorite: boolean;
}

/** A town, home or one that could be (Trovolve.Contracts.Organize.TownDto). */
export interface TownDto {
  town: string;
  /** The state, province or county it is in; empty when not known. */
  region: string;
  country: string;
  latitude: number;
  longitude: number;
  /** The days photos were taken there; 0 where it does not matter. */
  days: number;
}

/** A trip, as the list and the map show it (Trovolve.Contracts.Organize.TripDto). */
export interface TripDto {
  id: string;
  /** "Glacier National Park", "Big Sky, Montana", "Anaconda and Butte". */
  name: string;
  /** The folder it becomes on the computer: "2026-07 Glacier National Park". */
  folder: string;
  /** The day it started, "2026-07-18". */
  start: string;
  end: string;
  /** Its pictures and videos. */
  photos: number;
  /** How many of them are videos. */
  videos: number;
  latitude: number;
  longitude: number;
  cover: PhotoDto;
  /** The computer has made its folder. */
  folderMade: boolean;
  /** Trovolve's own name for it, which `name` is unless the person named it. */
  suggested: string;
}

/** The trip folder being made on the computer, or the last one asked for (Trovolve.Contracts.Organize.TripFolderDto). */
export interface TripFolderDto {
  /** The trip. */
  id: string;
  stage: 'running' | 'ready' | 'failed';
  problem: string | null;
  /** Where the computer made it: "Pictures/Trovolve/2026-07 Glacier National Park". */
  made: string | null;
}

/** Trips: home, the trips, the latest first, and the computer their folders are made on (Trovolve.Contracts.Organize.TripsDto). */
export interface TripsDto {
  /** Null when no photo says where it was taken. */
  home: TownDto | null;
  /** The person chose home; otherwise Trovolve worked it out. */
  homeChosen: boolean;
  trips: TripDto[];
  /** The computer the phone backs up to; null when it has been paired with none. */
  computer: string | null;
  /** Where the computer makes trip folders, as it last said ("Pictures/Trovolve"); null until it has said. */
  tripsFolder: string | null;
  folder: TripFolderDto | null;
}

/** A day of a trip (Trovolve.Contracts.Organize.TripDayDto). */
export interface TripDayDto {
  /** "2026-07-18". */
  date: string;
  photos: PhotoDto[];
}

/** A trip with its days (Trovolve.Contracts.Organize.TripDetailDto). */
export interface TripDetailDto {
  trip: TripDto;
  days: TripDayDto[];
}

/** How a phone's copies are named on the computer: as on the phone, by date, or by date and place. */
export type FileNaming = 'asOnPhone' | 'date' | 'datePlace';

/** A kind of fix, as Fix dates and places shows it (Trovolve.Contracts.Organize.FixGroupDto). */
export interface FixGroupDto {
  /** How many photos and videos, a Live Photo once. */
  count: number;
  /** The first few, to show. */
  photos: PhotoDto[];
  /** For wrong dates: the date most of them have, "2000-01-01". */
  date: string | null;
  /** For places: the place most of them would get. */
  place: string | null;
  placeCount: number;
  /** How many the last fix of this kind changed, which Undo puts back; 0 when there is nothing to undo. */
  undo: number;
}

/** The phone's copies on the computer, and how they are named there (Trovolve.Contracts.Organize.ComputerNamesDto). */
export interface ComputerNamesDto {
  computer: string;
  /** How many of the phone's files it holds. */
  files: number;
  style: FileNaming;
  /** An example: a name on the phone, "IMG_4821.HEIC", and the same by date; null when no photo says when it was taken. */
  now: string | null;
  after: string | null;
  /** Renaming asked from the phone; null when it was not asked since Trovolve started. */
  stage: 'running' | 'ready' | 'failed' | 'paused' | null;
  problem: string | null;
  renamed: number | null;
}

/** Fix dates and places (Trovolve.Contracts.Organize.FixesDto). */
export interface FixesDto {
  /** How many photos and videos need a date or a place, each once. */
  total: number;
  dates: FixGroupDto;
  places: FixGroupDto;
  /** Null when the phone has been paired with no computer. */
  names: ComputerNamesDto | null;
}

/** How many a fix, or its Undo, changed (Trovolve.Contracts.Organize.FixResultDto). */
export interface FixResultDto {
  changed: number;
}

/** An item of the hidden vault, as its grid shows it once unlocked (Trovolve.Contracts.Organize.VaultItemDto). */
export interface VaultItemDto {
  id: string;
  /** Its name, as Library showed it. */
  name: string;
  /** When it was taken, as the camera's clock had it; null when nothing said. */
  taken: string | null;
  isVideo: boolean;
  /** What its files take. */
  bytes: number;
  /** The computer holds it, sealed. */
  onComputer: boolean;
}

/** How the hidden vault stands, and, once unlocked, what it holds (Trovolve.Contracts.Organize.VaultDto). */
export interface VaultDto {
  stage: 'off' | 'locked' | 'open';
  /** How many items it holds. */
  count: number;
  /** How many the computer does not hold yet, which deleting Trovolve would delete. */
  notBackedUp: number;
  /** The computer backups go to; null before one is paired. */
  computer: string | null;
  /** What it holds, the latest hidden first; empty while locked. */
  items: VaultItemDto[];
  /** Hidden items being brought back from the computer, or the last time they were; null when they were not since Trovolve started. */
  restore: { stage: 'running' | 'ready' | 'failed' | 'paused' | 'unpaired'; problem: string | null; restored: number | null } | null;
}

/** On a computer: what renaming a phone's copies would make of their names (Trovolve.Contracts.Organize.RenamingDto). */
export interface RenamingDto {
  count: number;
  rows: { now: string; after: string }[];
}

/** What sort of place a location is. */
export type LocationKind = 'computer' | 'googleDrive' | 'oneDrive' | 'dropbox' | 'amazonS3' | 's3Compatible' | 'azureBlob' | 'webDav' | 'sftp';

/** A place the phone backs up to, as the page lists it (`LocationDto`). */
export interface LocationDto {
  /** "computer", "google-drive", or an id of its own for one added. */
  id: string;
  kind: LocationKind;
  /** The service's name, or the name the person gave one they added. */
  name: string;
  on: boolean;
  /** Signed in to, paired or added: it can be turned on. */
  connected: boolean;
  /** It can be signed in to from this version of Trovolve. */
  available: boolean;
  account: string | null;
  /** How it is reached, less its secrets. */
  settings: Record<string, string>;
  /** Files it holds, checked. */
  copies: number;
  lastBackup: string | null;
  lastChecked: string | null;
  problem: string | null;
}

/** How far one location has got in what the locations are doing. */
export interface LocationProgressDto {
  id: string;
  done: number;
  total: number;
  bytes: number;
  totalBytes: number;
  problem: string | null;
}

/** What sending to a location did. */
export interface SendResultDto {
  locationId: string;
  items: number;
  /** Those every file of which matches the copy there. */
  matched: number;
  bytes: number;
}

/** What the locations are doing, or last did. */
export interface LocationActivityDto {
  task: 'backup' | 'check' | 'send';
  stage: 'running' | 'ready' | 'paused' | 'failed';
  locations: LocationProgressDto[];
  sent: SendResultDto | null;
}

/** The locations, the Safe to delete rule, and what they are doing (`GET /api/locations`). */
export interface LocationsDto {
  locations: LocationDto[];
  /** On how many locations a copy must be checked: 1 or 2. */
  countAt: number;
  pro: boolean;
  activity: LocationActivityDto | null;
  /** On the free plan, the id of the location backups have gone to, which the plan is held to; null on Pro or before the first backup. */
  held: string | null;
}

/** A location in full (`GET /api/locations/{id}`). */
export interface LocationDetailsDto {
  location: LocationDto;
  where: string | null;
  space: { total: number; used: number } | null;
  problem: string | null;
}

/** A location as Connect to server adds or changes it. */
export interface ServerConnectionDto {
  id: string | null;
  kind: LocationKind;
  name: string;
  settings: Record<string, string>;
  secrets: Record<string, string>;
}
