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

export interface StartScanRequest {
  primaryLocation: string;
  secondaryLocation?: string;
  recursive: boolean;
  /** Forget what earlier scans recorded and read every file again. */
  startOver?: boolean;
}
