// Shared DTO-type definitions aligned with backend contracts (PhotoSense.Contracts).
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

export type MatchKind = 'identical' | 'samePicture' | 'similar';

export interface GroupMemberDto {
  photo: PhotoDto;
  match: MatchKind;
  /** Why the group's keeper was preferred over this photo. */
  keeperReason: string;
}

export interface DuplicateGroupDto {
  key: string;
  /** The best copy: the one that stays. */
  keeper: PhotoDto;
  members: GroupMemberDto[];
  reclaimableBytes: number;
}

export type GroupMode = 'duplicates' | 'similar';

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

export interface BulkRemovalResultDto {
  removed: number;
  bytes: number;
  skipped: number;
  /** Sidecars and Live Photo videos moved along with the files they belonged to. */
  companions: number;
  problems: string[];
}

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
}

/** A folder on the computer the service runs on. */
export interface FolderDto {
  name: string;
  /** The full path, as the service sees it. */
  path: string;
}

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
}

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

export interface OrganizeClashDto {
  /** The file that is going. */
  id: string;
  existing: OrganizeExistingDto[];
  /** The first name with a number that is free there. */
  nextFree: string;
}

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

export interface OrganizeApplyRequest extends OrganizeDestination {
  mode: 'move' | 'copy';
  /** Bring Live Photo videos and edit files along with their pictures. */
  companions: boolean;
  /** What the folder is called in the record of what was done. */
  label: string;
  files: { id: string; name: string; subfolder?: string }[];
}

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

export interface EraseRemovedDto {
  erased: number;
  bytes: number;
  /** Files that could not be erased and are still there. */
  skipped: number;
  problems: string[];
}
