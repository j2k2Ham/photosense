// Shared DTO-type definitions aligned with backend Domain / Application contracts.
export interface BaseDuplicateGroupDto {
  key: string;
  photos: PhotoDto[];
}
export interface ExactDuplicateGroupDto extends BaseDuplicateGroupDto {
  perceptual: false;
}
export interface NearDuplicateGroupDto extends BaseDuplicateGroupDto {
  perceptual: true;
  distance: number; // guaranteed for near
}
export type DuplicateGroupDto = ExactDuplicateGroupDto | NearDuplicateGroupDto;

export interface ExactGroupsPageDto {
  mode: 'exact'; page: number; pageSize: number; total: number; totalPages: number; unfilteredTotal: number; items: ExactDuplicateGroupDto[];
}
export interface NearGroupsPageDto {
  mode: 'near'; threshold: number; page: number; pageSize: number; total: number; totalPages: number; unfilteredTotal: number; items: NearDuplicateGroupDto[];
}
export type GroupsPageDto = ExactGroupsPageDto | NearGroupsPageDto;

export interface PhotoDto {
  id: string;
  fileName: string;
  sourcePath: string;
  fileSizeBytes: number;
  contentHash?: string;
  perceptualHash?: string;
  takenOn?: string;
  cameraModel?: string;
  latitude?: number;
  longitude?: number;
  set: 'Primary' | 'Secondary';
  categories?: string[];
  kept?: boolean;
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

export interface StartScanRequest {
  primaryLocation: string;
  secondaryLocation?: string;
  recursive: boolean;
  hammingThreshold: number;
}
